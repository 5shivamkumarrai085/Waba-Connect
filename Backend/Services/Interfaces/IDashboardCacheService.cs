using System.Threading.Tasks;

namespace WhatsAppCampaignApi.Services.Interfaces;

public interface IDashboardCacheService
{
    Task<object> GetSummaryAsync(string timeFilter);
    void InvalidateCache();
}
