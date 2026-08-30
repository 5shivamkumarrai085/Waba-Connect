using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Models.Entities;

namespace WhatsAppCampaignApi.Services
{
    public class MetaGraphService : IMetaGraphService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<MetaGraphService> _logger;
        private const string MetaGraphBaseUrl = "https://graph.facebook.com/v18.0";

        public MetaGraphService(IHttpClientFactory httpClientFactory, ILogger<MetaGraphService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        private HttpClient CreateClient(string? accessToken = null)
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            
            if (!string.IsNullOrEmpty(accessToken))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }
            
            return client;
        }

        private bool IsMock(string value)
        {
            if (string.IsNullOrEmpty(value)) return true;
            string lower = value.ToLower();
            return lower.StartsWith("mock") || lower.StartsWith("test") || lower.StartsWith("demo") || value.Length < 10;
        }

        public async Task<bool> ValidateAppAsync(string appId, string appSecret)
        {
            if (IsMock(appId) || IsMock(appSecret))
            {
                return true;
            }

            try
            {
                var client = CreateClient();
                string url = $"{MetaGraphBaseUrl}/oauth/access_token?client_id={appId}&client_secret={appSecret}&grant_type=client_credentials";
                var response = await client.GetAsync(url);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<string> DebugTokenAsync(string accessToken, string appId, string appSecret)
        {
            if (IsMock(accessToken))
            {
                var mockInfo = new
                {
                    data = new
                    {
                        app_id = appId,
                        type = "USER",
                        application = "RMA WABA Portal App",
                        expires_at = DateTime.UtcNow.AddDays(60).Ticks / 10000000,
                        is_valid = true,
                        issued_at = DateTime.UtcNow.AddDays(-2).Ticks / 10000000,
                        scopes = new[]
                        {
                            "whatsapp_business_management",
                            "whatsapp_business_messaging",
                            "whatsapp_business_manage_events",
                            "public_profile"
                        }
                    }
                };
                return JsonSerializer.Serialize(mockInfo);
            }

            try
            {
                var client = CreateClient();
                // To debug token, we need the app access token: appId|appSecret
                string appAccessToken = $"{appId}|{appSecret}";
                string url = $"https://graph.facebook.com/debug_token?input_token={accessToken}&access_token={appAccessToken}";
                
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
                
                // Fallback debug query if appAccessToken query fails, inspect directly
                return "{\"data\":{\"is_valid\":false,\"scopes\":[]}}";
            }
            catch (Exception ex)
            {
                return $"{{\"error\": \"{ex.Message}\"}}";
            }
        }

        public async Task<Business> GetBusinessDetailsAsync(string wabaId, string accessToken)
        {
            if (IsMock(wabaId) || IsMock(accessToken))
            {
                return new Business
                {
                    BusinessId = "biz_" + (string.IsNullOrEmpty(wabaId) ? "123456" : wabaId),
                    BusinessName = "RMA Global Enterprise Inc",
                    Timezone = "Asia/Kolkata",
                    Status = "APPROVED"
                };
            }

            try
            {
                var client = CreateClient(accessToken);
                // Querying the WABA Account node
                string url = $"{MetaGraphBaseUrl}/{wabaId}?fields=id,name,timezone_id,owner_business";
                var response = await client.GetAsync(url);
                
                if (!response.IsSuccessStatusCode)
                {
                    string error = await response.Content.ReadAsStringAsync();
                    throw new HttpRequestException($"Meta Graph API Error: {response.StatusCode} - {error}");
                }

                string content = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                var business = new Business
                {
                    BusinessId = wabaId,
                    BusinessName = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "WABA Business Account" : "WABA Business Account",
                    Timezone = root.TryGetProperty("timezone_id", out var tzProp) ? tzProp.GetString() ?? "UTC" : "UTC",
                    Status = "APPROVED" // Default status if request is successful
                };

                // Check if owner_business info is included
                if (root.TryGetProperty("owner_business", out var ownerProp) && ownerProp.ValueKind == JsonValueKind.Object)
                {
                    if (ownerProp.TryGetProperty("id", out var bizId))
                    {
                        business.BusinessId = bizId.GetString() ?? business.BusinessId;
                    }
                    if (ownerProp.TryGetProperty("name", out var bizName))
                    {
                        business.BusinessName = bizName.GetString() ?? business.BusinessName;
                    }
                }

                return business;
            }
            catch
            {
                // Return a default Business structure rather than crashing to maintain UI usability
                return new Business
                {
                    BusinessId = wabaId,
                    BusinessName = "WhatsApp Business Account (" + wabaId + ")",
                    Timezone = "UTC",
                    Status = "APPROVED"
                };
            }
        }

        /// <summary>
        /// A WABA's phone numbers, or an empty list if anything went wrong.
        ///
        /// Kept lossy for the callers that only want to refresh whatever they can reach — a
        /// background sync should not fail a whole operation because one account is unreachable.
        /// Anything user-triggered should call <see cref="GetPhoneNumbersDetailedAsync"/> instead
        /// and say what actually happened.
        /// </summary>
        public async Task<IEnumerable<WabaPhoneNumber>> GetPhoneNumbersAsync(string wabaId, string accessToken)
        {
            var result = await GetPhoneNumbersDetailedAsync(wabaId, accessToken);
            return result.Phones;
        }

        public async Task<MetaPhoneNumbersResult> GetPhoneNumbersDetailedAsync(string wabaId, string accessToken)
        {
            if (IsMock(wabaId) || IsMock(accessToken))
            {
                return new MetaPhoneNumbersResult(
                    new List<WabaPhoneNumber>
                    {
                        new WabaPhoneNumber
                        {
                            PhoneNumber = "+15550192834",
                            PhoneNumberId = "1098234857203",
                            DisplayName = "RMA Support",
                            VerifiedName = "RMA Global Enterprise Inc",
                            Quality = "GREEN",
                            Status = "APPROVED",
                            MessageLimit = "1000"
                        }
                    },
                    null);
            }

            try
            {
                var client = CreateClient(accessToken);
                string url = $"{MetaGraphBaseUrl}/{wabaId}/phone_numbers";
                var response = await client.GetAsync(url);

                string content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    // Meta's own wording, surfaced rather than flattened. "The token has expired on
                    // 15-Aug-26" tells someone exactly what to do; an empty list tells them nothing
                    // and looks identical to an account that genuinely has no number yet.
                    return new MetaPhoneNumbersResult(new List<WabaPhoneNumber>(), DescribeMetaError(content, response.StatusCode));
                }

                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                var phoneNumbers = new List<WabaPhoneNumber>();

                if (root.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in dataProp.EnumerateArray())
                    {
                        var phone = new WabaPhoneNumber
                        {
                            PhoneNumber = element.TryGetProperty("display_phone_number", out var num) ? num.GetString() ?? "" : "",
                            PhoneNumberId = element.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "",
                            DisplayName = element.TryGetProperty("verified_name", out var dName) ? dName.GetString() ?? "" : "",
                            VerifiedName = element.TryGetProperty("verified_name", out var vName) ? vName.GetString() ?? "" : "",
                            Quality = element.TryGetProperty("quality_rating", out var qual) ? qual.GetString() ?? "GREEN" : "GREEN",
                            Status = element.TryGetProperty("status", out var stat) ? stat.GetString() ?? "APPROVED" : "APPROVED",
                            MessageLimit = "1000" // Default for WABA tier 1
                        };

                        // Clean quality text (e.g. UPPER CASE)
                        phone.Quality = phone.Quality.ToUpper();

                        phoneNumbers.Add(phone);
                    }
                }

                return new MetaPhoneNumbersResult(phoneNumbers, null);
            }
            catch (Exception ex)
            {
                return new MetaPhoneNumbersResult(new List<WabaPhoneNumber>(), $"Could not reach Meta: {ex.Message}");
            }
        }

        /// <summary>
        /// Pulls the human-readable message out of a Graph API error body, falling back to the
        /// status code when the body is not the shape we expect.
        /// </summary>
        private static string DescribeMetaError(string content, System.Net.HttpStatusCode statusCode)
        {
            try
            {
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty("error", out var error)
                    && error.TryGetProperty("message", out var message))
                {
                    var text = message.GetString();
                    if (!string.IsNullOrWhiteSpace(text)) return text!;
                }
            }
            catch
            {
                // Fall through to the status code.
            }

            return $"Meta rejected the request ({(int)statusCode}).";
        }



        public async Task<bool> SendTemplateMessageAsync(string phoneNumberId, string accessToken, string recipientNumber, string templateName, string languageCode)
        {
            if (IsMock(phoneNumberId) || IsMock(accessToken))
            {
                return true;
            }

            try
            {
                var client = CreateClient(accessToken);
                string url = $"{MetaGraphBaseUrl}/{phoneNumberId}/messages";
                
                var payload = new
                {
                    messaging_product = "whatsapp",
                    to = recipientNumber,
                    type = "template",
                    template = new
                    {
                        name = templateName,
                        language = new
                        {
                            code = languageCode
                        }
                    }
                };

                string jsonPayload = JsonSerializer.Serialize(payload);
                var requestContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                
                var response = await client.PostAsync(url, requestContent);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <inheritdoc />
        public async Task<bool> SubscribeAppToWabaAsync(string wabaId, string accessToken)
        {
            if (IsMock(wabaId) || IsMock(accessToken))
            {
                return true;
            }

            try
            {
                var client = CreateClient(accessToken);
                string url = $"{MetaGraphBaseUrl}/{wabaId}/subscribed_apps";
                _logger.LogInformation("Subscribing app to WABA {WabaId} via POST {Url}", wabaId, url);
                
                var response = await client.PostAsync(url, null);
                string responseBody = await response.Content.ReadAsStringAsync();
                
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Successfully subscribed app to WABA {WabaId}. Response: {Response}", wabaId, responseBody);
                    return true;
                }

                _logger.LogError("Failed to subscribe app to WABA {WabaId}: HTTP {Status} - {Response}", wabaId, response.StatusCode, responseBody);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception subscribing app to WABA {WabaId}", wabaId);
                return false;
            }
        }

        /// <inheritdoc />
        public async Task<bool> IsAppSubscribedToWabaAsync(string wabaId, string accessToken)
        {
            if (IsMock(wabaId) || IsMock(accessToken))
            {
                return true;
            }

            try
            {
                var client = CreateClient(accessToken);
                string url = $"{MetaGraphBaseUrl}/{wabaId}/subscribed_apps";
                var response = await client.GetAsync(url);
                
                if (!response.IsSuccessStatusCode)
                {
                    return false;
                }

                string content = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                // If there's a "data" array with entries, the app is subscribed
                if (root.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Array)
                {
                    return dataProp.GetArrayLength() > 0;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public async Task<string?> FetchWebhookUrlFromMetaAsync(string appId, string appSecret, string? wabaId = null, string? accessToken = null)
        {
            if (string.IsNullOrWhiteSpace(appId) || IsMock(appId) || IsMock(appSecret))
            {
                return null;
            }

            try
            {
                var client = CreateClient();
                // 1. Fetch App Webhook Subscriptions from Meta Graph API
                string appAccessToken = $"{appId}|{appSecret}";
                string url = $"{MetaGraphBaseUrl}/{appId}/subscriptions?access_token={appAccessToken}";
                
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("data", out var dataArr) && dataArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in dataArr.EnumerateArray())
                        {
                            if (item.TryGetProperty("callback_url", out var cbProp) && !string.IsNullOrWhiteSpace(cbProp.GetString()))
                            {
                                string callbackUrl = cbProp.GetString()!;
                                _logger.LogInformation("Fetched dynamic Webhook URL from Meta API for App {AppId}: {Url}", appId, callbackUrl);
                                return callbackUrl;
                            }
                        }
                    }
                }

                // 2. Query WABA webhook_configuration if wabaId and accessToken provided
                if (!string.IsNullOrEmpty(wabaId) && !string.IsNullOrEmpty(accessToken) && !IsMock(wabaId) && !IsMock(accessToken))
                {
                    string wabaUrl = $"{MetaGraphBaseUrl}/{wabaId}?fields=id,name,subscribed_apps,webhook_configuration&access_token={accessToken}";
                    var wabaResp = await client.GetAsync(wabaUrl);
                    if (wabaResp.IsSuccessStatusCode)
                    {
                        string wabaJson = await wabaResp.Content.ReadAsStringAsync();
                        using var wabaDoc = JsonDocument.Parse(wabaJson);
                        if (wabaDoc.RootElement.TryGetProperty("webhook_configuration", out var whConfig) && whConfig.TryGetProperty("callback_url", out var cbProp) && !string.IsNullOrWhiteSpace(cbProp.GetString()))
                        {
                            return cbProp.GetString();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not fetch dynamic Webhook URL from Meta API for App {AppId}", appId);
            }

            return null;
        }
    }
}
