using System.Threading.Tasks;
using WhatsAppCampaignApi.Models.Entities;

namespace WhatsAppCampaignApi.Services.Interfaces
{
    public interface IHealthService
    {
        Task<HealthLog> RunHealthCheckAsync();
    }
}
