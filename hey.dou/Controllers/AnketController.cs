using System;
using System.Linq;
using System.Threading.Tasks;
using hey.dou.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace hey.dou.Controllers
{
    public class AnketController : Controller
    {
        private readonly HeydouContext _context;

        public AnketController(HeydouContext context) // Veritabanı bağlamını başlatır
        {
            _context = context;
        }

        private async Task<Anket?> GetActivePollAsync() // Süresi dolmamış en güncel aktif anketi getirir
        {
            return await _context.Ankets
                .Where(a => a.IsActive && a.EndDate > DateTime.Now)
                .OrderByDescending(a => a.AnketId)
                .FirstOrDefaultAsync();
        }

        private async Task<Anket?> GetPollWithDetailsAsync(int id) // Anketi, seçenekleri ve oylarıyla birlikte detaylıca getirir
        {
            return await _context.Ankets
                .Include(a => a.AnketSecenegis)
                    .ThenInclude(s => s.Oys)
                .FirstOrDefaultAsync(m => m.AnketId == id);
        }

        public async Task<IActionResult> Index() // Herkese tüm anket listesini gösterir
        {
            // Giriş yapılmış mı kontrol et
            if (HttpContext.Session.GetInt32("UserId") == null)
                return RedirectToAction("Login", "Account");

            // Bütün anketleri yeninden eskiye sıralayıp sayfaya gönder
            var anketler = await _context.Ankets.OrderByDescending(a => a.AnketId).ToListAsync();
            return View(anketler);
        }

        public async Task<IActionResult> Create() // Yeni etkinlik öneri sayfasını açar (Sadece Kulüp Başkanı)
        {
            var userIdFromSession = HttpContext.Session.GetInt32("UserId");
            if (HttpContext.Session.GetString("Rol") != "KulupBaskani" || userIdFromSession == null)
            {
                return RedirectToAction(nameof(Index));
            }

            string currentUserID = userIdFromSession.Value.ToString();
            var activePoll = await GetActivePollAsync();

            if (activePoll != null)
            {
                bool alreadyProposed = await _context.AnketSecenegis
                    .AnyAsync(s => s.AnketId == activePoll.AnketId && s.CreatorKullaniciId == currentUserID);

                if (alreadyProposed) return RedirectToAction(nameof(Details), new { id = activePoll.AnketId });
                ViewBag.ActivePollId = activePoll.AnketId;
            }

            return View(new EtkinlikCreateViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(EtkinlikCreateViewModel model) // Önerilen etkinliği kaydeder
        {
            var userIdFromSession = HttpContext.Session.GetInt32("UserId");
            if (HttpContext.Session.GetString("Rol") != "KulupBaskani" || userIdFromSession == null)
                return RedirectToAction("Login", "Account");

            string currentUserID = userIdFromSession.Value.ToString();
            if (!ModelState.IsValid) return View(model);

            var activePoll = await GetActivePollAsync();

            if (activePoll == null)
            {
                activePoll = new Anket
                {
                    Title = "Haftanın Etkinlik Oylaması",
                    Description = "Kulüp başkanlarının önerdiği etkinliklerden birini seçin.",
                    EndDate = DateTime.Now.AddDays(1),
                    IsActive = true
                };
                _context.Ankets.Add(activePoll);
                await _context.SaveChangesAsync();
            }

            var baskaninOnerisi = new AnketSecenegi
            {
                AnketId = activePoll.AnketId,
                SecenekText = model.EtkinlikAdi,
                CreatorKullaniciId = currentUserID,
                KulupAdi = model.KulupAdi,
                KulupBaskaniAdi = model.KulupBaskaniAdi,
                Konum = model.Konum,
                TarihSaat = model.TarihSaat,
                KisaAciklama = model.KisaAciklama
            };
            _context.AnketSecenegis.Add(baskaninOnerisi);

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Details), new { id = activePoll.AnketId });
        }

        public async Task<IActionResult> Details(int? id) // Anketin seçeneklerini ve oylama durumunu görüntüler
        {
            var userIdFromSession = HttpContext.Session.GetInt32("UserId");
            if (userIdFromSession == null) return RedirectToAction("Login", "Account");
            string currentUserID = userIdFromSession.Value.ToString();

            Anket? anket = (id == null)
                ? await GetActivePollAsync()
                : await GetPollWithDetailsAsync(id.Value);

            if (anket == null) return RedirectToAction(nameof(Index));
            if (id == null) anket = await GetPollWithDetailsAsync(anket.AnketId);

            ViewBag.HasVoted = await _context.Oys
                .AnyAsync(o => o.AnketId == anket.AnketId && o.KullaniciId == currentUserID);

            return View(anket);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Vote(int anketId, int secenekId) // Kullanıcının seçtiği seçeneğe oy vermesini sağlar
        {
            var userIdFromSession = HttpContext.Session.GetInt32("UserId");
            if (userIdFromSession == null) return RedirectToAction("Login", "Account");
            string currentUserID = userIdFromSession.Value.ToString();

            var anket = await _context.Ankets.FindAsync(anketId);
            if (anket == null || anket.EndDate < DateTime.Now) return RedirectToAction(nameof(Details), new { id = anketId });

            bool hasVoted = await _context.Oys.AnyAsync(o => o.AnketId == anketId && o.KullaniciId == currentUserID);
            if (hasVoted) return RedirectToAction(nameof(Details), new { id = anketId });

            _context.Oys.Add(new Oy { AnketId = anketId, SecenekId = secenekId, KullaniciId = currentUserID });
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Details), new { id = anketId });
        }
    }
}