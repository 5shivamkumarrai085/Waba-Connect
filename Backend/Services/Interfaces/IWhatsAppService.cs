using WhatsAppCampaignApi.Models.DTOs.Webhook;
using WhatsAppCampaignApi.Models.Entities;

namespace WhatsAppCampaignApi.Services.Interfaces;

/// <summary>
/// Service for interacting with the WhatsApp Business Cloud API.
/// </summary>
public interface IWhatsAppService
{
    /// <summary>
    /// Sends a template message to a single recipient via WhatsApp Cloud API.
    /// </summary>
    /// <param name="recipientPhone">Phone number in E.164 format (e.g., +919499373415)</param>
    /// <param name="templateName">The approved template name</param>
    /// <param name="languageCode">Language code (e.g., "en")</param>
    /// <param name="variables">Template variable values keyed by position (e.g., "1" => "John")</param>
    /// <returns>WhatsApp message ID (wamid.xxx) on success, null on failure</returns>
    Task<string?> SendTemplateMessageAsync(string recipientPhone, string templateName, string languageCode, Dictionary<string, string>? variables = null, int? connectionId = null);

    /// <summary>
    /// Sends a template message and returns the exact Meta send result, including rejection details.
    /// </summary>
    Task<WhatsAppSendResult> SendTemplateMessageWithResultAsync(string recipientPhone, string templateName, string languageCode, Dictionary<string, string>? variables = null, int? connectionId = null);

    /// <summary>
    /// Sends a free-form text message to a single recipient via WhatsApp Cloud API.
    /// This works only when Meta allows a customer-service conversation window for the recipient.
    /// </summary>
    Task<WhatsAppSendResult> SendTextMessageAsync(string recipientPhone, string text, string? fromPhoneNumberId = null, int? connectionId = null);

    /// <summary>Submits a template to Meta for review, on the given connection's WhatsApp Business Account.</summary>
    Task<(bool Success, string? TemplateId, string? Status, string? Error)> SubmitTemplateAsync(int connectionId, object submission);

    /// <summary>
    /// Sends an outbound media message (image, video, document) to a recipient via WhatsApp Cloud API.
    /// </summary>
    Task<WhatsAppSendResult> SendMediaMessageAsync(
        string recipientPhone,
        string mediaUrl,
        string mediaType,
        string? filename = null,
        string? caption = null,
        string? fromPhoneNumberId = null,
        int? connectionId = null);

    /// <summary>
    /// Fetches all templates from the WhatsApp Business Account.
    /// </summary>
    Task<List<WhatsAppTemplateInfo>> GetTemplatesAsync();

    /// <summary>
    /// Fetches all templates for a specific WABA Connection from Meta Graph API.
    /// </summary>
    Task<List<WhatsAppTemplateInfo>> GetTemplatesForConnectionAsync(int connectionId);

    /// <summary>
    /// Verifies the webhook callback from Meta.
    /// </summary>
    bool VerifyWebhook(string mode, string token, string challenge);

    /// <summary>
    /// Sends a custom JSON payload to WhatsApp Cloud API.
    /// </summary>
    Task<WhatsAppSendResult> SendCustomPayloadAsync(string recipientPhone, object payload, string? fromPhoneNumberId = null, int? connectionId = null);

    /// <summary>
    /// Processes an incoming webhook payload for delivery status updates.
    /// </summary>
    Task ProcessWebhookAsync(WhatsAppWebhookPayload payload);

    /// <summary>
    /// Attempts to match and trigger template bots for incoming message keyword.
    /// </summary>
    Task<bool> TryTriggerTemplateBotAsync(string normalizedPhone, string incomingText, Contact contact, int? connectionId = null);
}

/// <summary>
/// Represents template information fetched from WhatsApp Cloud API.
/// </summary>
public class WhatsAppTemplateInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? BodyText { get; set; }
    public string? HeaderContent { get; set; }
    public string? FooterText { get; set; }
    public string? RejectReason { get; set; }
    public string? TemplateType { get; set; }
    public string? ComponentsJson { get; set; }
    public string? ButtonsJson { get; set; }
}

public class WhatsAppSendResult
{
    public bool Success { get; set; }
    public string? MessageId { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// HTTP status Meta returned. Surfaced for the activity log's "Response Code" column —
    /// previously this was computed only to build a log message and then discarded.
    /// </summary>
    public int? HttpStatusCode { get; set; }

    // The single-argument factories are kept so the ~20 existing call sites compile unchanged.
    public static WhatsAppSendResult Sent(string? messageId) => new()
    {
        Success = true,
        MessageId = messageId
    };

    public static WhatsAppSendResult Sent(string? messageId, int? httpStatusCode) => new()
    {
        Success = true,
        MessageId = messageId,
        HttpStatusCode = httpStatusCode
    };

    public static WhatsAppSendResult Failed(string errorMessage) => new()
    {
        Success = false,
        ErrorMessage = errorMessage
    };

    public static WhatsAppSendResult Failed(string errorMessage, int? httpStatusCode) => new()
    {
        Success = false,
        ErrorMessage = errorMessage,
        HttpStatusCode = httpStatusCode
    };
}
