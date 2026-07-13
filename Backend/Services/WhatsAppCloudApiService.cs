using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.Webhook;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
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

    private async Task<(string AccessToken, string PhoneNumberId, string BusinessAccountId)> GetActiveConfigAsync(string? requestedPhoneNumberId = null)
    {
        var config = await _dbContext.WabaConfigurations.FirstOrDefaultAsync(c => c.Connected);
        if (config == null) throw new InvalidOperationException("WABA is not configured or connected.");

        var phone = !string.IsNullOrWhiteSpace(requestedPhoneNumberId)
            ? await _dbContext.WabaPhoneNumbers.FirstOrDefaultAsync(p => p.PhoneNumberId == requestedPhoneNumberId)
            : await _dbContext.WabaPhoneNumbers.FirstOrDefaultAsync();
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
        var result = await SendTemplateMessageWithResultAsync(recipientPhone, templateName, languageCode, variables);
        return result.Success ? result.MessageId : null;
    }

    /// <inheritdoc />
    public async Task<WhatsAppSendResult> SendTemplateMessageWithResultAsync(
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

                var errorMessage = ExtractMetaErrorMessage(responseBody)
                    ?? $"WhatsApp API rejected the template message with status {response.StatusCode}.";

                return WhatsAppSendResult.Failed(errorMessage);
            }

            using var doc = JsonDocument.Parse(responseBody);
            var messageId = doc.RootElement
                .GetProperty("messages")[0]
                .GetProperty("id")
                .GetString();

            _logger.LogInformation(
                "WhatsApp message sent successfully. MessageId: {MessageId}", messageId);

            return WhatsAppSendResult.Sent(messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending WhatsApp template message to {Phone}", recipientPhone);
            return WhatsAppSendResult.Failed(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<WhatsAppSendResult> SendTextMessageAsync(string recipientPhone, string text, string? fromPhoneNumberId = null)
    {
        try
        {
            var whatsAppPhone = PhoneNumberHelper.FormatForWhatsApp(recipientPhone);

            var messagePayload = new
            {
                messaging_product = "whatsapp",
                to = whatsAppPhone,
                type = "text",
                text = new
                {
                    preview_url = false,
                    body = text
                }
            };

            var json = JsonSerializer.Serialize(messagePayload, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var (accessToken, phoneNumberId, _) = await GetActiveConfigAsync(fromPhoneNumberId);

            var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/{phoneNumberId}/messages")
            {
                Content = content
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = ExtractMetaErrorMessage(responseBody)
                    ?? $"WhatsApp API rejected the message with status {response.StatusCode}.";

                _logger.LogError(
                    "Failed to send WhatsApp text message. Status: {Status}, Response: {Response}",
                    response.StatusCode, responseBody);

                return WhatsAppSendResult.Failed(errorMessage);
            }

            using var doc = JsonDocument.Parse(responseBody);
            var messageId = doc.RootElement
                .GetProperty("messages")[0]
                .GetProperty("id")
                .GetString();

            _logger.LogInformation("WhatsApp text message sent successfully. MessageId: {MessageId}", messageId);
            return WhatsAppSendResult.Sent(messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending WhatsApp text message to {Phone}", recipientPhone);
            return WhatsAppSendResult.Failed(ex.Message);
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
                if (change.Value?.Statuses != null)
                {
                    foreach (var statusUpdate in change.Value.Statuses)
                    {
                        await ProcessStatusUpdateAsync(statusUpdate);
                    }
                }

                if (change.Value?.Messages != null)
                {
                    foreach (var incomingMessage in change.Value.Messages)
                    {
                        var contactName = change.Value.Contacts?
                            .FirstOrDefault(c => c.WaId == incomingMessage.From)
                            ?.Profile?.Name;

                        await ProcessIncomingMessageAsync(
                            incomingMessage,
                            contactName,
                            change.Value.Metadata?.PhoneNumberId);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Processes a single inbound WhatsApp customer message and stores it in chat history.
    /// </summary>
    private async Task ProcessIncomingMessageAsync(IncomingMessage incomingMessage, string? contactName, string? fromPhoneNumberId)
    {
        if (string.IsNullOrWhiteSpace(incomingMessage.Id)) return;

        var exists = await _dbContext.ChatMessages.AnyAsync(m => m.WhatsAppMessageId == incomingMessage.Id);
        if (exists) return;

        var normalizedPhone = PhoneNumberHelper.NormalizePhoneNumber(incomingMessage.From);
        if (string.IsNullOrWhiteSpace(normalizedPhone)) return;

        var contact = await _dbContext.Contacts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Phone == normalizedPhone);

        if (contact == null)
        {
            contact = new Contact
            {
                Name = !string.IsNullOrWhiteSpace(contactName) ? contactName : normalizedPhone,
                Phone = normalizedPhone,
                Type = ContactType.Lead,
                Status = ContactStatus.New,
                Source = ContactSource.WhatsApp,
                IsActive = true
            };
            _dbContext.Contacts.Add(contact);
            await _dbContext.SaveChangesAsync();
        }
        else if (!contact.IsActive)
        {
            contact.IsActive = true;
            await _dbContext.SaveChangesAsync();
        }

        var account = !string.IsNullOrWhiteSpace(fromPhoneNumberId)
            ? await _dbContext.WabaPhoneNumbers.FirstOrDefaultAsync(p => p.PhoneNumberId == fromPhoneNumberId)
            : await _dbContext.WabaPhoneNumbers.FirstOrDefaultAsync();

        var conversation = await _dbContext.ChatConversations
            .FirstOrDefaultAsync(c => c.ContactId == contact.Id);

        if (conversation == null)
        {
            conversation = new ChatConversation
            {
                ContactId = contact.Id,
                WabaPhoneNumberId = account?.Id
            };
            _dbContext.ChatConversations.Add(conversation);
        }

        var text = incomingMessage.Text?.Body;
        if (string.IsNullOrWhiteSpace(text))
        {
            text = $"[{incomingMessage.Type} message]";
        }

        conversation.LastMessageText = text;
        conversation.LastMessageAt = DateTime.UtcNow;
        conversation.UnreadCount += 1;

        _dbContext.ChatMessages.Add(new ChatMessage
        {
            Conversation = conversation,
            ContactId = contact.Id,
            WhatsAppMessageId = incomingMessage.Id,
            Direction = ChatMessageDirection.Incoming,
            Status = ChatMessageStatus.Received,
            Text = text
        });

        await _dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Processes a single message status update from the webhook.
    /// Updates the campaign recipient and matching chat message.
    /// </summary>
    private async Task ProcessStatusUpdateAsync(StatusUpdate statusUpdate)
    {
        try
        {
            var campaignContact = await _dbContext.CampaignContacts
                .Include(cc => cc.Campaign)
                .FirstOrDefaultAsync(cc => cc.WhatsAppMessageId == statusUpdate.Id);

            var chatMessage = await _dbContext.ChatMessages
                .FirstOrDefaultAsync(cm => cm.WhatsAppMessageId == statusUpdate.Id);

            if (campaignContact == null && chatMessage == null)
            {
                _logger.LogWarning(
                    "Received status update for unknown message ID: {MessageId}", statusUpdate.Id);
                return;
            }

            var previousCampaignStatus = campaignContact?.Status;
            var previousChatStatus = chatMessage?.Status;
            var now = DateTime.UtcNow;

            switch (statusUpdate.Status?.ToLowerInvariant())
            {
                case "sent":
                    if (campaignContact?.Status == MessageStatus.Pending)
                    {
                        campaignContact.Status = MessageStatus.Sent;
                        campaignContact.SentAt ??= now;
                    }
                    if (chatMessage?.Status == ChatMessageStatus.Pending)
                    {
                        chatMessage.Status = ChatMessageStatus.Sent;
                        chatMessage.SentAt ??= now;
                    }
                    break;

                case "delivered":
                    if (campaignContact?.Status is MessageStatus.Pending or MessageStatus.Sent)
                    {
                        campaignContact.Status = MessageStatus.Delivered;
                        campaignContact.SentAt ??= now;
                        campaignContact.DeliveredAt ??= now;
                    }
                    if (chatMessage?.Status is ChatMessageStatus.Pending or ChatMessageStatus.Sent)
                    {
                        chatMessage.Status = ChatMessageStatus.Delivered;
                        chatMessage.SentAt ??= now;
                        chatMessage.DeliveredAt ??= now;
                    }
                    break;

                case "read":
                    if (campaignContact?.Status is MessageStatus.Pending or MessageStatus.Sent or MessageStatus.Delivered)
                    {
                        campaignContact.Status = MessageStatus.Read;
                        campaignContact.SentAt ??= now;
                        campaignContact.DeliveredAt ??= now;
                        campaignContact.ReadAt ??= now;
                    }
                    if (chatMessage?.Status is ChatMessageStatus.Pending or ChatMessageStatus.Sent or ChatMessageStatus.Delivered)
                    {
                        chatMessage.Status = ChatMessageStatus.Read;
                        chatMessage.SentAt ??= now;
                        chatMessage.DeliveredAt ??= now;
                        chatMessage.ReadAt ??= now;
                    }
                    break;

                case "failed":
                    var error = statusUpdate.Errors?.FirstOrDefault();
                    var errorMessage = error?.ErrorData?.Details
                        ?? error?.Message
                        ?? error?.Title
                        ?? "Message delivery failed";
                    if (campaignContact != null)
                    {
                        campaignContact.Status = MessageStatus.Failed;
                        campaignContact.ErrorMessage = errorMessage;
                    }
                    if (chatMessage != null)
                    {
                        chatMessage.Status = ChatMessageStatus.Failed;
                        chatMessage.ErrorMessage = errorMessage;
                    }
                    break;

                default:
                    _logger.LogWarning(
                        "Unknown status '{Status}' for message {MessageId}",
                        statusUpdate.Status, statusUpdate.Id);
                    return;
            }

            if (campaignContact != null)
            {
                await RecalculateCampaignCountsAsync(campaignContact.CampaignId);
            }

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Updated message {MessageId} status. Campaign: {OldCampaignStatus} -> {NewCampaignStatus}, Chat: {OldChatStatus} -> {NewChatStatus}",
                statusUpdate.Id,
                previousCampaignStatus,
                campaignContact?.Status,
                previousChatStatus,
                chatMessage?.Status);
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

    private static string? ExtractMetaErrorMessage(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody)) return null;

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                if (error.TryGetProperty("error_user_msg", out var userMessage))
                    return userMessage.GetString();

                if (error.TryGetProperty("error_data", out var errorData)
                    && errorData.TryGetProperty("details", out var details))
                    return details.GetString();

                if (error.TryGetProperty("message", out var message))
                    return message.GetString();
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static object[]? BuildTemplateComponents(Dictionary<string, string>? variables)
    {
        if (variables == null || variables.Count == 0)
            return null;

        var componentsList = new System.Collections.Generic.List<object>();

        // Check for media attachment variable
        var fileVar = variables.FirstOrDefault(v => v.Key.Equals("file", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(fileVar.Value))
        {
            var fileUrl = fileVar.Value;
            var fileName = System.IO.Path.GetFileName(fileUrl) ?? "document.pdf";

            componentsList.Add(new
            {
                type = "header",
                parameters = new object[]
                {
                    new
                    {
                        type = "document",
                        document = new
                        {
                            link = fileUrl,
                            filename = fileName
                        }
                    }
                }
            });
        }

        // Add regular text variables to the body parameters
        var bodyParams = variables
            .Where(v => !v.Key.Equals("file", StringComparison.OrdinalIgnoreCase))
            .OrderBy(v => v.Key)
            .Select(v => new
            {
                type = "text",
                text = v.Value
            })
            .ToArray();

        if (bodyParams.Length > 0)
        {
            componentsList.Add(new
            {
                type = "body",
                parameters = (object)bodyParams
            });
        }

        return componentsList.Count > 0 ? componentsList.ToArray() : null;
    }
}
