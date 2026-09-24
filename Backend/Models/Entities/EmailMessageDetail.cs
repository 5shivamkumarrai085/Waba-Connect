using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// The email envelope and HTML body for one <see cref="ChatMessage"/>, in a 1:1 side table.
///
/// <para>
/// ChatMessage.Text carries the plain-text rendering the conversation list and search already
/// rely on; this row carries what only email has — a subject, cc/bcc, an HTML body and the
/// threading headers. <see cref="MessageIdHeader"/> is the Message-ID we generate ourselves when
/// sending, which is what makes inbound reply correlation deterministic rather than a best-effort
/// guess from the sender address.
/// </para>
/// </summary>
public class EmailMessageDetail
{
    /// <summary>Also the primary key — a 1:1 extension of the chat message row.</summary>
    public int ChatMessageId { get; set; }
    public ChatMessage ChatMessage { get; set; } = null!;

    [MaxLength(300)]
    public string? Subject { get; set; }

    [MaxLength(255)]
    public string? FromAddress { get; set; }

    [MaxLength(200)]
    public string? FromName { get; set; }

    /// <summary>
    /// Comma-separated addresses. A normalised recipient table would be the textbook answer, but
    /// nothing in this product queries by individual cc recipient — the inbox only ever renders
    /// the list.
    /// </summary>
    [MaxLength(2000)]
    public string? ToAddresses { get; set; }

    [MaxLength(2000)]
    public string? CcAddresses { get; set; }

    [MaxLength(2000)]
    public string? BccAddresses { get; set; }

    [MaxLength(255)]
    public string? ReplyTo { get; set; }

    /// <summary>Sanitised HTML, via SanitizationHelper.SanitizeHtml. Never stored or rendered raw.</summary>
    public string? HtmlBody { get; set; }

    /// <summary>
    /// RFC 5322 Message-ID — ours on outbound, theirs on inbound. Indexed, because the inbound
    /// path resolves a reply by looking its In-Reply-To up against this column.
    /// </summary>
    [MaxLength(500)]
    public string? MessageIdHeader { get; set; }

    [MaxLength(500)]
    public string? InReplyTo { get; set; }

    /// <summary>
    /// The full References chain, kept so replies we send stay correctly threaded in the
    /// recipient's own mail client.
    /// </summary>
    [MaxLength(4000)]
    public string? ReferencesHeader { get; set; }

    public bool HasAttachments { get; set; }

    /// <summary>Attachment descriptors as JSON, same shape as EmailCampaignDetail.</summary>
    public string? AttachmentsJson { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
