using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// Email-only settings for a campaign, in a 1:1 side table rather than as columns on
/// <see cref="Campaign"/>.
///
/// <para>
/// Keeping them out of Campaign is what makes the channel dimension extensible: a future SMS or
/// Instagram channel adds its own side table instead of another block of mostly-null columns on
/// the shared entity, and every existing Campaign query stays untouched.
/// </para>
/// </summary>
public class EmailCampaignDetail
{
    /// <summary>Also the primary key — this is a 1:1 extension of the campaign row.</summary>
    public int CampaignId { get; set; }
    public Campaign Campaign { get; set; } = null!;

    public int SenderIdentityId { get; set; }
    public EmailSenderIdentity SenderIdentity { get; set; } = null!;

    /// <summary>Overrides the sender identity's reply-to for this campaign only.</summary>
    [MaxLength(255)]
    public string? ReplyToOverride { get; set; }

    /// <summary>
    /// Overrides the template's subject for this campaign only. The template stays the source of
    /// truth; this is the per-campaign exception.
    /// </summary>
    [MaxLength(300)]
    public string? SubjectOverride { get; set; }

    /// <summary>
    /// Attachment descriptors as JSON — url, file name, content type and size per entry. A
    /// campaign can carry several, unlike the single WhatsApp media header, so the existing
    /// Campaign.FileUrl/FileName/FileType trio cannot express it.
    /// </summary>
    public string? AttachmentsJson { get; set; }

    public bool TrackOpens { get; set; } = true;

    public bool TrackClicks { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
