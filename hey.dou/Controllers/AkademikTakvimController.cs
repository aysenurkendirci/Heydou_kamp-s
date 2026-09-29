using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using hey.dou.Models;

namespace hey.dou.Controllers
{
    public class AkademikTakvimController : Controller
    {
        private readonly HeydouContext _context;

        public AkademikTakvimController(HeydouContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(int? year, int? month)
        {
            var today = DateTime.Today;
            int currentYear = year ?? today.Year;
            int currentMonth = month ?? today.Month;

            var firstDay = new DateTime(currentYear, currentMonth, 1);
            var lastDay = firstDay.AddMonths(1).AddDays(-1);

            var prev = firstDay.AddMonths(-1);
            var next = firstDay.AddMonths(1);

            ViewBag.CurrentYear = currentYear;
            ViewBag.CurrentMonth = currentMonth;
            ViewBag.PrevYear = prev.Year;
            ViewBag.PrevMonth = prev.Month;
            ViewBag.NextYear = next.Year;
            ViewBag.NextMonth = next.Month;

            var events = await _context.AkademikTakvims
                .Where(e =>
                    e.BaslangicTarihi <= DateOnly.FromDateTime(lastDay) &&
                    e.BitisTarihi >= DateOnly.FromDateTime(firstDay))
                .OrderBy(e => e.BaslangicTarihi)
                .ToListAsync();

            // Bu hafta ne var?
            var startOfWeek = today.AddDays(-(int)today.DayOfWeek + (int)DayOfWeek.Monday);
            var endOfWeek = startOfWeek.AddDays(6);

            ViewBag.BuHaftaEtkinlikler = await _context.AkademikTakvims
                .Where(e => e.EtkinlikMi &&
                           e.BaslangicTarihi >= DateOnly.FromDateTime(startOfWeek) &&
                           e.BaslangicTarihi <= DateOnly.FromDateTime(endOfWeek))
                .OrderBy(e => e.BaslangicTarihi)
                .ToListAsync();

            return View(events);
        }

        [HttpGet]
        public async Task<IActionResult> GetEventsByDate(string date)
        {
            if (!DateOnly.TryParse(date, out var selectedDate))
                return Content("<p class='text-red-500 text-sm'>Geçersiz tarih.</p>", "text/html");

            var today = DateOnly.FromDateTime(DateTime.Today);

            var events = await _context.AkademikTakvims
                .Where(e => e.BaslangicTarihi <= selectedDate && e.BitisTarihi >= selectedDate)
                .OrderBy(e => e.BaslangicTarihi)
                .ToListAsync();

            if (!events.Any())
            {
                events = await _context.AkademikTakvims
                    .Where(e => e.BaslangicTarihi >= today)
                    .OrderBy(e => e.BaslangicTarihi)
                    .Take(5)
                    .ToListAsync();

                if (!events.Any())
                    return Content("<p class='text-gray-400 text-sm'>Yaklaşan etkinlik bulunamadı.</p>", "text/html");
            }

            var html = string.Join("", events.Select(ev =>
            {
                var renk = string.IsNullOrWhiteSpace(ev.RenkKodu)
                    ? (ev.Kategori?.Contains("Sınav") == true ? "#f59e0b"
                    : ev.Kategori?.Contains("Tatil") == true ? "#10b981"
                    : ev.Kategori?.Contains("Kayıt") == true ? "#ef4444"
                    : ev.EtkinlikMi ? "#8b5cf6" : "#94a3b8")
                    : ev.RenkKodu;

                var detayLink = ev.EtkinlikMi
                    ? $"<a href='/AkademikTakvim/EtkinlikDetay/{ev.EventId}' class='text-xs text-blue-500 hover:underline mt-1 inline-block'>Detayları Gör →</a>"
                    : "";

                return $@"
                    <div class='flex items-start gap-3 p-3 rounded-xl hover:bg-slate-50 dark:hover:bg-slate-800/20'>
                        <div class='h-3 w-3 rounded-full mt-1.5 flex-shrink-0' style='background-color:{renk}'></div>
                        <div class='flex-1'>
                            <div class='flex items-center gap-2 mb-1'>
                                <p class='font-semibold text-sm'>{ev.Kategori}</p>
                                {(ev.EtkinlikMi ? "<span class='text-[10px] bg-purple-100 text-purple-700 px-2 py-0.5 rounded-full'>ETKİNLİK</span>" : "")}
                            </div>
                            <p class='text-xs text-gray-600 dark:text-gray-400'>{ev.Aciklama}</p>
                            {(!string.IsNullOrWhiteSpace(ev.Konum) ? $"<p class='text-[11px] text-gray-400 flex items-center gap-1 mt-1'><span class='material-symbols-rounded' style='font-size:12px'>location_on</span>{ev.Konum}</p>" : "")}
                            <p class='text-[11px] text-gray-400 mt-1'>
                                {ev.BaslangicTarihi:dd.MM.yyyy} - {ev.BitisTarihi:dd.MM.yyyy}
                            </p>
                            {detayLink}
                        </div>
                    </div>";
            }));

            return Content(html, "text/html");
        }

        public async Task<IActionResult> EtkinlikDetay(int id)
        {
            var etkinlik = await _context.AkademikTakvims.FindAsync(id);

            if (etkinlik == null)
                return NotFound();

            return View(etkinlik);
        }
    }
}