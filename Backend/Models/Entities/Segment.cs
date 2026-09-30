using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// A saved, rule-based audience ("opened a campaign in the last 30 days and lives in Mumbai").
/// Membership is computed from the rules whenever it is used, so it stays current by itself.
/// </summary>
public class Segment
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>The rules as JSON (see SegmentRuleGroup).</summary>
    [Required]
    public string RulesJson { get; set; } = "{}";

    /// <summary>Member count at <see cref="CountedAt"/>: a hint for lists, never used to send.</summary>
    public int? CachedCount { get; set; }
    public DateTime? CountedAt { get; set; }

    public int? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>A campaign targeting a segment. The segment is re-resolved when the campaign starts sending.</summary>
public class CampaignSegment
{
    public int CampaignId { get; set; }
    public Campaign Campaign { get; set; } = null!;

    public int SegmentId { get; set; }
    public Segment Segment { get; set; } = null!;
}
