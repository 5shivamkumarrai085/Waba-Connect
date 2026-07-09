using System.Collections.Generic;
using System.Threading.Tasks;
using WhatsAppCampaignApi.Models.Entities;

namespace WhatsAppCampaignApi.Services.Interfaces
{
    public interface IHealthLogRepository
    {
        Task<IEnumerable<HealthLog>> GetRecentLogsAsync(int count = 10);
        Task<HealthLog?> GetLatestLogAsync();
        Task AddLogAsync(HealthLog log);
        Task ClearAllAsync();
    }
}
