using System.Collections.Generic;
using System.Threading.Tasks;
using WhatsAppCampaignApi.Models.Entities;

namespace WhatsAppCampaignApi.Services.Interfaces
{
    public interface IPhoneRepository
    {
        Task<IEnumerable<WabaPhoneNumber>> GetAllAsync();
        Task<WabaPhoneNumber?> GetByNumberIdAsync(string phoneNumberId);
        Task SaveRangeAsync(IEnumerable<WabaPhoneNumber> phoneNumbers);
        Task ClearAllAsync();
    }
}
