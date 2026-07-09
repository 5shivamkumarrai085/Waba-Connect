using System.Threading.Tasks;
using WhatsAppCampaignApi.Models.Entities;

namespace WhatsAppCampaignApi.Services.Interfaces
{
    public interface IWabaRepository
    {
        Task<WabaConfiguration?> GetAsync();
        Task<WabaConfiguration?> GetByIdAsync(int id);
        Task<WabaConfiguration> AddOrUpdateAsync(WabaConfiguration config);
        Task<bool> DeleteAsync();
    }
}
