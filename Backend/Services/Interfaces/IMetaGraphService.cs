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

        /// <summary>
        /// The same fetch, but reporting why it came back empty.
        ///
        /// <para>
        /// <see cref="GetPhoneNumbersAsync"/> returns an empty list for every failure — an expired
        /// token, a WABA the app cannot see, a network blip — which is fine for the callers that
        /// only want to refresh what they can. It is not fine for a user-triggered repair, where
        /// "no numbers found" sent someone hunting for a missing phone number when the real answer
        /// was that their access token expired twelve days ago.
        /// </para>
        /// </summary>
        Task<MetaPhoneNumbersResult> GetPhoneNumbersDetailedAsync(string wabaId, string accessToken);
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
    /// <summary>
    /// What Meta said about a WABA's phone numbers: the numbers, or the reason there were none.
    /// <paramref name="Error"/> is null when the call succeeded, whatever it returned.
    /// </summary>
    public record MetaPhoneNumbersResult(IReadOnlyList<WabaPhoneNumber> Phones, string? Error);

}