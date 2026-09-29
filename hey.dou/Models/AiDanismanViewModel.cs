namespace hey.dou.Models
{
    public class AiDanismanViewModel
    {
        public string Question { get; set; } = string.Empty;
        public string? Answer { get; set; }
        public string? Error { get; set; }

        public List<RelatedPageLink> RelatedPages { get; set; } = new();
    }

    public class RelatedPageLink
    {
        public string Controller { get; set; } = "";
        public string Action { get; set; } = "Index";
        public string Label { get; set; } = "";
        public string Icon { get; set; } = ""; // material-symbols-rounded adı
    }
}