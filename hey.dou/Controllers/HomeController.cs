using System.Diagnostics;
using hey.dou.Models;
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System;

namespace hey.dou.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly HeydouContext _context;

        public HomeController(ILogger<HomeController> logger, HeydouContext context)
        {
            _logger = logger;
            _context = context;
        }

        public IActionResult Index()
        {
            // DÝKKAT: Eðer altý kýrmýzý çizilirse 'Anket' kelimesini 'Ankets' veya 'Anketler' yapýn.
            bool aktifAnketVarMi = _context.Ankets.Any(a => a.EndDate > DateTime.Now);

            ViewBag.AktifAnketVarMi = aktifAnketVarMi;

            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}