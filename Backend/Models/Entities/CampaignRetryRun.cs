using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// One "retry failed recipients" action on a campaign: who asked, when, and how many recipients
/// were put back in the queue. Kept as history (and to cap how many times a campaign is retried).
/// </summary>
public class CampaignRetryRun
{
    public int Id { get; set; }

    public int CampaignId { get; set; }
    public Campaign Campaign { get; set; } = null!;

    /// <summary>The queue run id the retried recipients were dispatched under.</summary>
    [Required, MaxLength(64)]
    public string RunId { get; set; } = string.Empty;

    public int RecipientCount { get; set; }

    public int? RequestedByUserId { get; set; }

    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
}
