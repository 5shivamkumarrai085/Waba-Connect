using System.Threading.Tasks;
using WhatsAppCampaignApi.Models.Entities;

namespace WhatsAppCampaignApi.Services.Interfaces
{
    public interface IBusinessRepository
    {
        Task<Business?> GetAsync();
        Task SaveAsync(Business business);
        Task ClearAllAsync();
    }
}
