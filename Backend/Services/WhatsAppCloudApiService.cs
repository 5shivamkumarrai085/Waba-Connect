using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Webhook;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services;

/// <summary>
/// Implementation of WhatsApp Business Cloud API integration.
/// Handles sending messages, syncing templates, and processing webhook callbacks.
/// </summary>
public class WhatsAppCloudApiService : IWhatsAppService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<WhatsAppCloudApiService> _logger;

    private string ApiVersion => _configuration["WhatsApp:ApiVersion"] ?? "v21.0";
    private string BaseUrl => $"https://graph.facebook.com/{ApiVersion}";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public WhatsAppCloudApiService(
        HttpClient httpClient,
        IConfiguration configuration,
        AppDbContext dbContext,
        ILogger<WhatsAppCloudApiService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _dbContext = dbContext;
        _logger = logger;
    }

    private async Task<(string AccessToken, string PhoneNumberId, string BusinessAccountId)> GetActiveConfigAsync()
    {
        var config = await _dbContext.WabaConfigurations.FirstOrDefaultAsync(c => c.Connected);
        if (config == null) throw new InvalidOperationException("WABA is not configured or connected.");

        var phone = await _dbContext.WabaPhoneNumbers.FirstOrDefaultAsync();
        var phoneId = phone?.PhoneNumberId ?? throw new InvalidOperationException("No WABA phone number found.");

        var biz = await _dbContext.Businesses.FirstOrDefaultAsync();
        var bizId = biz?.BusinessId ?? throw new InvalidOperationException("No WABA business details found.");

        return (config.AccessToken, phoneId, bizId);
    }

    /// <inheritdoc />
    public async Task<string?> SendTemplateMessageAsync(
        string recipientPhone,
        string templateName,
        string languageCode,
        Dictionary<string, string>? variables = null)
    {
        try
        {
            var whatsAppPhone = PhoneNumberHelper.FormatForWhatsApp(recipientPhone);

            var messagePayload = new
            {
                messaging_product = "whatsapp",
                to = whatsAppPhone,
                type = "template",
                template = new
                {
                    name = templateName,
                    language = new { code = languageCode },
                    components = BuildTemplateComponents(variables)
                }
            };

            var json = JsonSerializer.Serialize(messagePayload, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogInformation(
                "Sending WhatsApp template message to {Phone} using template '{Template}'",
                whatsAppPhone, templateName);

            var (accessToken, phoneNumberId, _) = await GetActiveConfigAsync();

            var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/{phoneNumberId}/messages")
            {
                Content = content
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request);

            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Failed to send WhatsApp message. Status: {Status}, Response: {Response}",
                    response.StatusCode, responseBody);
                return null;
            }

            // Parse message ID from response: { "messages": [{ "id": "wamid.xxx" }] }
            using var doc = JsonDocument.Parse(responseBody);
            var messageId = doc.RootElement
                .GetProperty("messages")[0]
                .GetProperty("id")
                .GetString();

            _logger.LogInformation(
                "WhatsApp message sent successfully. MessageId: {MessageId}", messageId);

            return messageId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending WhatsApp template message to {Phone}", recipientPhone);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<List<WhatsAppTemplateInfo>> GetTemplatesAsync()
    {
        try
        {
            var (accessToken, _, businessAccountId) = await GetActiveConfigAsync();

            var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/{businessAccountId}/message_templates?limit=100");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request);

            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Failed to fetch templates from WhatsApp. Status: {Status}, Response: {Response}",
                    response.StatusCode, responseBody);
                return [];
            }

            using var doc = JsonDocument.Parse(responseBody);
            var templates = new List<WhatsAppTemplateInfo>();

            if (doc.RootElement.TryGetProperty("data", out var dataArray))
            {
                foreach (var item in dataArray.EnumerateArray())
                {
                    var templateInfo = new WhatsAppTemplateInfo
                    {
                        Id = item.GetProperty("id").GetString() ?? string.Empty,
                        Name = item.GetProperty("name").GetString() ?? string.Empty,
                        Language = item.GetProperty("language").GetString() ?? "en",
                        Category = item.GetProperty("category").GetString() ?? string.Empty,
                        Status = item.GetProperty("status").GetString() ?? string.Empty
                    };

                    // Extract body text from components
                    if (item.TryGetProperty("components", out var components))
                    {
                        foreach (var component in components.EnumerateArray())
                        {
                            if (component.GetProperty("type").GetString() == "BODY")
                            {
                                templateInfo.BodyText = component.GetProperty("text").GetString();
                                break;
                            }
                        }
                    }

                    templates.Add(templateInfo);
                }
            }

            _logger.LogInformation("Fetched {Count} templates from WhatsApp API", templates.Count);
            return templates;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching templates from WhatsApp API");
            return [];
        }
    }

    public bool VerifyWebhook(string mode, string token, string challenge)
    {
        // VerifyToken needs to be read synchronously or passed in differently.
        // The VerifyWebhook is called from a synchronous context or needs to read from DB synchronously which is bad.
        // Let's read from DB synchronously as a fallback or just use FirstOrDefault.
        var config = _dbContext.WabaConfigurations.FirstOrDefault();
        if (config == null || string.IsNullOrEmpty(config.VerifyToken))
        {
            _logger.LogWarning("Webhook verification failed. No WABA configuration found.");
            return false;
        }

        if (mode == "subscribe" && token == config.VerifyToken)
        {
            _logger.LogInformation("Webhook verified successfully");
            return true;
        }

        _logger.LogWarning("Webhook verification failed. Mode: {Mode}, Token mismatch", mode);
        return false;
    }

    /// <inheritdoc />
    public async Task ProcessWebhookAsync(WhatsAppWebhookPayload payload)
    {
        if (payload.Entry == null) return;

        foreach (var entry in payload.Entry)
        {
            if (entry.Changes == null) continue;

            foreach (var change in entry.Changes)
            {
                if (change.Value?.Statuses == null) continue;

                foreach (var statusUpdate in change.Value.Statuses)
                {
                    await ProcessStatusUpdateAsync(statusUpdate);
                }
            }
        }
    }

    /// <summary>
    /// Processes a single message status update from the webhook.
    /// Updates the CampaignContact record and recalculates campaign aggregate counts.
    /// </summary>
    private async Task ProcessStatusUpdateAsync(StatusUpdate statusUpdate)
    {
        try
        {
            var campaignContact = await _dbContext.CampaignContacts
                .Include(cc => cc.Campaign)
                .FirstOrDefaultAsync(cc => cc.WhatsAppMessageId == statusUpdate.Id);

            if (campaignContact == null)
            {
                _logger.LogWarning(
                    "Received status update for unknown message ID: {MessageId}", statusUpdate.Id);
                return;
            }

            var previousStatus = campaignContact.Status;
            var now = DateTime.UtcNow;

            switch (statusUpdate.Status?.ToLowerInvariant())
            {
                case "sent":
                    if (campaignContact.Status == MessageStatus.Pending)
                    {
                        campaignContact.Status = MessageStatus.Sent;
                        campaignContact.SentAt ??= now;
                    }
                    break;

                case "delivered":
                    if (campaignContact.Status is MessageStatus.Pending or MessageStatus.Sent)
                    {
                        campaignContact.Status = MessageStatus.Delivered;
                        campaignContact.SentAt ??= now;
                        campaignContact.DeliveredAt ??= now;
                    }
                    break;

                case "read":
                    if (campaignContact.Status is MessageStatus.Pending or MessageStatus.Sent or MessageStatus.Delivered)
                    {
                        campaignContact.Status = MessageStatus.Read;
                        campaignContact.SentAt ??= now;
                        campaignContact.DeliveredAt ??= now;
                        campaignContact.ReadAt ??= now;
                    }
                    break;

                case "failed":
                    campaignContact.Status = MessageStatus.Failed;
                    campaignContact.ErrorMessage = statusUpdate.Errors?.FirstOrDefault()?.Title
                        ?? "Message delivery failed";
                    break;

                default:
                    _logger.LogWarning(
                        "Unknown status '{Status}' for message {MessageId}",
                        statusUpdate.Status, statusUpdate.Id);
                    return;
            }

            // Recalculate campaign aggregate counts
            await RecalculateCampaignCountsAsync(campaignContact.CampaignId);

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Updated message {MessageId} status: {OldStatus} → {NewStatus}",
                statusUpdate.Id, previousStatus, campaignContact.Status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error processing status update for message {MessageId}", statusUpdate.Id);
        }
    }

    /// <summary>
    /// Recalculates the aggregate delivery counts on a campaign.
    /// </summary>
    private async Task RecalculateCampaignCountsAsync(int campaignId)
    {
        var campaign = await _dbContext.Campaigns.FindAsync(campaignId);
        if (campaign == null) return;

        var contacts = await _dbContext.CampaignContacts
            .Where(cc => cc.CampaignId == campaignId)
            .ToListAsync();

        campaign.TotalRecipients = contacts.Count;
        campaign.DeliveredCount = contacts.Count(c =>
            c.Status is MessageStatus.Delivered or MessageStatus.Read);
        campaign.ReadCount = contacts.Count(c => c.Status == MessageStatus.Read);
        campaign.FailedCount = contacts.Count(c => c.Status == MessageStatus.Failed);
    }

    /// <summary>
    /// Builds the template components array for the WhatsApp API request.
    /// </summary>
    private static object[]? BuildTemplateComponents(Dictionary<string, string>? variables)
    {
        if (variables == null || variables.Count == 0)
            return null;

        var parameters = variables
            .OrderBy(v => v.Key)
            .Select(v => new
            {
                type = "text",
                text = v.Value
            })
            .ToArray();

        return
        [
            new
            {
                type = "body",
                parameters = (object)parameters
            }
        ];
    }
}
