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

    /// <summary>Recipients the provider never accepted (send error, missing address, render failure).</summary>
    public int FailedCount { get; set; }

    /// <summary>Recipients whose message was accepted and later bounced permanently. A subset of
    /// <see cref="SentCount"/>; delivered = sent − bounced for providers that report no delivery.</summary>
    public int BouncedCount { get; set; }

    /// <summary>Recipients skipped because the address is on the suppression list.</summary>
    public int SuppressedCount { get; set; }

    /// <summary>Recipients excluded by a compliance rule (consent, opt-out, frequency cap).</summary>
    public int SkippedCount { get; set; }

    /// <summary>Consent topic this campaign is sent under; null means "marketing".</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(64)]
    public string? Topic { get; set; }

    /// <summary>Service messages (statements, alerts): exempt from quiet hours and the frequency cap.</summary>
    public bool IsTransactional { get; set; }

    /// <summary>For RecipientLocalTime: the wall-clock date and time to deliver in each recipient's zone.</summary>
    public DateTime? LocalSendAt { get; set; }

    /// <summary>
    /// The audience is one or more segments and nothing else, so recipients who leave a segment
    /// before the send are dropped. With explicit contacts or groups as well, nobody is dropped.
    /// </summary>
    public bool AudienceIsSegmentsOnly { get; set; }

    public ICollection<CampaignSegment> Segments { get; set; } = new List<CampaignSegment>();

    // ── A/B testing ─────────────────────────────────────────────────────────────────────────

    public ICollection<CampaignVariant> Variants { get; set; } = new List<CampaignVariant>();

    /// <summary>For a follow-up campaign: the campaign whose recipients it follows up.</summary>
    public int? ParentCampaignId { get; set; }

    /// <summary>Share of the audience (1–100 %) that receives a variant first; the rest wait for the winner.</summary>
    public int? AbTestPercent { get; set; }

    /// <summary>What decides the winner: open, click or reply (email); read or reply (WhatsApp).</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(20)]
    public string? AbWinnerMetric { get; set; }

    /// <summary>
    /// When the winner is picked automatically. Provisional until the first test message goes out;
    /// then the winner worker re-anchors it to that send plus <see cref="AbDecideAfterHours"/>.
    /// </summary>
    public DateTime? AbDecideAt { get; set; }

    /// <summary>The test window, measured from the first test send.</summary>
    public int? AbDecideAfterHours { get; set; }

    public const int PausedReasonMaxLength = 500;

    /// <summary>
    /// Why the system put the campaign on hold (for example, the connection's stored password
    /// cannot be read). Null for a pause an operator chose. Cleared on resume.
    /// </summary>
    [System.ComponentModel.DataAnnotations.MaxLength(PausedReasonMaxLength)]
    public string? PausedReason { get; set; }

    public int? AbWinnerVariantId { get; set; }
    public DateTime? AbDecidedAt { get; set; }

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
