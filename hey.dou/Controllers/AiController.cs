using hey.dou.Models;
using hey.dou.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace hey.dou.Controllers
{
    public class AiDanismanController : Controller
    {
        private readonly IAiDanismanService _aiService;

        public AiDanismanController(IAiDanismanService aiService)
        {
            _aiService = aiService;
        }

        [HttpGet]
        public IActionResult Index()
        {
            if (HttpContext.Session.GetInt32("UserId") == null)
                return RedirectToAction("Login", "Account");

            return View(new AiDanismanViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(AiDanismanViewModel model)
        {
            if (HttpContext.Session.GetInt32("UserId") == null)
                return RedirectToAction("Login", "Account");

            if (string.IsNullOrWhiteSpace(model.Question))
            {
                model.Error = "Lütfen bir soru yaz.";
                return View(model);
            }

            try
            {
                var result = await _aiService.GetAnswerAsync(model.Question);
                model.Answer = result.Answer;
                model.RelatedPages = GetRelatedPages(result.Intent);
            }
            catch
            {
                model.Error = "Cevap alınırken bir hata oluştu. Lütfen daha sonra tekrar dene.";
            }

            return View(model);
        }

        private static List<RelatedPageLink> GetRelatedPages(AiIntent intent)
        {
            return intent switch
            {
                AiIntent.AcademicCalendar => new()
                {
                    new RelatedPageLink { Controller = "AkademikTakvim", Action = "Index", Label = "Akademik Takvim", Icon = "calendar_month" }
                },

                AiIntent.Cafeteria => new()
                {
                    new RelatedPageLink { Controller = "Yemekhane", Action = "Index", Label = "Yemekhane", Icon = "restaurant_menu" }
                },

                AiIntent.Internships => new()
                {
                    new RelatedPageLink { Controller = "Staj", Action = "Index", Label = "Staj İlanları", Icon = "work" }
                },

                AiIntent.Polls => new()
                {
                    new RelatedPageLink { Controller = "Anket", Action = "Index", Label = "Anketler", Icon = "poll" }
                },

                AiIntent.Events => new()
                {
                    new RelatedPageLink { Controller = "Etkinlik", Action = "Index", Label = "Etkinlikler", Icon = "event" }
                },

                _ => new()
                {
                    new RelatedPageLink { Controller = "AkademikTakvim", Action = "Index", Label = "Akademik Takvim", Icon = "calendar_month" },
                    new RelatedPageLink { Controller = "Yemekhane", Action = "Index", Label = "Yemekhane", Icon = "restaurant_menu" },
                    new RelatedPageLink { Controller = "Staj", Action = "Index", Label = "Staj İlanları", Icon = "work" }
                }
            };
        }
    }
}