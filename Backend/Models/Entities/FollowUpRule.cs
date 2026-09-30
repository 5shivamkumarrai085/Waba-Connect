using System.ComponentModel.DataAnnotations;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// "N hours after this campaign, for recipients who did (or did not) do X, send Y" — optionally on
/// the other channel. When due, the matching recipients become a child campaign that goes through
/// the full pipeline: approval, consent, frequency cap, quiet hours.
/// </summary>
public class FollowUpRule
{
    public int Id { get; set; }

    public int CampaignId { get; set; }
    public Campaign Campaign { get; set; } = null!;

    /// <summary>NotOpened, NotClicked, Clicked, Replied, NotReplied, NotRead, Failed.</summary>
    [Required, MaxLength(20)]
    public string Condition { get; set; } = "NotOpened";

    public int DelayHours { get; set; }

    /// <summary>send (a template) or tag (add a tag to the matching contacts).</summary>
    [Required, MaxLength(10)]
    public string Action { get; set; } = "send";

    public MessageChannel Channel { get; set; }
    public int? TemplateId { get; set; }
    public int? EmailTemplateId { get; set; }
    public int? SenderIdentityId { get; set; }
    public int? ConnectionId { get; set; }

    [MaxLength(998)]
    public string? SubjectOverride { get; set; }

    [MaxLength(100)]
    public string? Tag { get; set; }

    public DateTime DueAt { get; set; }

    /// <summary>Pending, Running, Done, Failed.</summary>
    [Required, MaxLength(12)]
    public string Status { get; set; } = "Pending";

    public int? ChildCampaignId { get; set; }
    public int? MatchedCount { get; set; }

    [MaxLength(1000)]
    public string? Note { get; set; }

    public DateTime? ExecutedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
