using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using hey.dou.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace hey.dou.Services
{
    public class AiDanismanService : IAiDanismanService
    {
        private readonly HeydouContext _db;
        private readonly IHttpContextAccessor _http;
        private readonly HttpClient _client;

        private readonly string _apiKey;
        private readonly string _model;

        public AiDanismanService(
            HeydouContext db,
            IConfiguration config,
            IHttpContextAccessor httpContextAccessor)
        {
            _db = db;
            _http = httpContextAccessor;

            _apiKey = config["OpenAI:ApiKey"]
                      ?? throw new InvalidOperationException("OpenAI:ApiKey configuration is missing.");

            _model = config["OpenAI:Model"] ?? "gpt-4.1-mini";

            _client = new HttpClient
            {
                BaseAddress = new Uri("https://api.openai.com/")
            };
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        public async Task<AiDanismanResult> GetAnswerAsync(string question)
        {
            question = (question ?? "").Trim();
            if (string.IsNullOrWhiteSpace(question))
            {
                return new AiDanismanResult
                {
                    Answer = "Lütfen bir soru yaz.",
                    Intent = AiIntent.Unknown
                };
            }

            // session -> kişiselleştirme
            var session = _http.HttpContext?.Session;
            var adSoyad = session?.GetString("AdSoyad") ?? "Öğrenci";
            var rol = session?.GetString("Rol") ?? "Ogrenci";

            var intent = DetectIntent(question);

            // 1) DB context
            string portalContext = intent switch
            {
                AiIntent.AcademicCalendar => await BuildAcademicCalendarContext(question),
                AiIntent.Cafeteria => await BuildCafeteriaContext(question),
                AiIntent.Internships => await BuildInternshipsContext(question),
                AiIntent.Polls => await BuildPollsContext(question, rol),
                AiIntent.Events => await BuildEventsContext(question),
                _ => await BuildFallbackContext()
            };

            // 2) system prompt (uydurma kapalı)
            var systemPrompt =
$@"Sen HeyDOU üniversite portalı için Türkçe cevap veren yardımcı asistansın.

KURALLAR:
- SADECE aşağıdaki 'PORTAL_VERISI' içinde bulunan bilgilere dayanarak cevap ver.
- PORTAL_VERISI'nde yoksa 'Bu bilgi portal verisinde yok' de ve kullanıcıyı doğru sayfaya yönlendir:
  Akademik Takvim / Yemekhane / Staj İlanları / Anketler / Etkinlikler.
- Kesin tarih verirken format: dd.MM.yyyy.
- Cevabı kısa, net ve kullanıcıya uygun şekilde yaz.

KULLANICI:
- AdSoyad: {adSoyad}
- Rol: {rol}

PORTAL_VERISI:
{portalContext}
";

            var payload = new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = question }
                },
                max_tokens = 350,
                temperature = 0.2
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _client.PostAsync("v1/chat/completions", content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);

            var answer = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            var finalAnswer = string.IsNullOrWhiteSpace(answer)
                ? "Şu anda cevap veremiyorum, lütfen tekrar dener misin?"
                : answer.Trim();

            return new AiDanismanResult
            {
                Answer = finalAnswer,
                Intent = intent
            };
        }

        // -------------------------
        // INTENT DETECTION
        // -------------------------
        private static AiIntent DetectIntent(string q)
        {
            var s = q.ToLowerInvariant();

            if (s.Contains("yemek") || s.Contains("menü") || s.Contains("menu") || s.Contains("çorba") || s.Contains("corba"))
                return AiIntent.Cafeteria;

            if (s.Contains("staj") || s.Contains("intern"))
                return AiIntent.Internships;

            if (s.Contains("anket") || s.Contains("oylama") || s.Contains("oy"))
                return AiIntent.Polls;

            if (s.Contains("etkinlik") || s.Contains("katılım") || s.Contains("katilim"))
                return AiIntent.Events;

            if (s.Contains("takvim") || s.Contains("vize") || s.Contains("ara sınav") || s.Contains("arasınav") ||
                s.Contains("final") || s.Contains("bütünleme") || s.Contains("butunleme") ||
                s.Contains("ders kayıt") || s.Contains("ders kayit") || s.Contains("ekle") || s.Contains("bırak") || s.Contains("birak"))
                return AiIntent.AcademicCalendar;

            return AiIntent.Unknown;
        }

        // -------------------------
        // CONTEXT BUILDERS (DB)
        // -------------------------
        private async Task<string> BuildAcademicCalendarContext(string question)
        {
            var q = question.ToLowerInvariant();

            string? period = null;
            if (q.Contains("bahar")) period = "Bahar";
            else if (q.Contains("güz") || q.Contains("guz")) period = "Güz";
            else if (q.Contains("yaz")) period = "Yaz";

            var keys = new List<string>();
            if (q.Contains("vize") || q.Contains("ara sınav") || q.Contains("arasınav"))
                keys.AddRange(new[] { "Ara Sinav", "Ara Sınav" });

            if (q.Contains("final"))
                keys.Add("Final");

            if (q.Contains("bütünleme") || q.Contains("butunleme"))
                keys.AddRange(new[] { "Bütünleme", "Butunleme" });

            if (q.Contains("ders kayıt") || q.Contains("ders kayit") || q.Contains("kayıt") || q.Contains("kayit"))
                keys.AddRange(new[] { "Ders Kayit", "Ders Kayıt", "Ders Kayit Tarihleri", "Ders Kayıt Tarihleri" });

            // Belirli tarih sorusu
            if (TryExtractDate(question, out var specificDate))
            {
                var itemsByDate = await _db.AkademikTakvims
                    .Where(e => e.BaslangicTarihi <= specificDate && e.BitisTarihi >= specificDate)
                    .OrderBy(e => e.BaslangicTarihi)
                    .Take(20)
                    .ToListAsync();

                if (itemsByDate.Count == 0)
                    return $"{specificDate:yyyy-MM-dd} tarihinde AkademikTakvim tablosunda kayıt yok.";

                return FormatAcademicCalendar(itemsByDate);
            }

            var query = _db.AkademikTakvims.AsQueryable();

            if (!string.IsNullOrWhiteSpace(period))
                query = query.Where(x => x.Kategori != null && x.Kategori.Contains(period));

            if (keys.Count > 0)
            {
                query = query.Where(x =>
                    keys.Any(k =>
                        (x.Aciklama != null && x.Aciklama.Contains(k)) ||
                        (x.Kategori != null && x.Kategori.Contains(k))
                    ));
            }

            var today = TodayDateOnly();
            var items = await query
                .Where(x => x.BitisTarihi >= today.AddDays(-30))
                .OrderBy(x => x.BaslangicTarihi)
                .Take(15)
                .ToListAsync();

            if (items.Count == 0)
            {
                var upcoming = await _db.AkademikTakvims
                    .Where(x => x.BaslangicTarihi >= today)
                    .OrderBy(x => x.BaslangicTarihi)
                    .Take(10)
                    .ToListAsync();

                if (upcoming.Count == 0)
                    return "AkademikTakvim tablosunda hiç kayıt bulunamadı.";

                return FormatAcademicCalendar(upcoming);
            }

            return FormatAcademicCalendar(items);
        }

        private static string FormatAcademicCalendar(List<AkademikTakvim> items)
        {
            var sb = new StringBuilder();
            sb.AppendLine("AkademikTakvim kayıtları:");
            foreach (var e in items)
            {
                sb.AppendLine($"- {e.BaslangicTarihi:yyyy-MM-dd} → {e.BitisTarihi:yyyy-MM-dd} | Kategori: {e.Kategori ?? "-"} | Açıklama: {e.Aciklama}");
            }
            return sb.ToString();
        }

        private async Task<string> BuildCafeteriaContext(string question)
        {
            // 1) spesifik tarih
            if (TryExtractDate(question, out var specificDate))
            {
                var dayMenu = await _db.HaftalikMenus
                    .Where(m => m.Tarih == specificDate)
                    .OrderByDescending(m => m.Tarih)
                    .FirstOrDefaultAsync();

                if (dayMenu == null)
                    return $"HaftalikMenu tablosunda {specificDate:yyyy-MM-dd} için kayıt yok.";

                return
$@"Seçilen gün menüsü:
- {dayMenu.Tarih:yyyy-MM-dd} ({dayMenu.Gun})
  Çorba: {dayMenu.Corba ?? "-"}
  Ana Yemek: {dayMenu.AnaYemek ?? "-"}
  Yardımcı: {dayMenu.YardimciYemek ?? "-"}
  Tatlı/Meyve: {dayMenu.TatliVeyaMeyve ?? "-"}";
            }

            // 2) hafta aralığı
            DateOnly start, end;
            if (!TryExtractWeekRange(question, out start, out end))
            {
                var today = TodayDateOnly();
                start = GetMonday(today);
                end = start.AddDays(4);
            }
            else
            {
                end = start.AddDays(4);
            }

            var menus = await _db.HaftalikMenus
                .Where(m => m.Tarih >= start && m.Tarih <= end)
                .OrderBy(m => m.Tarih)
                .ToListAsync();

            if (menus.Count == 0)
                return $"HaftalikMenu tablosunda {start:yyyy-MM-dd} - {end:yyyy-MM-dd} için kayıt yok.";

            var sb = new StringBuilder();
            sb.AppendLine($"Haftalık Yemekhane Menüsü ({start:yyyy-MM-dd} → {end:yyyy-MM-dd}):");
            foreach (var m in menus)
            {
                sb.AppendLine($"- {m.Tarih:yyyy-MM-dd} ({m.Gun}) | Çorba: {m.Corba ?? "-"} | Ana: {m.AnaYemek ?? "-"} | Yardımcı: {m.YardimciYemek ?? "-"} | Tatlı/Meyve: {m.TatliVeyaMeyve ?? "-"}");
            }
            return sb.ToString();
        }

        private async Task<string> BuildInternshipsContext(string question)
        {
            var q = question.ToLowerInvariant();
            var query = _db.StajIlanlaris.AsQueryable();

            query = query.Where(i => i.Aktif == null || i.Aktif == true);

            if (q.Contains("remote") || q.Contains("uzaktan"))
                query = query.Where(i => i.Lokasyon.Contains("Remote") || i.Lokasyon.Contains("Uzaktan"));

            var items = await query
                .OrderByDescending(i => i.YayinlanmaTarihi)
                .Take(10)
                .ToListAsync();

            if (items.Count == 0)
                return "Aktif staj ilanı bulunamadı.";

            var sb = new StringBuilder();
            sb.AppendLine("Aktif staj ilanları (ilk 10):");
            foreach (var i in items)
            {
                sb.AppendLine($"- {i.Baslik} | {i.Sirket} | {i.Lokasyon} | Tür: {i.StajTuru} | Son Başvuru: {(i.SonBasvuru.HasValue ? i.SonBasvuru.Value.ToString("yyyy-MM-dd") : "-")} | Link: {i.BasvuruLinki ?? "-"}");
            }
            return sb.ToString();
        }

        private async Task<string> BuildPollsContext(string question, string rol)
        {
            var poll = await _db.Ankets
                .Include(a => a.AnketSecenegis)
                    .ThenInclude(s => s.Oys)
                .Where(a => a.IsActive && a.EndDate > DateTime.Now)
                .OrderByDescending(a => a.AnketId)
                .FirstOrDefaultAsync();

            if (poll == null)
                return "Şu anda aktif anket yok.";

            var sb = new StringBuilder();
            sb.AppendLine("Aktif anket:");
            sb.AppendLine($"- Başlık: {poll.Title}");
            sb.AppendLine($"- Açıklama: {poll.Description ?? "-"}");
            sb.AppendLine($"- Bitiş: {poll.EndDate:yyyy-MM-dd HH:mm}");

            sb.AppendLine("Seçenekler:");
            foreach (var s in poll.AnketSecenegis.OrderBy(x => x.SecenekId))
            {
                sb.AppendLine($"- {s.SecenekId}: {s.SecenekText} | Kulüp: {s.KulupAdi ?? "-"} | Konum: {s.Konum ?? "-"} | Tarih: {(s.TarihSaat.HasValue ? s.TarihSaat.Value.ToString("yyyy-MM-dd HH:mm") : "-")} | Oy: {s.Oys?.Count ?? 0}");
            }

            sb.AppendLine($"Not: Kullanıcının rolü = {rol}");
            return sb.ToString();
        }

        private async Task<string> BuildEventsContext(string question)
        {
            var items = await _db.Events
                .Where(e => e.AnketSonucuSecildi == true)
                .OrderBy(e => e.EventDate)
                .Take(10)
                .ToListAsync();

            if (items.Count == 0)
                return "Anket sonucu seçilmiş aktif etkinlik bulunamadı.";

            var sb = new StringBuilder();
            sb.AppendLine("Seçilmiş etkinlikler (ilk 10):");
            foreach (var e in items)
            {
                sb.AppendLine($"- {e.Title} | {e.EventDate:yyyy-MM-dd HH:mm} | Konum: {e.Location ?? "-"} | Açıklama: {e.Description ?? "-"}");
            }
            return sb.ToString();
        }

        private async Task<string> BuildFallbackContext()
        {
            await Task.CompletedTask;
            return "Portal modülleri: AkademikTakvim, HaftalikMenu, StajIlanlari, Anket/AnketSecenegi/Oy, Events.";
        }

        // -------------------------
        // PARSERS
        // -------------------------
        private static DateOnly TodayDateOnly() => DateOnly.FromDateTime(DateTime.Today);

        private static DateOnly GetMonday(DateOnly date)
        {
            int diff = date.DayOfWeek == DayOfWeek.Sunday ? -6 : DayOfWeek.Monday - date.DayOfWeek;
            return date.AddDays(diff);
        }

        private static bool TryExtractDate(string input, out DateOnly date)
        {
            if (DateOnly.TryParseExact(input, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                return true;

            if (DateOnly.TryParseExact(input, new[] { "dd.MM.yyyy", "d.M.yyyy" }, new CultureInfo("tr-TR"),
                    DateTimeStyles.None, out date))
                return true;

            var tr = new CultureInfo("tr-TR");
            var m = Regex.Match(input.ToLowerInvariant(),
                @"(?<day>\d{1,2})\s*(?<month>ocak|şubat|subat|mart|nisan|mayıs|mayis|haziran|temmuz|ağustos|agustos|eylül|eylul|ekim|kasım|kasim|aralık|aralik)\s*(?<year>\d{4})?");

            if (m.Success)
            {
                int day = int.Parse(m.Groups["day"].Value);
                string monthText = m.Groups["month"].Value
                    .Replace("subat", "şubat")
                    .Replace("agustos", "ağustos")
                    .Replace("eylul", "eylül")
                    .Replace("kasim", "kasım")
                    .Replace("aralik", "aralık");

                int year = m.Groups["year"].Success ? int.Parse(m.Groups["year"].Value) : DateTime.Today.Year;

                var composed = $"{day} {monthText} {year}";
                if (DateTime.TryParse(composed, tr, DateTimeStyles.None, out var dt))
                {
                    date = DateOnly.FromDateTime(dt);
                    return true;
                }
            }

            date = default;
            return false;
        }

        private static bool TryExtractWeekRange(string q, out DateOnly start, out DateOnly end)
        {
            var s = q.ToLowerInvariant();
            var today = TodayDateOnly();

            if (s.Contains("bu hafta"))
            {
                start = GetMonday(today);
                end = start.AddDays(6);
                return true;
            }
            if (s.Contains("gelecek hafta") || s.Contains("önümüzdeki hafta") || s.Contains("onumuzdeki hafta"))
            {
                start = GetMonday(today).AddDays(7);
                end = start.AddDays(6);
                return true;
            }
            if (s.Contains("geçen hafta") || s.Contains("gecen hafta"))
            {
                start = GetMonday(today).AddDays(-7);
                end = start.AddDays(6);
                return true;
            }

            start = default;
            end = default;
            return false;
        }
    }
}