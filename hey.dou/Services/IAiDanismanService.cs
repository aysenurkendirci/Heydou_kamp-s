using hey.dou.Models;

namespace hey.dou.Services
{
    public interface IAiDanismanService
    {
        Task<AiDanismanResult> GetAnswerAsync(string question);
    }
}