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
    private readonly IServiceScopeFactory _scopeFactory;

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
        ILogger<WhatsAppCloudApiService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _dbContext = dbContext;
        _logger = logger;
        _scopeFactory = scopeFactory;
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
            var template = await _dbContext.Templates.FirstOrDefaultAsync(t => t.Name == templateName);
            var headerType = template?.HeaderType ?? HeaderType.None;

            var (accessToken, phoneNumberId, _) = await GetActiveConfigAsync();

            string? mediaId = null;
            if (headerType != HeaderType.None && variables != null)
            {
                var fileVar = variables.FirstOrDefault(v => v.Key.Equals("file", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(fileVar.Value) && (fileVar.Value.Contains("localhost") || fileVar.Value.Contains("127.0.0.1") || !fileVar.Value.StartsWith("http", StringComparison.OrdinalIgnoreCase)))
                {
                    var mediaTypeStr = headerType switch
                    {
                        HeaderType.Image => "image",
                        HeaderType.Video => "video",
                        HeaderType.Document => "document",
                        _ => "document"
                    };
                    mediaId = await UploadMediaToWhatsAppAsync(fileVar.Value, mediaTypeStr, Path.GetFileName(fileVar.Value) ?? "file", accessToken, phoneNumberId);
                }
            }

            var messagePayload = new
            {
                messaging_product = "whatsapp",
                to = whatsAppPhone,
                type = "template",
                template = new
                {
                    name = templateName,
                    language = new { code = languageCode },
                    components = BuildTemplateComponents(variables, headerType, mediaId)
                }
            };

            var json = JsonSerializer.Serialize(messagePayload, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogInformation(
                "Sending WhatsApp template message to {Phone} using template '{Template}'",
                whatsAppPhone, templateName);

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

        if (incomingMessage.Type == "interactive" && incomingMessage.Interactive != null)
        {
            var interactive = incomingMessage.Interactive;
            if (interactive.Type == "button_reply" && interactive.ButtonReply != null)
            {
                text = !string.IsNullOrEmpty(interactive.ButtonReply.Title) 
                    ? interactive.ButtonReply.Title 
                    : interactive.ButtonReply.Id;
            }
            else if (interactive.Type == "list_reply" && interactive.ListReply != null)
            {
                text = !string.IsNullOrEmpty(interactive.ListReply.Title) 
                    ? interactive.ListReply.Title 
                    : interactive.ListReply.Id;
            }
        }
        else if (incomingMessage.Type == "button" && incomingMessage.Button != null)
        {
            text = incomingMessage.Button.Text;
        }

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

        // Start flow execution asynchronously
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                
                // Fetch contact using the new scoped dbContext to avoid cross-context tracking and disposed context issues
                var dbContact = await dbContext.Contacts.FindAsync(contact.Id);
                if (dbContact == null) return;

                var routerService = scope.ServiceProvider.GetRequiredService<IBotRouterService>();
                await routerService.RouteMessageAsync(normalizedPhone, text, dbContact);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error routing incoming message in background task for phone {Phone}", normalizedPhone);
            }
        });
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

    private static object[]? BuildTemplateComponents(Dictionary<string, string>? variables, HeaderType headerType, string? mediaId = null)
    {
        if (variables == null || variables.Count == 0)
            return null;

        var componentsList = new System.Collections.Generic.List<object>();

        // Check for media attachment variable and valid header type
        var fileVar = variables.FirstOrDefault(v => v.Key.Equals("file", StringComparison.OrdinalIgnoreCase));
        if (headerType != HeaderType.None && !string.IsNullOrEmpty(fileVar.Value))
        {
            var fileUrl = fileVar.Value;
            var fileName = System.IO.Path.GetFileName(fileUrl) ?? "document.pdf";
            object? headerParam = null;

            if (headerType == HeaderType.Document)
            {
                headerParam = !string.IsNullOrEmpty(mediaId) ? new
                {
                    type = "document",
                    document = new
                    {
                        id = mediaId,
                        filename = fileName
                    }
                } : new
                {
                    type = "document",
                    document = new
                    {
                        link = fileUrl,
                        filename = fileName
                    }
                };
            }
            else if (headerType == HeaderType.Image)
            {
                headerParam = !string.IsNullOrEmpty(mediaId) ? new
                {
                    type = "image",
                    image = new
                    {
                        id = mediaId
                    }
                } : new
                {
                    type = "image",
                    image = new
                    {
                        link = fileUrl
                    }
                };
            }
            else if (headerType == HeaderType.Video)
            {
                headerParam = !string.IsNullOrEmpty(mediaId) ? new
                {
                    type = "video",
                    video = new
                    {
                        id = mediaId
                    }
                } : new
                {
                    type = "video",
                    video = new
                    {
                        link = fileUrl
                    }
                };
            }

            if (headerParam != null)
            {
                componentsList.Add(new
                {
                    type = "header",
                    parameters = new object[] { headerParam }
                });
            }
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

    /// <inheritdoc />
    public async Task<WhatsAppSendResult> SendMediaMessageAsync(
        string recipientPhone,
        string mediaUrl,
        string mediaType,
        string? filename = null,
        string? caption = null,
        string? fromPhoneNumberId = null)
    {
        try
        {
            var whatsAppPhone = PhoneNumberHelper.FormatForWhatsApp(recipientPhone);
            var (accessToken, activePhoneNumberId, _) = await GetActiveConfigAsync(fromPhoneNumberId);
            var finalPhoneNumberId = fromPhoneNumberId ?? activePhoneNumberId;

            // 1. Try to upload local/localhost media to WhatsApp
            string? mediaId = null;
            if (!string.IsNullOrWhiteSpace(mediaUrl) && (mediaUrl.Contains("localhost") || mediaUrl.Contains("127.0.0.1") || !mediaUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)))
            {
                mediaId = await UploadMediaToWhatsAppAsync(mediaUrl, mediaType, filename ?? "file", accessToken, finalPhoneNumberId);
            }

            // Construct payload based on media type using mediaId if available, otherwise link
            object mediaObject;
            if (!string.IsNullOrEmpty(mediaId))
            {
                mediaObject = mediaType.ToLower() switch
                {
                    "image" => new { id = mediaId, caption = caption },
                    "video" => new { id = mediaId, caption = caption },
                    "document" => new { id = mediaId, filename = filename ?? Path.GetFileName(mediaUrl) },
                    _ => throw new ArgumentException($"Unsupported media type: {mediaType}")
                };
            }
            else
            {
                mediaObject = mediaType.ToLower() switch
                {
                    "image" => new { link = mediaUrl, caption = caption },
                    "video" => new { link = mediaUrl, caption = caption },
                    "document" => new { link = mediaUrl, filename = filename ?? Path.GetFileName(mediaUrl) },
                    _ => throw new ArgumentException($"Unsupported media type: {mediaType}")
                };
            }

            var messagePayload = new
            {
                messaging_product = "whatsapp",
                to = whatsAppPhone,
                type = mediaType.ToLower(),
                image = mediaType.ToLower() == "image" ? mediaObject : null,
                video = mediaType.ToLower() == "video" ? mediaObject : null,
                document = mediaType.ToLower() == "document" ? mediaObject : null
            };

            // Remove null properties from payload serialization
            var json = JsonSerializer.Serialize(messagePayload, new JsonSerializerOptions 
            { 
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogInformation(
                "Sending WhatsApp {MediaType} message to {Phone}",
                mediaType, whatsAppPhone);

            var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/{finalPhoneNumberId}/messages")
            {
                Content = content
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Failed to send WhatsApp media message. Status: {Status}, Response: {Response}",
                    response.StatusCode, responseBody);

                var errorMessage = ExtractMetaErrorMessage(responseBody)
                    ?? $"WhatsApp API rejected the {mediaType} message with status {response.StatusCode}.";

                return WhatsAppSendResult.Failed(errorMessage);
            }

            using var doc = JsonDocument.Parse(responseBody);
            var messageId = doc.RootElement
                .GetProperty("messages")[0]
                .GetProperty("id")
                .GetString();

            _logger.LogInformation(
                "WhatsApp media message sent successfully. MessageId: {MessageId}", messageId);

            return WhatsAppSendResult.Sent(messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending WhatsApp {MediaType} message to {Phone}", mediaType, recipientPhone);
            return WhatsAppSendResult.Failed(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<WhatsAppSendResult> SendCustomPayloadAsync(string recipientPhone, object payload, string? fromPhoneNumberId = null)
    {
        try
        {
            var whatsAppPhone = PhoneNumberHelper.FormatForWhatsApp(recipientPhone);
            var (accessToken, activePhoneNumberId, _) = await GetActiveConfigAsync(fromPhoneNumberId);
            var finalPhoneNumberId = fromPhoneNumberId ?? activePhoneNumberId;

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions 
            { 
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogInformation("Sending custom WhatsApp payload to {Phone}", whatsAppPhone);

            var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/{finalPhoneNumberId}/messages")
            {
                Content = content
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to send WhatsApp custom message. Status: {Status}, Response: {Response}", response.StatusCode, responseBody);
                var errorMessage = ExtractMetaErrorMessage(responseBody)
                    ?? $"WhatsApp API rejected custom message with status {response.StatusCode}.";
                return WhatsAppSendResult.Failed(errorMessage);
            }

            using var doc = JsonDocument.Parse(responseBody);
            var messageId = doc.RootElement
                .GetProperty("messages")[0]
                .GetProperty("id")
                .GetString();

            _logger.LogInformation("WhatsApp custom message sent successfully. MessageId: {MessageId}", messageId);
            return WhatsAppSendResult.Sent(messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending custom WhatsApp payload to {Phone}", recipientPhone);
            return WhatsAppSendResult.Failed(ex.Message);
        }
    }

    private async Task<string?> UploadMediaToWhatsAppAsync(string mediaUrl, string mediaType, string filename, string accessToken, string phoneNumberId)
    {
        try
        {
            // 1. Download file from our local server or local path
            byte[] fileBytes;
            if (mediaUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && mediaUrl.Contains(";base64,"))
            {
                var prefix = mediaUrl.Substring(0, mediaUrl.IndexOf(";base64,"));
                var parsedMime = prefix.Substring(5); // e.g. "image/jpeg"
                var base64Ext = parsedMime.Split('/').LastOrDefault();
                if (base64Ext == "jpeg") base64Ext = "jpg";
                
                if (string.IsNullOrEmpty(Path.GetExtension(filename)))
                {
                    filename = $"{filename}.{base64Ext ?? "jpg"}";
                }

                var base64Part = mediaUrl.Substring(mediaUrl.IndexOf(";base64,") + 8);
                fileBytes = Convert.FromBase64String(base64Part);
            }
            else if (mediaUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                using var tempClient = new HttpClient();
                fileBytes = await tempClient.GetByteArrayAsync(mediaUrl);
            }
            else
            {
                var relativePath = mediaUrl.Replace("/", Path.DirectorySeparatorChar.ToString());
                if (relativePath.StartsWith(Path.DirectorySeparatorChar))
                {
                    relativePath = relativePath.Substring(1);
                }
                var absolutePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", relativePath);
                if (!File.Exists(absolutePath))
                {
                    _logger.LogError("File does not exist: {Path}", absolutePath);
                    return null;
                }
                fileBytes = await File.ReadAllBytesAsync(absolutePath);
            }

            // 2. Prepare MultipartFormDataContent
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent("whatsapp"), "messaging_product");
            
            var mimeType = mediaType.ToLower() switch
            {
                "image" => "image/jpeg",
                "video" => "video/mp4",
                "document" => "application/pdf",
                _ => "application/octet-stream"
            };

            var ext = Path.GetExtension(filename).ToLower();
            if (ext == ".jpg" || ext == ".jpeg") mimeType = "image/jpeg";
            else if (ext == ".png") mimeType = "image/png";
            else if (ext == ".gif") mimeType = "image/gif";
            else if (ext == ".mp4") mimeType = "video/mp4";
            else if (ext == ".pdf") mimeType = "application/pdf";
            else if (ext == ".txt") mimeType = "text/plain";
            else if (ext == ".doc") mimeType = "application/msword";
            else if (ext == ".docx") mimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
            else if (ext == ".xls") mimeType = "application/vnd.ms-excel";
            else if (ext == ".xlsx") mimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);
            form.Add(fileContent, "file", filename);

            // 3. Make HTTP request to Meta
            var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/{phoneNumberId}/media");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = form;

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Meta media upload failed: {Error}", errorContent);
                return null;
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            if (doc.RootElement.TryGetProperty("id", out var idProp))
            {
                return idProp.GetString();
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading media to WhatsApp");
            return null;
        }
    }

    public async Task<bool> TryTriggerTemplateBotAsync(string normalizedPhone, string incomingText, Contact contact)
    {
        try
        {
            var activeTemplateBots = await _dbContext.TemplateBots
                .Where(b => b.IsActive)
                .Include(b => b.Template)
                .Include(b => b.Variables)
                .ToListAsync();

            var relationTypeStr = contact.Type == ContactType.Customer ? "Customer" : "Lead";

            TemplateBot? matchedBot = null;

            // Try to match based on exact keywords first
            foreach (var bot in activeTemplateBots)
            {
                if (!string.IsNullOrEmpty(bot.RelationType) && 
                    !string.Equals(bot.RelationType, relationTypeStr, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var botKeywords = (bot.TriggerKeyword ?? "")
                    .Split(',')
                    .Select(k => k.Trim().ToLower())
                    .Where(k => !string.IsNullOrEmpty(k))
                    .ToList();

                bool isMatch = false;

                if (bot.ReplyType == "On Exact Match")
                {
                    isMatch = botKeywords.Any(k => string.Equals(incomingText.Trim(), k, StringComparison.OrdinalIgnoreCase));
                }
                else if (bot.ReplyType == "When Message Contains")
                {
                    isMatch = botKeywords.Any(k => incomingText.ToLower().Contains(k));
                }

                if (isMatch)
                {
                    matchedBot = bot;
                    break;
                }
            }

            // If no match by keyword, check for First Message
            if (matchedBot == null)
            {
                var messageCount = await _dbContext.ChatMessages
                    .CountAsync(m => m.ContactId == contact.Id);
                
                bool isFirstMessage = messageCount <= 1; // Including the incoming message we just saved

                foreach (var bot in activeTemplateBots)
                {
                    if (!string.IsNullOrEmpty(bot.RelationType) && 
                        !string.Equals(bot.RelationType, relationTypeStr, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (bot.ReplyType == "When Lead or Client Sends First Message" && isFirstMessage)
                    {
                        matchedBot = bot;
                        break;
                    }
                }
            }

            // Fallback to Default Reply
            if (matchedBot == null)
            {
                matchedBot = activeTemplateBots.FirstOrDefault(b => 
                    b.ReplyType == "Default Reply" && 
                    (string.IsNullOrEmpty(b.RelationType) || string.Equals(b.RelationType, relationTypeStr, StringComparison.OrdinalIgnoreCase)));
            }

            if (matchedBot == null)
            {
                return false;
            }

            // Find all variable names used in template body text
            var templateBody = matchedBot.Template.BodyText ?? "";
            var varMatches = System.Text.RegularExpressions.Regex.Matches(templateBody, @"\{\{(\d+)\}\}");
            var allowedVarNames = varMatches.Cast<System.Text.RegularExpressions.Match>()
                .Select(m => m.Groups[1].Value)
                .ToHashSet();

            // Resolve Variables
            var resolvedVariables = new Dictionary<string, string>();
            foreach (var v in matchedBot.Variables)
            {
                if (!allowedVarNames.Contains(v.VariableName))
                {
                    continue; // Skip variables that are not used in the body text of the template
                }

                string val = "";
                if (!string.IsNullOrEmpty(v.VariableValue))
                {
                    val = v.VariableValue; // Static value
                }
                else if (!string.IsNullOrEmpty(v.MergeField))
                {
                    val = v.MergeField.ToLower() switch
                    {
                        "name" => contact.Name,
                        "phone" => contact.Phone,
                        "email" => "",
                        _ => ""
                    };
                }

                if (string.IsNullOrEmpty(val))
                {
                    val = " ";
                }

                resolvedVariables[v.VariableName] = val;
            }

            // Ensure all required parameters are present with at least a space placeholder
            foreach (var varName in allowedVarNames)
            {
                if (!resolvedVariables.ContainsKey(varName))
                {
                    resolvedVariables[varName] = " ";
                }
            }

            // Send Template Message
            var sendResult = await SendTemplateMessageWithResultAsync(
                normalizedPhone, 
                matchedBot.Template.Name, 
                matchedBot.Template.Language ?? "en", 
                resolvedVariables);

            if (sendResult.Success)
            {
                // Sync outbound template message to ChatMessages
                string bodyText = matchedBot.Template.BodyText ?? "";
                foreach (var v in resolvedVariables)
                {
                    bodyText = bodyText.Replace($"{{{{{v.Key}}}}}", v.Value);
                }

                var conversation = await _dbContext.ChatConversations.FirstOrDefaultAsync(c => c.ContactId == contact.Id);
                if (conversation != null)
                {
                    conversation.LastMessageText = bodyText;
                    conversation.LastMessageAt = DateTime.UtcNow;
                }

                _dbContext.ChatMessages.Add(new ChatMessage
                {
                    ConversationId = conversation?.Id ?? 0,
                    ContactId = contact.Id,
                    WhatsAppMessageId = sendResult.MessageId,
                    Direction = ChatMessageDirection.Outgoing,
                    Status = ChatMessageStatus.Sent,
                    Text = bodyText,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
                await _dbContext.SaveChangesAsync();

                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing template bot trigger for phone {Phone}", normalizedPhone);
        }

        return false;
    }
}
