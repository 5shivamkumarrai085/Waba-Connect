using WhatsAppCampaignApi.Helpers;
namespace WhatsAppCampaignApi.Models.DTOs.Chat;

public class ChatAccountResponse
{
    public int Id { get; set; }
    public int? ConnectionId { get; set; }
    public string? ConnectionName { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string PhoneNumberId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string VerifiedName { get; set; } = string.Empty;
    public string Quality { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class ChatConversationResponse
{
    public int Id { get; set; }
    public int ContactId { get; set; }
    public int? ConnectionId { get; set; }
    public string? ConnectionName { get; set; }

    /// <summary>
    /// "WhatsApp" or "Email". Always populated — pre-existing threads read as WhatsApp — so the
    /// inbox can show a per-row channel icon and filter without a second lookup.
    /// </summary>
    public string Channel { get; set; } = "WhatsApp";
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;

    /// <summary>
    /// The contact's email address, when they have one.
    ///
    /// How an email thread identifies its correspondent — the phone number above is meaningless
    /// on that channel, and showing it was how an email conversation ended up labelled with a
    /// WhatsApp number.
    /// </summary>
    public string? Email { get; set; }
    public string LastMessage { get; set; } = string.Empty;
    public int UnreadCount { get; set; }
    public string LastMessageTime { get; set; } = string.Empty;
    public DateTime? LastMessageAt { get; set; }
    public string? AvatarUrl { get; set; }
    public string? FromPhoneNumber { get; set; }
    public string? FromPhoneNumberId { get; set; }
    public string? AssignedTo { get; set; }
    public string? Source { get; set; }
    public DateTime ContactCreatedAt { get; set; }
    public List<string> ContactGroups { get; set; } = [];
    public bool ContactIsActive { get; set; }
}

public class ChatMessageResponse
{
    public int Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsTemplate { get; set; }
    public string? ErrorMessage { get; set; }
    public string? MediaUrl { get; set; }
    public string? MediaType { get; set; }
    public string? MediaFileName { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public int? CampaignId { get; set; }
    public string? WhatsAppMessageId { get; set; }

    /// <summary>"WhatsApp" or "Email". Lets the thread pane pick how to render this message.</summary>
    public string Channel { get; set; } = "WhatsApp";

    // ── Email only ───────────────────────────────────────────────────────────────────────────
    // Null on WhatsApp. An email is not a bubble: it has a subject, named correspondents and an
    // HTML body, and rendering one as a chat line loses the parts that identify it.

    public string? Subject { get; set; }
    public string? FromAddress { get; set; }
    public string? FromName { get; set; }
    public string? ToAddresses { get; set; }
    public string? CcAddresses { get; set; }

    /// <summary>The body as sent. Sanitised on the way in, and rendered in an isolated frame.</summary>
    public string? HtmlBody { get; set; }

    public bool HasAttachments { get; set; }
}

public class SendChatMessageRequest
{
    public string Text { get; set; } = string.Empty;
    public string? FromPhoneNumberId { get; set; }
    public string? MediaUrl { get; set; }
    public string? MediaType { get; set; }
    public string? MediaFileName { get; set; }
    public int? ConnectionId { get; set; }
}

public class SendTemplateToContactRequest
{
    public int ContactId { get; set; }
    public int TemplateId { get; set; }
    public int? ConnectionId { get; set; }
    public Dictionary<string, string>? Variables { get; set; }
}

/// <summary>
/// What the inbox composer posts to send one email from a thread.
/// </summary>
/// <remarks>
/// Recipients arrive as lists rather than a comma-separated string: the composer already holds
/// them as chips, and splitting a string server-side would have to re-guess where a display name
/// containing a comma ends.
/// </remarks>
public class SendEmailReplyRequest
{
    public string? Subject { get; set; }

    /// <summary>
    /// The body as HTML. Sanitised server-side before it is sent — it is operator input on its
    /// way into other people's mail clients.
    /// </summary>
    [SkipSanitization]
    public string? BodyHtml { get; set; }

    public List<string>? To { get; set; }
    public List<string>? Cc { get; set; }
    public List<string>? Bcc { get; set; }

    /// <summary>The message being replied to, so the reply threads under it.</summary>
    public int? InReplyToMessageId { get; set; }

    /// <summary>
    /// File attachments encoded as base64. Each item carries the file name, MIME type and data.
    /// </summary>
    public List<EmailAttachmentDto>? Attachments { get; set; }
}

/// <summary>One file attachment from the composer, base64-encoded.</summary>
public class EmailAttachmentDto
{
    /// <summary>Original file name shown to the recipient.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>MIME type, e.g. "application/pdf" or "image/png".</summary>
    public string ContentType { get; set; } = "application/octet-stream";

    /// <summary>Raw file bytes as a base64 string.</summary>
    public string Base64Data { get; set; } = string.Empty;
}
