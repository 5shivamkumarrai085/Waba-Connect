using System.ComponentModel.DataAnnotations;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Models.Entities;

public class Campaign
{
    public int Id { get; set; }
    
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Which channel this campaign sends on. Defaults to WhatsApp so every existing row and
    /// every existing create path keeps its current meaning without being touched.
    /// </summary>
    public MessageChannel Channel { get; set; } = MessageChannel.WhatsApp;

    // Nullable as of the email channel: a WhatsApp campaign points at a Meta-approved Template,
    // an email campaign points at an EmailTemplate, and exactly one of the two is set. Making
    // this nullable turns the generated join from INNER to LEFT, which changes nothing for
    // existing rows because all of them have a template. The C# dereferences of Template in
    // CampaignService are null-guarded accordingly.
    public int? TemplateId { get; set; }
    public Template? Template { get; set; }

    /// <summary>
    /// The email template this campaign renders, when <see cref="Channel"/> is Email. Points at
    /// the same EmailTemplates the Setup section manages — there is deliberately no separate
    /// campaign-template store.
    /// </summary>
    public int? EmailTemplateId { get; set; }
    public EmailTemplate? EmailTemplate { get; set; }

    public int? ConnectionId { get; set; }
    public virtual Connection? Connection { get; set; }

    /// <summary>Channel-specific settings, present only for email campaigns.</summary>
    public EmailCampaignDetail? EmailDetail { get; set; }
    
    // Comma-separated list of ContactType names (e.g. "Lead,Customer"), not a single
    // enum — a campaign can now target multiple relation types at once. Still a plain
    // VARCHAR(50) column (HasConversion<string>() was removed from the enum mapping in
    // AppDbContext.cs since this is no longer an enum-typed property), so no migration
    // was needed for this change.
    public string RelationType { get; set; } = string.Empty;
    public ScheduleType ScheduleType { get; set; }
    public DateTime? ScheduledAt { get; set; }
    
    public CampaignStatus Status { get; set; } = CampaignStatus.Draft;
    
    public int TotalRecipients { get; set; }

    // ── Delivery counters ─────────────────────────────────────────────────────────────────────
    // Incremented atomically (UPDATE SET count = count + 1) by the event processor after each
    // event, guarded by idempotency. Never recomputed with a full GROUP BY on every event —
    // that approach was an O(N) query per event at scale.
    public int SentCount { get; set; }
    public int DeliveredCount { get; set; }
    public int ReadCount { get; set; }
    public int FailedCount { get; set; }

    // ── Engagement counters ───────────────────────────────────────────────────────────────────
    // OPENED = tracking pixel loaded (engagement, not guaranteed delivery).
    // These are absent from MessageStatus deliberately: engagement is not a delivery state and
    // must not retroactively change a recipient's status (e.g. an Open must not overwrite Bounced).
    public int OpenedCount { get; set; }
    public int ClickedCount { get; set; }
    public int RepliedCount { get; set; }
    public int UnsubscribedCount { get; set; }
    public int ComplainedCount { get; set; }
    
    [MaxLength(100)]
    public string? CreatedBy { get; set; }
    
    public string? FileName { get; set; }
    public string? FileType { get; set; }
    public string? FileUrl { get; set; }
    
    public bool IsDeleted { get; set; } = false;
    public bool IsBulkCampaign { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    
    [MaxLength(100)]
    public string? DeletedBy { get; set; }
    
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<CampaignContact> CampaignContacts { get; set; } = [];
    public ICollection<CampaignVariable> Variables { get; set; } = [];
}
