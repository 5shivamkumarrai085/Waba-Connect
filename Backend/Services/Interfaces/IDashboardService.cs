using System.Threading.Tasks;
using WhatsAppCampaignApi.Models.DTOs;

namespace WhatsAppCampaignApi.Services.Interfaces
{
    public interface IDashboardService
    {
        Task<DashboardDto?> GetDashboardDataAsync();
    }
}
