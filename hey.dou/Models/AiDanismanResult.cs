namespace hey.dou.Models
{
    public enum AiIntent
    {
        AcademicCalendar,
        Cafeteria,
        Internships,
        Polls,
        Events,
        Unknown
    }

    public class AiDanismanResult
    {
        public string Answer { get; set; } = "";
        public AiIntent Intent { get; set; } = AiIntent.Unknown;
    }
}