using System.Collections.Generic;
using System.Threading.Tasks;
using RansomGuard.Core.Models;

namespace RansomGuard.Service.Services
{
    public interface IHistoryStore
    {
        Task SaveActivityAsync(FileActivity activity);
        Task<List<FileActivity>> GetHistoryAsync(int limit = 100);
        Task SaveThreatAsync(Threat threat);
        Task<List<Threat>> GetActiveThreatsAsync();
        Task UpdateThreatStatusAsync(string path, string status);
    }
}
