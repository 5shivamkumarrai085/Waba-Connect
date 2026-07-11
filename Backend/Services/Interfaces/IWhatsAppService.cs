using WhatsAppCampaignApi.Models.DTOs.Webhook;

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
    Task<string?> SendTemplateMessageAsync(string recipientPhone, string templateName, string languageCode, Dictionary<string, string>? variables = null);

    /// <summary>
    /// Sends a template message and returns the exact Meta send result, including rejection details.
    /// </summary>
    Task<WhatsAppSendResult> SendTemplateMessageWithResultAsync(string recipientPhone, string templateName, string languageCode, Dictionary<string, string>? variables = null);

    /// <summary>
    /// Sends a free-form text message to a single recipient via WhatsApp Cloud API.
    /// This works only when Meta allows a customer-service conversation window for the recipient.
    /// </summary>
    Task<WhatsAppSendResult> SendTextMessageAsync(string recipientPhone, string text, string? fromPhoneNumberId = null);

    /// <summary>
    /// Fetches all templates from the WhatsApp Business Account.
    /// </summary>
    Task<List<WhatsAppTemplateInfo>> GetTemplatesAsync();

    /// <summary>
    /// Verifies the webhook callback from Meta.
    /// </summary>
    bool VerifyWebhook(string mode, string token, string challenge);

    /// <summary>
    /// Processes an incoming webhook payload for delivery status updates.
    /// </summary>
    Task ProcessWebhookAsync(WhatsAppWebhookPayload payload);
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
}

public class WhatsAppSendResult
{
    public bool Success { get; set; }
    public string? MessageId { get; set; }
    public string? ErrorMessage { get; set; }

    public static WhatsAppSendResult Sent(string? messageId) => new()
    {
        Success = true,
        MessageId = messageId
    };

    public static WhatsAppSendResult Failed(string errorMessage) => new()
    {
        Success = false,
        ErrorMessage = errorMessage
    };
}
