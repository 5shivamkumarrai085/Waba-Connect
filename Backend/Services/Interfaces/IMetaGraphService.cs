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
        
        /// <summary>
        /// Subscribes the Meta App to receive webhooks for a specific WABA.
        /// Must be called for each WABA after connecting to ensure Meta delivers webhooks.
        /// </summary>
        Task<bool> SubscribeAppToWabaAsync(string wabaId, string accessToken);
        
        /// <summary>
        /// Checks if the Meta App is already subscribed to receive webhooks for a WABA.
        /// </summary>
        Task<bool> IsAppSubscribedToWabaAsync(string wabaId, string accessToken);

        /// <summary>
        /// Fetches the configured Webhook Callback URL directly from Meta Graph API for a Meta App / WABA connection.
        /// </summary>
        Task<string?> FetchWebhookUrlFromMetaAsync(string appId, string appSecret, string? wabaId = null, string? accessToken = null);
    }
}
