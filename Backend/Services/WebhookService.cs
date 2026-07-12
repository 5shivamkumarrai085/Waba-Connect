using System;
using System.Net.Http;
using System.Threading.Tasks;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services
{
    public class WebhookService : IWebhookService
    {
        private readonly IWabaRepository _wabaRepository;
        private readonly IHttpClientFactory _httpClientFactory;

        public WebhookService(IWabaRepository wabaRepository, IHttpClientFactory httpClientFactory)
        {
            _wabaRepository = wabaRepository;
            _httpClientFactory = httpClientFactory;
        }

        public bool VerifyToken(string hubMode, string hubVerifyToken, string hubChallenge, string configuredVerifyToken, out string challenge)
        {
            challenge = string.Empty;
            
            if (hubMode == "subscribe" && hubVerifyToken == configuredVerifyToken)
            {
                challenge = hubChallenge;
                return true;
            }

            return false;
        }

        public async Task<bool> TriggerVerificationAsync(string verifyToken)
        {
            var config = await _wabaRepository.GetAsync();
            if (config == null || string.IsNullOrEmpty(config.WebhookUrl))
            {
                return false;
            }

            // Simulate the GET request Meta makes to verify the webhook
            try
            {
                if (IsLocalWebhookUrl(config.WebhookUrl))
                {
                    return false;
                }

                var client = _httpClientFactory.CreateClient();
                
                string mode = "subscribe";
                string challenge = Guid.NewGuid().ToString();
                
                string separator = config.WebhookUrl.Contains("?") ? "&" : "?";
                string testUrl = $"{config.WebhookUrl}{separator}hub.mode={mode}&hub.verify_token={verifyToken}&hub.challenge={challenge}";
                
                var response = await client.GetAsync(testUrl);
                if (response.IsSuccessStatusCode)
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    return responseBody.Trim() == challenge;
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsLocalWebhookUrl(string webhookUrl)
        {
            return false;
        }
    }
}
