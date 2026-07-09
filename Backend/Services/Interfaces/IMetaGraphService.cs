using System.Collections.Generic;
using System.Threading.Tasks;
using WhatsAppCampaignApi.Models.Entities;

namespace WhatsAppCampaignApi.Services.Interfaces
{
    public interface IMetaGraphService
    {
        Task<bool> ValidateAppAsync(string appId, string appSecret);
        Task<Business> GetBusinessDetailsAsync(string wabaId, string accessToken);
        Task<IEnumerable<WabaPhoneNumber>> GetPhoneNumbersAsync(string wabaId, string accessToken);
        Task<bool> SendTemplateMessageAsync(string phoneNumberId, string accessToken, string recipientNumber, string templateName, string languageCode);
        Task<string> DebugTokenAsync(string accessToken, string appId, string appSecret);
    }
}
