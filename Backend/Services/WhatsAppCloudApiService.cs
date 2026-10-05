using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.Webhook;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Email;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Services.Realtime;
using WhatsAppCampaignApi.Services.Storage;

namespace WhatsAppCampaignApi.Services;

/// <summary>
/// Thrown when Meta reports a status for a message id we have not stored yet — the status raced
/// the send that records the id. The webhook job is retried a few seconds later.
/// </summary>
public sealed class WhatsAppStatusNotYetKnownException(string messageId)
    : Exception($"No stored message has WhatsApp id {messageId} yet.")
{
    public string MessageId { get; } = messageId;
}

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
    private readonly IOmniSettingsService _settings;
    private readonly IInboxNotifier _inboxNotifier;
    private readonly IEventPublisher _eventPublisher;
    private readonly IFileStorage _fileStorage;
    private readonly Microsoft.Extensions.Caching.Memory.IMemoryCache _cache;

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
        IServiceScopeFactory scopeFactory,
        IOmniSettingsService settings,
        IInboxNotifier inboxNotifier,
        IEventPublisher eventPublisher,
        IFileStorage fileStorage,
        Microsoft.Extensions.Caching.Memory.IMemoryCache cache)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _dbContext = dbContext;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _settings = settings;
        _inboxNotifier = inboxNotifier;
        _eventPublisher = eventPublisher;
        _fileStorage = fileStorage;
        _cache = cache;
    }

    private async Task<(string AccessToken, string PhoneNumberId, string BusinessAccountId)> GetActiveConfigAsync(string? requestedPhoneNumberId = null, int? connectionId = null)
    {
        WabaConfiguration? config = null;

        if (connectionId.HasValue)
        {
            config = await _dbContext.WabaConfigurations.FirstOrDefaultAsync(c => c.ConnectionId == connectionId.Value && c.Connected)
                ?? await _dbContext.WabaConfigurations.FirstOrDefaultAsync(c => c.ConnectionId == connectionId.Value);
        }

        if (config == null && !string.IsNullOrWhiteSpace(requestedPhoneNumberId))
        {
            var phoneAcc = await _dbContext.WabaPhoneNumbers.FirstOrDefaultAsync(p => p.PhoneNumberId == requestedPhoneNumberId);
            if (phoneAcc?.ConnectionId != null)
            {
                config = await _dbContext.WabaConfigurations.FirstOrDefaultAsync(c => c.ConnectionId == phoneAcc.ConnectionId && c.Connected)
                    ?? await _dbContext.WabaConfigurations.FirstOrDefaultAsync(c => c.ConnectionId == phoneAcc.ConnectionId);
            }
        }

        if (config == null && !connectionId.HasValue && string.IsNullOrWhiteSpace(requestedPhoneNumberId))
        {
            config = await _dbContext.WabaConfigurations.FirstOrDefaultAsync(c => c.Connected)
                ?? await _dbContext.WabaConfigurations.FirstOrDefaultAsync();
        }

        if (config == null) throw new InvalidOperationException($"WABA is not configured or connected for connection ID {connectionId}.");

        WabaPhoneNumber? phone = null;
        // 1. Prefer exact phone number match (most specific)
        if (!string.IsNullOrWhiteSpace(requestedPhoneNumberId))
        {
            phone = await _dbContext.WabaPhoneNumbers.FirstOrDefaultAsync(p => p.PhoneNumberId == requestedPhoneNumberId);
        }
        // 2. Fall back to connection-scoped phone
        if (phone == null && config.ConnectionId.HasValue)
        {
            phone = await _dbContext.WabaPhoneNumbers.FirstOrDefaultAsync(p => p.ConnectionId == config.ConnectionId.Value);
        }
        // 3. Global fallback only if nothing else matched
        if (phone == null)
        {
            phone = await _dbContext.WabaPhoneNumbers.FirstOrDefaultAsync();
        }

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
        Dictionary<string, string>? variables = null,
        int? connectionId = null)
    {
        var result = await SendTemplateMessageWithResultAsync(recipientPhone, templateName, languageCode, variables, connectionId);
        return result.Success ? result.MessageId : null;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The single choke point every template send passes through — campaigns, template bots and
    /// initiate-chat all land here. That is why the activity log is written from this method
    /// rather than from each caller: one place to record, and no way for a fourth caller added
    /// later to silently skip logging.
    /// </remarks>
    public async Task<WhatsAppSendResult> SendTemplateMessageWithResultAsync(
        string recipientPhone,
        string templateName,
        string languageCode,
        Dictionary<string, string>? variables = null,
        int? connectionId = null)
    {
        try
        {
            var whatsAppPhone = PhoneNumberHelper.FormatForWhatsApp(recipientPhone);
            var template = await _dbContext.Templates.FirstOrDefaultAsync(t => t.Name == templateName);
            var headerType = template?.HeaderType ?? HeaderType.None;

            var (accessToken, phoneNumberId, _) = await GetActiveConfigAsync(connectionId: connectionId);

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
                    // Uploaded once per file per sending number, not once per recipient: Meta
                    // media ids stay valid for 30 days, and re-uploading the same header image for
                    // every recipient of a large campaign was pure waste of time and bandwidth.
                    var mediaCacheKey = $"wa-media:{phoneNumberId}:{fileVar.Value}";
                    if (!_cache.TryGetValue(mediaCacheKey, out mediaId))
                    {
                        mediaId = await UploadMediaToWhatsAppAsync(fileVar.Value, mediaTypeStr, Path.GetFileName(fileVar.Value) ?? "file", accessToken, phoneNumberId);
                        if (!string.IsNullOrEmpty(mediaId))
                        {
                            _cache.Set(mediaCacheKey, mediaId, TimeSpan.FromHours(12));
                        }
                    }
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
                    components = BuildTemplateComponents(variables, headerType, mediaId, template?.ButtonsJson)
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

                return WhatsAppSendResult.Failed(errorMessage, (int)response.StatusCode);
            }

            using var doc = JsonDocument.Parse(responseBody);
            var messageId = doc.RootElement
                .GetProperty("messages")[0]
                .GetProperty("id")
                .GetString();

            _logger.LogInformation(
                "WhatsApp message sent successfully. MessageId: {MessageId}", messageId);

            return WhatsAppSendResult.Sent(messageId, (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending WhatsApp template message to {Phone}", recipientPhone);

            // Network failures and the like never reached Meta, so there is no response code.
            return WhatsAppSendResult.Failed(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<WhatsAppSendResult> SendTextMessageAsync(string recipientPhone, string text, string? fromPhoneNumberId = null, int? connectionId = null)
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
            var (accessToken, phoneNumberId, _) = await GetActiveConfigAsync(fromPhoneNumberId, connectionId);

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
                        Status = item.GetProperty("status").GetString() ?? string.Empty,
                        TemplateType = "TEXT"
                    };

                    if (item.TryGetProperty("rejected_reason", out var rejectProp))
                    {
                        templateInfo.RejectReason = rejectProp.GetString();
                    }

                    if (item.TryGetProperty("components", out var components))
                    {
                        foreach (var component in components.EnumerateArray())
                        {
                            var compType = component.GetProperty("type").GetString();
                            if (compType == "HEADER")
                            {
                                if (component.TryGetProperty("format", out var formatProp))
                                {
                                    var formatVal = formatProp.GetString()?.ToUpper();
                                    if (formatVal == "IMAGE") templateInfo.TemplateType = "IMAGE";
                                    else if (formatVal == "VIDEO") templateInfo.TemplateType = "VIDEO";
                                    else if (formatVal == "DOCUMENT") templateInfo.TemplateType = "DOCUMENT";
                                }
                            }
                            else if (compType == "BODY")
                            {
                                templateInfo.BodyText = component.GetProperty("text").GetString();
                            }
                        }
                    }

                    if (item.TryGetProperty("components", out var allComponents) && allComponents.ValueKind == JsonValueKind.Array)
                    {
                        // Buttons and carousels were dropped here, so templates using them could not be
                        // filled in at send time. Kept now, with the raw components for reference.
                        var (templateButtons, isCarousel) = WhatsApp.TemplateComponents.ReadFromMeta(allComponents);
                        templateInfo.ComponentsJson = allComponents.GetRawText();
                        templateInfo.ButtonsJson = templateButtons.Count > 0
                            ? JsonSerializer.Serialize(templateButtons, WhatsApp.TemplateComponents.Json)
                            : null;
                        if (isCarousel) templateInfo.TemplateType = "CAROUSEL";
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

    /// <summary>
    /// Decides what a failed template fetch means, and stops the retry loop when the answer is
    /// "these credentials will never work again".
    ///
    /// <para>
    /// Meta distinguishes two kinds of refusal, and so must we. An expired or invalid token
    /// (OAuthException, code 190) and a WABA the app cannot see (code 100, subcode 33) are
    /// permanent until a human reconnects the account — retrying every fifteen minutes forever
    /// achieves nothing except an error in the log every fifteen minutes forever, which is how a
    /// real problem ends up looking like background noise. Those mark the configuration
    /// disconnected, which takes it out of the sync and surfaces it in the UI as needing
    /// attention.
    /// </para>
    /// <para>
    /// Everything else — a 500 from Meta, a timeout, a transport blip — is transient and is left
    /// alone to retry on the next cycle. Demoting a live connection because Meta had a bad minute
    /// would be its own outage.
    /// </para>
    /// </summary>
    private async Task HandleTemplateFetchFailureAsync(
        int connectionId,
        WabaConfiguration config,
        System.Net.HttpStatusCode status,
        string responseBody)
    {
        var (code, subcode, message) = ParseMetaError(responseBody);

        var isPermanent =
            code == 190 ||                        // token expired, revoked or malformed
            (code == 100 && subcode == 33) ||     // object missing, or app lacks permission on it
            status == System.Net.HttpStatusCode.Forbidden;

        if (!isPermanent)
        {
            // Warning, not Error: a transient failure that the next cycle will retry is not an
            // incident, and logging it at Error trains people to ignore the level that matters.
            _logger.LogWarning(
                "Template fetch for connection {ConnectionId} failed transiently ({Status}). Will retry. {Message}",
                connectionId, status, message);
            return;
        }

        if (!config.Connected)
        {
            // Already demoted; say nothing. This is what stops the same line repeating on every
            // sync for an account nobody has reconnected yet.
            return;
        }

        config.Connected = false;
        config.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _dbContext.SaveChangesAsync();

            _logger.LogWarning(
                "Connection {ConnectionId} has been marked disconnected: WhatsApp rejected its credentials " +
                "permanently ({Message}). Reconnect the account to resume template sync.",
                connectionId, message);
        }
        catch (Exception ex)
        {
            // Never let bookkeeping break a read. The next cycle will try to demote it again.
            _logger.LogWarning(ex, "Could not mark connection {ConnectionId} disconnected.", connectionId);
        }
    }

    /// <summary>
    /// Pulls Meta's error code, subcode and message out of a Graph API error body.
    ///
    /// Tolerant of a body that is not the expected shape — a proxy error page or a truncated
    /// response must not turn a handled failure into an unhandled one.
    /// </summary>
    private static (int Code, int Subcode, string Message) ParseMetaError(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (!doc.RootElement.TryGetProperty("error", out var error))
                return (0, 0, "No error detail returned.");

            var code = error.TryGetProperty("code", out var c) && c.TryGetInt32(out var ci) ? ci : 0;
            var sub = error.TryGetProperty("error_subcode", out var sc) && sc.TryGetInt32(out var si) ? si : 0;
            var msg = error.TryGetProperty("message", out var m) ? m.GetString() ?? string.Empty : string.Empty;

            return (code, sub, msg);
        }
        catch
        {
            return (0, 0, "Unreadable error response.");
        }
    }

    public async Task<List<WhatsAppTemplateInfo>> GetTemplatesForConnectionAsync(int connectionId)
    {
        try
        {
            var config = await _dbContext.WabaConfigurations.FirstOrDefaultAsync(c => c.ConnectionId == connectionId);
            if (config == null || string.IsNullOrEmpty(config.WabaId) || string.IsNullOrEmpty(config.AccessToken))
            {
                return await GetTemplatesAsync();
            }

            var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/{config.WabaId}/message_templates?limit=100");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken);

            var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                await HandleTemplateFetchFailureAsync(connectionId, config, response.StatusCode, responseBody);

                // Deliberately no fallback to GetTemplatesAsync().
                //
                // That fallback fetched with whichever configuration happens to come first, so a
                // connection whose own credentials had failed quietly displayed a *different*
                // account's templates as its own. For a product where a template is the thing you
                // send to someone else's customers, showing the wrong account's is worse than
                // showing none — and it also doubled every failure in the log, once for this
                // connection and once for the fallback.
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
                        Status = item.GetProperty("status").GetString() ?? string.Empty,
                        TemplateType = "TEXT"
                    };

                    if (item.TryGetProperty("rejected_reason", out var rejectProp))
                    {
                        templateInfo.RejectReason = rejectProp.GetString();
                    }

                    if (item.TryGetProperty("components", out var components))
                    {
                        foreach (var component in components.EnumerateArray())
                        {
                            var type = component.GetProperty("type").GetString();
                            if (type == "BODY" && component.TryGetProperty("text", out var bodyText))
                            {
                                templateInfo.BodyText = bodyText.GetString() ?? string.Empty;
                            }
                            else if (type == "HEADER")
                            {
                                var format = component.GetProperty("format").GetString();
                                templateInfo.TemplateType = format ?? "TEXT";
                                if (component.TryGetProperty("text", out var headerText))
                                {
                                    templateInfo.HeaderContent = headerText.GetString();
                                }
                            }
                            else if (type == "FOOTER" && component.TryGetProperty("text", out var footerText))
                            {
                                templateInfo.FooterText = footerText.GetString();
                            }
                        }
                    }

                    if (item.TryGetProperty("components", out var allComponents) && allComponents.ValueKind == JsonValueKind.Array)
                    {
                        // Buttons and carousels were dropped here, so templates using them could not be
                        // filled in at send time. Kept now, with the raw components for reference.
                        var (templateButtons, isCarousel) = WhatsApp.TemplateComponents.ReadFromMeta(allComponents);
                        templateInfo.ComponentsJson = allComponents.GetRawText();
                        templateInfo.ButtonsJson = templateButtons.Count > 0
                            ? JsonSerializer.Serialize(templateButtons, WhatsApp.TemplateComponents.Json)
                            : null;
                        if (isCarousel) templateInfo.TemplateType = "CAROUSEL";
                    }

                    templates.Add(templateInfo);
                }
            }

            return templates;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching templates for connection {ConnectionId}", connectionId);
            return await GetTemplatesAsync();
        }
    }

    public bool VerifyWebhook(string mode, string token, string challenge)
    {
        // Multi-connection support: check ALL WABA configurations for a matching verify token
        if (string.IsNullOrEmpty(token)) return false;

        var matchingConfig = _dbContext.WabaConfigurations
            .AsNoTracking()
            .Where(c => c.VerifyToken == token)
            .Select(c => new { c.Id, c.ConnectionId })
            .FirstOrDefault();

        if (matchingConfig == null)
        {
            // Fallback to appsettings.json token
            var fallbackToken = _configuration["WhatsApp:VerifyToken"] ?? _configuration["WhatsApp:WebhookVerifyToken"];
            if (!string.IsNullOrWhiteSpace(fallbackToken) && mode == "subscribe" && token == fallbackToken)
            {
                _logger.LogInformation("Webhook verified successfully using appsettings fallback token.");
                return true;
            }

            _logger.LogWarning("Webhook verification failed. Mode: {Mode}; the token matched no configured connection.", mode);
            return false;
        }

        if (mode == "subscribe")
        {
            _logger.LogInformation("Webhook verified successfully for connection {ConnectionId} (Config Id: {ConfigId}).", matchingConfig.ConnectionId, matchingConfig.Id);
            return true;
        }

        _logger.LogWarning("Webhook verification failed. Mode: {Mode} (expected 'subscribe'), Token matched config {ConfigId}", mode, matchingConfig.Id);
        return false;
    }

    /// <inheritdoc />
    public async Task ProcessWebhookAsync(WhatsAppWebhookPayload payload)
    {
        if (payload.Entry == null) return;

        // Re-send runs after the loop below, never before it: the customer endpoint is secondary,
        // and Meta re-delivers anything we are slow to acknowledge.
        var unknownStatuses = new List<string>();

        foreach (var entry in payload.Entry)
        {
            if (entry.Changes == null) continue;

            foreach (var change in entry.Changes)
            {
                if (change.Value?.Statuses != null)
                {
                    foreach (var statusUpdate in change.Value.Statuses)
                    {
                        try
                        {
                            await ProcessStatusUpdateAsync(statusUpdate);
                        }
                        catch (WhatsAppStatusNotYetKnownException ex)
                        {
                            unknownStatuses.Add(ex.MessageId);
                        }
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

        if (unknownStatuses.Count > 0)
        {
            // Everything else in the payload has been applied (and re-applying it is a no-op),
            // so the caller can retry this payload to pick up the statuses that raced their send.
            throw new WhatsAppStatusNotYetKnownException(string.Join(",", unknownStatuses));
        }

        // Forwarded on a detached task so a slow customer endpoint cannot hold up the 200 owed to
        // Meta. The forwarder never throws, and takes its own scope because this one ends with the
        // request.
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var forwarder = scope.ServiceProvider.GetRequiredService<IWebhookForwarder>();
                await forwarder.ForwardAsync(payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Webhook forwarding task failed.");
            }
        });
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

        var referral = incomingMessage.Referral;

        if (contact == null)
        {
            contact = new Contact
            {
                Name = !string.IsNullOrWhiteSpace(contactName) ? contactName : normalizedPhone,
                Phone = normalizedPhone,
                Type = nameof(ContactType.Lead),
                Status = nameof(ContactStatus.New),
                Source = nameof(ContactSource.WhatsApp),
                IsActive = true
            };

            await ApplyAutoLeadSettingsAsync(contact);

            _dbContext.Contacts.Add(contact);
            try
            {
                await _dbContext.SaveChangesAsync();

                // Created by the system from an incoming message: audited like any new contact.
                using var auditScope = _scopeFactory.CreateScope();
                await auditScope.ServiceProvider.GetRequiredService<IAuditService>().LogAsync(
                    "Contact.Created", "Data",
                    $"Created contact \"{contact.Name}\" ({contact.Phone}) automatically from an incoming WhatsApp message.",
                    "Contact", contact.Id.ToString());
            }
            catch (DbUpdateException)
            {
                // Two first messages from the same new number, processed at once: the unique
                // phone index let one insert win. Adopt that row instead of failing the webhook.
                _dbContext.Entry(contact).State = EntityState.Detached;
                contact = await _dbContext.Contacts.IgnoreQueryFilters().FirstAsync(c => c.Phone == normalizedPhone);
            }
        }

        // Click-to-WhatsApp: the first ad that brought this person in is kept (first touch).
        if (referral is not null && contact.AdAttributedAt is null
            && (!string.IsNullOrWhiteSpace(referral.SourceId) || !string.IsNullOrWhiteSpace(referral.CtwaClid)))
        {
            static string? Cap(string? v, int max) => string.IsNullOrWhiteSpace(v) ? null : v.Length <= max ? v : v[..max];
            contact.AdSourceId = Cap(referral.SourceId, 100);
            contact.AdSourceUrl = Cap(referral.SourceUrl, 500);
            contact.AdHeadline = Cap(referral.Headline, 300);
            contact.AdClickId = Cap(referral.CtwaClid, 200);
            contact.AdAttributedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }

        if (contact.IsDeleted || !contact.IsActive)
        {
            // A deleted or deactivated contact who writes in again is a returning customer. The
            // conversation lookups below exclude deleted contacts, so leaving it deleted made every
            // message from them try to create a second conversation and fail on the unique index —
            // and Meta then retried the same failing delivery indefinitely.
            contact.IsDeleted = false;
            contact.IsActive = true;
            await _dbContext.SaveChangesAsync();
        }

        // Never "the first phone of any connection": a message without a phone_number_id cannot be
        // attributed, and guessing would file a customer's message under another bank's number.
        var account = !string.IsNullOrWhiteSpace(fromPhoneNumberId)
            ? await _dbContext.WabaPhoneNumbers.FirstOrDefaultAsync(p => p.PhoneNumberId == fromPhoneNumberId)
            : null;

        var connectionId = account?.ConnectionId;

        // Auto-sync: If the phone number wasn't found in WabaPhoneNumbers, try to resolve it
        // by querying all connected WABA configs against Meta Graph API and auto-register the phone.
        // At most once per unknown id per ten minutes, so a stream of messages for a number that
        // is not ours cannot turn into a stream of Graph calls.
        if (connectionId == null && !string.IsNullOrWhiteSpace(fromPhoneNumberId)
            && _cache.TryGetValue($"wa-autosync:{fromPhoneNumberId}", out _) == false)
        {
            _cache.Set($"wa-autosync:{fromPhoneNumberId}", true, TimeSpan.FromMinutes(10));
            _logger.LogInformation("Phone number ID {PhoneNumberId} not found in WabaPhoneNumbers. Attempting auto-sync from Meta Graph API.", fromPhoneNumberId);
            var connectedConfigs = await _dbContext.WabaConfigurations
                .Where(c => c.Connected && c.ConnectionId.HasValue)
                .ToListAsync();

            foreach (var cfg in connectedConfigs)
            {
                try
                {
                    var phones = await FetchPhoneNumbersFromMetaAsync(cfg.WabaId, cfg.AccessToken);
                    var match = phones?.FirstOrDefault(p => p.PhoneNumberId == fromPhoneNumberId);
                    if (match != null)
                    {
                        // Check if already exists (race condition guard)
                        var existing = await _dbContext.WabaPhoneNumbers.FirstOrDefaultAsync(p => p.PhoneNumberId == fromPhoneNumberId);
                        if (existing == null)
                        {
                            var newPhone = new WabaPhoneNumber
                            {
                                PhoneNumberId = match.PhoneNumberId,
                                PhoneNumber = match.PhoneNumber,
                                DisplayName = match.DisplayName,
                                VerifiedName = match.VerifiedName,
                                Quality = match.Quality,
                                Status = match.Status,
                                ConnectionId = cfg.ConnectionId
                            };
                            _dbContext.WabaPhoneNumbers.Add(newPhone);
                            await _dbContext.SaveChangesAsync();
                            account = newPhone;
                            _logger.LogInformation("Auto-synced phone number {PhoneNumberId} to connection {ConnectionId}.", fromPhoneNumberId, cfg.ConnectionId);
                        }
                        else
                        {
                            // Update ConnectionId if it was null
                            if (existing.ConnectionId == null)
                            {
                                existing.ConnectionId = cfg.ConnectionId;
                                await _dbContext.SaveChangesAsync();
                            }
                            account = existing;
                        }
                        connectionId = cfg.ConnectionId;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to query Meta Graph API for WABA {WabaId} during auto-sync.", cfg.WabaId);
                }
            }
        }

        var conversation = await _dbContext.ChatConversations
            .FirstOrDefaultAsync(c => c.ContactId == contact.Id
                                   && c.Channel == MessageChannel.WhatsApp
                                   && (connectionId == null || c.ConnectionId == connectionId));

        if (conversation == null)
        {
            conversation = new ChatConversation
            {
                ContactId = contact.Id,
                ConnectionId = connectionId,
                WabaPhoneNumberId = account?.Id
            };
            _dbContext.ChatConversations.Add(conversation);
        }
        else if (conversation.ConnectionId == null && connectionId.HasValue)
        {
            conversation.ConnectionId = connectionId;
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

        _dbContext.ChatMessages.Add(new ChatMessage
        {
            Conversation = conversation,
            ContactId = contact.Id,
            ConnectionId = connectionId,
            WhatsAppMessageId = incomingMessage.Id,
            Direction = ChatMessageDirection.Incoming,
            Status = ChatMessageStatus.Received,
            Text = text
        });

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Meta delivered the same message twice concurrently and the other delivery stored it
            // first; the unique index on the WhatsApp id is what caught it. Anything else is real.
            if (await _dbContext.ChatMessages.AsNoTracking().AnyAsync(m => m.WhatsAppMessageId == incomingMessage.Id)) return;
            throw;
        }

        // Incremented in the database: an agent opening the thread resets it concurrently, and a
        // read-modify-write on the tracked entity loses one of the two.
        var unreadConversationId = conversation.Id;
        await _dbContext.ChatConversations
            .Where(c => c.Id == unreadConversationId)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.UnreadCount, c => c.UnreadCount + 1));

        await _inboxNotifier.MessageReceivedAsync(conversation.Id, connectionId);

        // Reopen / SLA clock / routing. Never allowed to break message ingestion.
        try
        {
            using var opsScope = _scopeFactory.CreateScope();
            await opsScope.ServiceProvider.GetRequiredService<Chat.IConversationOperations>().OnInboundAsync(conversation.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Conversation {ConversationId}: inbound bookkeeping (SLA/routing) failed.", conversation.Id);
        }

        // Consent keywords (STOP / START, configurable). Recorded with the message as proof and
        // confirmed to the customer; the keyword is not passed on to a bot, so nobody who just
        // opted out receives an automated reply.
        using (var consentScope = _scopeFactory.CreateScope())
        {
            var consent = consentScope.ServiceProvider.GetRequiredService<Compliance.IConsentService>();
            var changed = await consent.TryApplyWhatsAppKeywordAsync(contact.Id, text, incomingMessage.Id);
            if (changed is { } consentStatus)
            {
                var settings = consentScope.ServiceProvider.GetRequiredService<IOmniSettingsService>();
                var reply = consentStatus == ConsentStatus.OptedOut
                    ? await settings.GetValueAsync("compliance.optOutReply")
                    : await settings.GetValueAsync("compliance.optInReply");
                reply = string.IsNullOrWhiteSpace(reply)
                    ? consentStatus == ConsentStatus.OptedOut
                        ? "You have been unsubscribed from our WhatsApp messages. Reply START to subscribe again."
                        : "You are subscribed to our WhatsApp messages. Reply STOP to unsubscribe."
                    : reply;

                try
                {
                    await SendTextMessageAsync(contact.Phone, reply, fromPhoneNumberId, connectionId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not confirm the consent change to contact {ContactId}.", contact.Id);
                }
                return;
            }
        }

        // Start flow execution asynchronously.
        //
        // Detached so Meta gets its 200 immediately, but serialised per customer: a bot turn reads
        // the conversation state, decides, and writes the next state, and two of those interleaving
        // on one person corrupted the conversation — duplicate greetings, menus answering the wrong
        // message, and a "stop" undone by a task still running from the message before it.
        _ = Task.Run(async () =>
        {
            using var turn = await ConversationGate.EnterAsync(normalizedPhone);

            if (turn is null)
            {
                _logger.LogWarning(
                    "Timed out waiting for the previous bot turn for {Phone}; skipping this message rather than running two at once.",
                    normalizedPhone);
                return;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                
                // Fetch contact using the new scoped dbContext to avoid cross-context tracking and disposed context issues
                var dbContact = await dbContext.Contacts.FindAsync(contact.Id);
                if (dbContact == null) return;

                var routerService = scope.ServiceProvider.GetRequiredService<IBotRouterService>();
                await routerService.RouteMessageAsync(normalizedPhone, text, dbContact, connectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error routing incoming message in background task for phone {Phone}", normalizedPhone);
            }
        });
    }

    /// <summary>
    /// Stamps a newly seen WhatsApp sender with the configured lead status, source and owner.
    ///
    /// <para>
    /// Only for a contact that does not exist yet — this is called from the one branch that
    /// constructs one, so a returning sender keeps whatever a human has since set on them. That is
    /// what stops the setting quietly re-classifying an existing customer as a new lead every time
    /// they send a message.
    /// </para>
    /// <para>
    /// When the toggle is off the contact is still created, because the conversation and its
    /// messages cannot exist without one and dropping it would lose the inbound message entirely.
    /// What the toggle governs is whether the contact is <em>enrolled as a lead</em>: with it off
    /// the row keeps the neutral defaults it has always had, and no status, source or owner from
    /// this screen is applied.
    /// </para>
    /// <para>
    /// Nothing here is hardcoded. The status and source are whatever the operator chose from the
    /// lookup tables, and the owner is resolved from the stored user id to the display name,
    /// because <see cref="Contact.AssignedTo"/> holds a name — that is what the contact form
    /// writes into it, and a mismatch would leave the contact assigned to nobody the UI can find.
    /// </para>
    /// </summary>
    private async Task ApplyAutoLeadSettingsAsync(Contact contact)
    {
        try
        {
            if (!await _settings.GetFlagAsync("autoLead.enabled")) return;

            var status = await _settings.GetValueAsync("autoLead.status");
            if (!string.IsNullOrWhiteSpace(status)) contact.Status = status;

            var source = await _settings.GetValueAsync("autoLead.source");
            if (!string.IsNullOrWhiteSpace(source)) contact.Source = source;

            var assignedUserId = await _settings.GetValueAsync("autoLead.assignedUserId");
            if (!string.IsNullOrWhiteSpace(assignedUserId) && int.TryParse(assignedUserId, out var userId))
            {
                var owner = await _dbContext.AppUsers.AsNoTracking()
                    .Where(u => u.Id == userId && u.IsActive)
                    .Select(u => new { u.FirstName, u.LastName })
                    .FirstOrDefaultAsync();

                if (owner != null)
                {
                    contact.AssignedTo = string.IsNullOrWhiteSpace(owner.LastName)
                        ? owner.FirstName
                        : $"{owner.FirstName} {owner.LastName}".Trim();
                }
            }

            _logger.LogInformation(
                "Auto Lead applied to {Phone}: status={Status}, source={Source}, assignedTo={AssignedTo}",
                contact.Phone, contact.Status, contact.Source, contact.AssignedTo ?? "(unassigned)");
        }
        catch (Exception ex)
        {
            // A settings failure must not cost us the inbound message. The contact is still
            // created with its defaults and the conversation proceeds.
            _logger.LogError(ex, "Could not apply Auto Lead settings to incoming contact {Phone}.", contact.Phone);
        }
    }

    /// <summary>
    /// Applies one delivery-status callback to the campaign recipient and the chat message.
    /// </summary>
    /// <remarks>
    /// Each transition is a conditional UPDATE on the expected previous status, so concurrent
    /// webhook deliveries for the same message cannot both apply, and statuses that arrive out of
    /// order ("delivered" after "read") never move a row backwards. The campaign counters then move
    /// by the delta of that one transition — O(1) per status. This used to reload every recipient
    /// of the campaign on every status (three statuses per message, so O(n²) per campaign), which
    /// is what made large WhatsApp campaigns slow the whole database down.
    /// </remarks>
    private async Task ProcessStatusUpdateAsync(StatusUpdate statusUpdate)
    {
        if (string.IsNullOrWhiteSpace(statusUpdate.Id)) return;

        var target = ParseWhatsAppStatus(statusUpdate.Status);
        if (target is null)
        {
            _logger.LogWarning("Unknown status '{Status}' for message {MessageId}", statusUpdate.Status, statusUpdate.Id);
            return;
        }

        var now = DateTime.UtcNow;
        var error = statusUpdate.Errors?.FirstOrDefault();
        var errorMessage = Truncate(error?.ErrorData?.Details ?? error?.Message ?? error?.Title ?? "Message delivery failed", 500);

        var recipientUpdated = await ApplyRecipientStatusAsync(statusUpdate.Id, target.Value, now, errorMessage);
        var chatUpdated = await ApplyChatMessageStatusAsync(statusUpdate.Id, target.Value, now, errorMessage);

        if (!recipientUpdated.Found && !chatUpdated.Found)
        {
            // Meta can report a status before our send has stored the message id. Thrown so the
            // webhook job is retried shortly instead of the status being lost.
            throw new WhatsAppStatusNotYetKnownException(statusUpdate.Id);
        }

        if (chatUpdated.ConversationId is { } conversationId && chatUpdated.MessageId is { } chatMessageId)
        {
            await _inboxNotifier.MessageStatusChangedAsync(conversationId, chatMessageId, target.Value.ToString());
        }
    }

    private static MessageStatus? ParseWhatsAppStatus(string? status) => status?.ToLowerInvariant() switch
    {
        "sent" => MessageStatus.Sent,
        "delivered" => MessageStatus.Delivered,
        "read" => MessageStatus.Read,
        "failed" => MessageStatus.Failed,
        _ => null
    };

    /// <summary>Forward-only order of the delivery states. Failed is terminal from any of them.</summary>
    private static int Rank(MessageStatus status) => status switch
    {
        MessageStatus.Pending => 0,
        MessageStatus.Sent => 1,
        MessageStatus.Delivered => 2,
        MessageStatus.Read => 3,
        _ => 4
    };

    private async Task<(bool Found, bool Changed)> ApplyRecipientStatusAsync(
        string whatsAppMessageId, MessageStatus target, DateTime now, string errorMessage)
    {
        // Two attempts: the first can lose a race with a concurrent status for the same message,
        // in which case the re-read sees the winner and decides again.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var current = await _dbContext.CampaignContacts
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(cc => cc.WhatsAppMessageId == whatsAppMessageId)
                .Select(cc => new { cc.Id, cc.CampaignId, cc.Status })
                .FirstOrDefaultAsync();

            if (current is null) return (false, false);

            var previous = current.Status;
            var isAdvance = target == MessageStatus.Failed
                ? previous is not (MessageStatus.Failed or MessageStatus.Read)
                : Rank(target) > Rank(previous) && previous != MessageStatus.Failed;

            if (!isAdvance) return (true, false);

            var rows = await _dbContext.CampaignContacts
                .IgnoreQueryFilters()
                .Where(cc => cc.Id == current.Id && cc.Status == previous)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(cc => cc.Status, target)
                    .SetProperty(cc => cc.SentAt, cc => target != MessageStatus.Failed ? (cc.SentAt ?? now) : cc.SentAt)
                    .SetProperty(cc => cc.DeliveredAt, cc => target == MessageStatus.Delivered || target == MessageStatus.Read ? (cc.DeliveredAt ?? now) : cc.DeliveredAt)
                    .SetProperty(cc => cc.ReadAt, cc => target == MessageStatus.Read ? (cc.ReadAt ?? now) : cc.ReadAt)
                    .SetProperty(cc => cc.ErrorMessage, cc => target == MessageStatus.Failed ? errorMessage : cc.ErrorMessage));

            if (rows == 0) continue;

            await ApplyCampaignDeltaAsync(current.CampaignId, previous, target, now);
            return (true, true);
        }

        return (true, false);
    }

    /// <summary>
    /// Moves the campaign's WhatsApp counters by exactly one recipient's transition, and pushes
    /// the same delta to open campaign pages.
    /// </summary>
    private async Task ApplyCampaignDeltaAsync(int campaignId, MessageStatus previous, MessageStatus next, DateTime now)
    {
        static int Delivered(MessageStatus s) => s is MessageStatus.Delivered or MessageStatus.Read ? 1 : 0;
        static int Read(MessageStatus s) => s == MessageStatus.Read ? 1 : 0;
        static int Failed(MessageStatus s) => s == MessageStatus.Failed ? 1 : 0;
        static int Sent(MessageStatus s) => s is MessageStatus.Sent or MessageStatus.Delivered or MessageStatus.Read ? 1 : 0;

        var deliveredDelta = Delivered(next) - Delivered(previous);
        var readDelta = Read(next) - Read(previous);
        var failedDelta = Failed(next) - Failed(previous);
        var sentDelta = Sent(next) - Sent(previous);

        if (deliveredDelta == 0 && readDelta == 0 && failedDelta == 0 && sentDelta == 0) return;

        await _dbContext.Campaigns
            .IgnoreQueryFilters()
            .Where(c => c.Id == campaignId)
            .ExecuteUpdateAsync(u => u
                .SetProperty(c => c.DeliveredCount, c => c.DeliveredCount + deliveredDelta)
                .SetProperty(c => c.ReadCount, c => c.ReadCount + readDelta)
                .SetProperty(c => c.FailedCount, c => c.FailedCount + failedDelta)
                .SetProperty(c => c.SentCount, c => c.SentCount + sentDelta)
                .SetProperty(c => c.UpdatedAt, now));

        await _eventPublisher.PublishEmailEventAsync(new CampaignEmailEventNotification(
            CampaignId: campaignId,
            Kind: next switch
            {
                MessageStatus.Read => EmailEventKind.Opened,
                MessageStatus.Delivered => EmailEventKind.Delivered,
                MessageStatus.Failed => EmailEventKind.Failed,
                _ => EmailEventKind.Sent
            },
            CampaignContactId: null,
            RecipientAddress: null,
            OccurredAt: now,
            SentDelta: sentDelta,
            FailedDelta: failedDelta,
            DeliveredDelta: deliveredDelta,
            ReadDelta: readDelta));
    }

    private async Task<(bool Found, int? ConversationId, int? MessageId)> ApplyChatMessageStatusAsync(
        string whatsAppMessageId, MessageStatus target, DateTime now, string errorMessage)
    {
        var message = await _dbContext.ChatMessages
            .AsNoTracking()
            .Where(m => m.WhatsAppMessageId == whatsAppMessageId)
            .Select(m => new { m.Id, m.ConversationId, m.Status })
            .FirstOrDefaultAsync();

        if (message is null) return (false, null, null);

        var chatTarget = target switch
        {
            MessageStatus.Sent => ChatMessageStatus.Sent,
            MessageStatus.Delivered => ChatMessageStatus.Delivered,
            MessageStatus.Read => ChatMessageStatus.Read,
            _ => ChatMessageStatus.Failed
        };

        // The statuses a message may move forward from, per target.
        var allowedFrom = chatTarget switch
        {
            ChatMessageStatus.Sent => new[] { ChatMessageStatus.Pending },
            ChatMessageStatus.Delivered => new[] { ChatMessageStatus.Pending, ChatMessageStatus.Sent },
            ChatMessageStatus.Read => new[] { ChatMessageStatus.Pending, ChatMessageStatus.Sent, ChatMessageStatus.Delivered },
            _ => new[] { ChatMessageStatus.Pending, ChatMessageStatus.Sent, ChatMessageStatus.Delivered }
        };

        var rows = await _dbContext.ChatMessages
            .Where(m => m.Id == message.Id && allowedFrom.Contains(m.Status))
            .ExecuteUpdateAsync(u => u
                .SetProperty(m => m.Status, chatTarget)
                .SetProperty(m => m.SentAt, m => chatTarget != ChatMessageStatus.Failed ? (m.SentAt ?? now) : m.SentAt)
                .SetProperty(m => m.DeliveredAt, m => chatTarget == ChatMessageStatus.Delivered || chatTarget == ChatMessageStatus.Read ? (m.DeliveredAt ?? now) : m.DeliveredAt)
                .SetProperty(m => m.ReadAt, m => chatTarget == ChatMessageStatus.Read ? (m.ReadAt ?? now) : m.ReadAt)
                .SetProperty(m => m.ErrorMessage, m => chatTarget == ChatMessageStatus.Failed ? errorMessage : m.ErrorMessage));

        return rows > 0 ? (true, message.ConversationId, message.Id) : (true, null, null);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private static string? ExtractMetaErrorMessage(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody)) return null;

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                // Prefer "message" — Meta's raw, non-localized error text — over
                // "error_user_msg", which Meta pre-localizes server-side based on the
                // Business Manager account's configured display language (this is why
                // errors were showing up in Hindi regardless of any header this app
                // sends; there is no Accept-Language/locale knob on our side to control
                // it). This is a mitigation, not a guaranteed fix — it only helps when
                // Meta's response actually includes "message"; Meta ultimately controls
                // what each field contains.
                if (error.TryGetProperty("message", out var message))
                    return message.GetString();

                if (error.TryGetProperty("error_user_msg", out var userMessage))
                    return userMessage.GetString();

                if (error.TryGetProperty("error_data", out var errorData)
                    && errorData.TryGetProperty("details", out var details))
                    return details.GetString();
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static object[]? BuildTemplateComponents(Dictionary<string, string>? variables, HeaderType headerType, string? mediaId = null, string? buttonsJson = null)
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

        // Add regular text variables to the body parameters (button_* and card_* fill buttons and
        // carousel cards, below, and are not body parameters).
        var bodyParams = variables
            .Where(v => !WhatsApp.TemplateComponents.IsNonBodyVariable(v.Key))
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

        componentsList.AddRange(WhatsApp.TemplateComponents.BuildSendComponents(variables, buttonsJson));

        return componentsList.Count > 0 ? componentsList.ToArray() : null;
    }

    /// <inheritdoc />
    public async Task<(bool Success, string? TemplateId, string? Status, string? Error)> SubmitTemplateAsync(int connectionId, object submission)
    {
        var config = await _dbContext.WabaConfigurations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ConnectionId == connectionId && c.Connected);
        if (config is null) return (false, null, null, "That connection has no connected WhatsApp Business Account.");

        var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/{config.WabaId}/message_templates")
        {
            Content = new StringContent(JsonSerializer.Serialize(submission), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AccessToken);

        var response = await _httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Meta refused a template submission: {Status} {Body}", response.StatusCode, body);
            return (false, null, null, ExtractMetaErrorMessage(body) ?? $"Meta refused the template ({(int)response.StatusCode}).");
        }

        using var doc = JsonDocument.Parse(body);
        var id = doc.RootElement.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
        var status = doc.RootElement.TryGetProperty("status", out var st) ? st.GetString() : "PENDING";
        return (true, id, status, null);
    }

    /// <inheritdoc />
    public async Task<WhatsAppSendResult> SendMediaMessageAsync(
        string recipientPhone,
        string mediaUrl,
        string mediaType,
        string? filename = null,
        string? caption = null,
        string? fromPhoneNumberId = null,
        int? connectionId = null)
    {
        try
        {
            var whatsAppPhone = PhoneNumberHelper.FormatForWhatsApp(recipientPhone);
            var (accessToken, activePhoneNumberId, _) = await GetActiveConfigAsync(fromPhoneNumberId, connectionId);
            var finalPhoneNumberId = activePhoneNumberId;

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
    public async Task<WhatsAppSendResult> SendCustomPayloadAsync(string recipientPhone, object payload, string? fromPhoneNumberId = null, int? connectionId = null)
    {
        try
        {
            var whatsAppPhone = PhoneNumberHelper.FormatForWhatsApp(recipientPhone);
            var (accessToken, activePhoneNumberId, _) = await GetActiveConfigAsync(fromPhoneNumberId, connectionId);
            var finalPhoneNumberId = activePhoneNumberId;

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
            else
            {
                // Local uploads and allow-listed remote hosts only, both through the storage
                // gatekeeper: this used to join any caller-supplied path onto the web root (so
                // "../appsettings.json" worked) and fetch any URL with a throwaway HttpClient.
                var loaded = await _fileStorage.ReadAsync(mediaUrl, 100L * 1024 * 1024, CancellationToken.None);
                if (loaded is null)
                {
                    _logger.LogError("Media {Media} is not an upload on this server or an allow-listed URL.", Path.GetFileName(mediaUrl));
                    return null;
                }

                fileBytes = loaded;
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

    /// <summary>
    /// Fetches phone numbers from Meta Graph API for a given WABA ID.
    /// Used for auto-syncing unrecognized phone numbers during webhook processing.
    /// </summary>
    private async Task<List<WabaPhoneNumber>> FetchPhoneNumbersFromMetaAsync(string wabaId, string accessToken)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/{wabaId}/phone_numbers");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch phone numbers from Meta for WABA {WabaId}. Status: {Status}", wabaId, response.StatusCode);
                return new List<WabaPhoneNumber>();
            }

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);

            var phoneNumbers = new List<WabaPhoneNumber>();
            if (doc.RootElement.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in dataProp.EnumerateArray())
                {
                    phoneNumbers.Add(new WabaPhoneNumber
                    {
                        PhoneNumber = element.TryGetProperty("display_phone_number", out var num) ? num.GetString() ?? "" : "",
                        PhoneNumberId = element.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "",
                        DisplayName = element.TryGetProperty("verified_name", out var dName) ? dName.GetString() ?? "" : "",
                        VerifiedName = element.TryGetProperty("verified_name", out var vName) ? vName.GetString() ?? "" : "",
                        Quality = (element.TryGetProperty("quality_rating", out var qual) ? qual.GetString() ?? "GREEN" : "GREEN").ToUpper(),
                        Status = element.TryGetProperty("status", out var stat) ? stat.GetString() ?? "APPROVED" : "APPROVED"
                    });
                }
            }

            return phoneNumbers;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching phone numbers from Meta Graph API for WABA {WabaId}", wabaId);
            return new List<WabaPhoneNumber>();
        }
    }

    public async Task<bool> TryTriggerTemplateBotAsync(string normalizedPhone, string incomingText, Contact contact, int? connectionId = null)
    {
        try
        {
            _logger.LogInformation("TryTriggerTemplateBotAsync called for phone {Phone}, text '{Text}', connectionId {ConnectionId}", normalizedPhone, incomingText, connectionId);

            var activeTemplateBots = await _dbContext.TemplateBots
                .Where(b => b.IsActive && (b.ConnectionId == null || b.ConnectionId == connectionId))
                .Include(b => b.Template)
                .Include(b => b.Variables)
                .ToListAsync();

            _logger.LogInformation("Found {Count} active template bots", activeTemplateBots.Count);

            var relationTypeStr = contact.Type;

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
                    _logger.LogInformation("Template Bot '{BotName}' matched keyword in message '{Text}'", bot.Name, incomingText);
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
                _logger.LogInformation("No template bot matched for phone {Phone}, text '{Text}'", normalizedPhone, incomingText);
                return false;
            }

            _logger.LogInformation("Using matched template bot '{BotName}' (Id: {BotId}), Template: '{TemplateName}'", matchedBot.Name, matchedBot.Id, matchedBot.Template?.Name ?? "null");

            // Find all variable names used in template body text
            var templateBody = matchedBot.Template?.BodyText ?? "";
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
                matchedBot.Template?.Name ?? string.Empty,
                matchedBot.Template?.Language ?? "en",
                resolvedVariables,
                connectionId);

            if (sendResult.Success)
            {
                // Sync outbound template message to ChatMessages
                string bodyText = matchedBot.Template?.BodyText ?? "";
                foreach (var v in resolvedVariables)
                {
                    bodyText = bodyText.Replace($"{{{{{v.Key}}}}}", v.Value);
                }

                var conversation = await _dbContext.ChatConversations
                    .FirstOrDefaultAsync(c => c.ContactId == contact!.Id && (connectionId == null || c.ConnectionId == connectionId));

                if (conversation == null)
                {
                    var account = connectionId.HasValue 
                        ? await _dbContext.WabaPhoneNumbers.FirstOrDefaultAsync(p => p.ConnectionId == connectionId.Value)
                        : await _dbContext.WabaPhoneNumbers.FirstOrDefaultAsync();

                    conversation = new ChatConversation
                    {
                        ContactId = contact!.Id,
                        ConnectionId = connectionId,
                        WabaPhoneNumberId = account?.Id,
                        LastMessageText = bodyText,
                        LastMessageAt = DateTime.UtcNow
                    };
                    _dbContext.ChatConversations.Add(conversation);
                    await _dbContext.SaveChangesAsync();
                }
                else
                {
                    conversation.LastMessageText = bodyText;
                    conversation.LastMessageAt = DateTime.UtcNow;
                }

                _dbContext.ChatMessages.Add(new ChatMessage
                {
                    ConversationId = conversation.Id,
                    ContactId = contact!.Id,
                    ConnectionId = connectionId,
                    WhatsAppMessageId = sendResult.MessageId,
                    Direction = ChatMessageDirection.Outgoing,
                    Status = ChatMessageStatus.Sent,
                    Text = bodyText,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
                await _dbContext.SaveChangesAsync();

                // If template bot has an attached file / PDF / document, send the attachment media message
                var fileVar = matchedBot.Variables.FirstOrDefault(v => v.VariableName.Equals("file", StringComparison.OrdinalIgnoreCase));
                if (fileVar != null && !string.IsNullOrWhiteSpace(fileVar.VariableValue))
                {
                    string rawUrl = fileVar.VariableValue.Trim();
                    string fileName = !string.IsNullOrWhiteSpace(fileVar.MergeField) 
                        ? fileVar.MergeField 
                        : rawUrl.Substring(rawUrl.LastIndexOf('/') + 1);

                    string mediaType = "document";
                    string ext = Path.GetExtension(rawUrl).ToLowerInvariant();
                    if (ext is ".jpg" or ".jpeg" or ".png" or ".webp")
                    {
                        mediaType = "image";
                    }
                    else if (ext is ".mp4" or ".3gp")
                    {
                        mediaType = "video";
                    }

                    string? resolvedPhoneNumberId = null;
                    if (connectionId.HasValue)
                    {
                        var phone = await _dbContext.WabaPhoneNumbers.FirstOrDefaultAsync(p => p.ConnectionId == connectionId.Value);
                        resolvedPhoneNumberId = phone?.PhoneNumberId;
                    }

                    var mediaSendResult = await SendMediaMessageAsync(
                        normalizedPhone,
                        rawUrl,
                        mediaType,
                        fileName,
                        null,
                        resolvedPhoneNumberId,
                        connectionId);

                    if (conversation != null)
                    {
                        conversation.LastMessageText = $"[Sent {mediaType}: {fileName}]";
                        conversation.LastMessageAt = DateTime.UtcNow;
                    }

                    _dbContext.ChatMessages.Add(new ChatMessage
                    {
                        ConversationId = conversation?.Id ?? 0,
                        ContactId = contact.Id,
                        ConnectionId = connectionId,
                        WhatsAppMessageId = mediaSendResult.Success ? mediaSendResult.MessageId : null,
                        Direction = ChatMessageDirection.Outgoing,
                        Status = mediaSendResult.Success ? ChatMessageStatus.Sent : ChatMessageStatus.Failed,
                        ErrorMessage = mediaSendResult.Success ? null : mediaSendResult.ErrorMessage,
                        Text = $"[Attachment: {fileName}]",
                        MediaUrl = rawUrl,
                        MediaType = mediaType,
                        MediaFileName = fileName,
                        CreatedAt = DateTime.UtcNow.AddMilliseconds(100),
                        UpdatedAt = DateTime.UtcNow.AddMilliseconds(100)
                    });
                    await _dbContext.SaveChangesAsync();
                }

                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing template bot trigger for phone {Phone}, connectionId {ConnectionId}", normalizedPhone, connectionId);
        }

        return false;
    }
}
