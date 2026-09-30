using WhatsAppCampaignApi.Models.DTOs.Common;

namespace WhatsAppCampaignApi.Models.DTOs.Campaigns;

public class CreateCampaignRequest
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The WhatsApp template. Required for the WhatsApp channel and ignored for email, which uses
    /// <see cref="EmailTemplateId"/> instead. Kept non-nullable so every existing caller — the
    /// campaign wizard, the bulk CSV flow and the API clients — is unaffected.
    /// </summary>
    public int TemplateId { get; set; }

    /// <summary>
    /// "WhatsApp" or "Email". Defaults to WhatsApp when omitted, so a request written before the
    /// email channel existed still means exactly what it meant before.
    /// </summary>
    public string Channel { get; set; } = "WhatsApp";

    /// <summary>Required for the email channel: the template from the Setup section to render.</summary>
    public int? EmailTemplateId { get; set; }

    /// <summary>Required for the email channel: which verified sender to send as.</summary>
    public int? SenderIdentityId { get; set; }

    /// <summary>Overrides the template's subject for this campaign only.</summary>
    public string? SubjectOverride { get; set; }

    /// <summary>Overrides the sender identity's reply-to for this campaign only.</summary>
    public string? ReplyToOverride { get; set; }

    /// <summary>
    /// Email attachments. Several are allowed, unlike the single WhatsApp media header, which is
    /// why they do not go through the existing "file" campaign variable.
    /// </summary>
    public List<CampaignAttachmentRequest>? Attachments { get; set; }

    public bool TrackOpens { get; set; } = true;

    public bool TrackClicks { get; set; } = true;

    public string RelationType { get; set; } = string.Empty;
    public string ScheduleType { get; set; } = "Immediate";

    /// <summary>
    /// For ScheduleType "RecipientLocalTime": the date and time to deliver in each recipient's own
    /// time zone, as a wall-clock value (no offset), e.g. "2026-10-03T09:30:00".
    /// </summary>
    public DateTime? LocalSendAt { get; set; }

    /// <summary>Consent topic the campaign is sent under ("marketing" when empty).</summary>
    public string? Topic { get; set; }

    /// <summary>Service messages: exempt from quiet hours and the frequency cap (never from opt-outs).</summary>
    public bool IsTransactional { get; set; }

    /// <summary>
    /// Send even though the pre-flight check failed. Administrators only; recorded in the audit log.
    /// </summary>
    public bool OverridePrecheck { get; set; }

    /// <summary>Dynamic segments: resolved now for the count, and again when the campaign starts sending.</summary>
    public List<int>? SegmentIds { get; set; }

    /// <summary>A/B test settings; null for an ordinary campaign.</summary>
    public AbTestRequest? AbTest { get; set; }

    /// <summary>Follow-ups: sent (or tagged) automatically after a delay, to recipients matching a condition.</summary>
    public List<FollowUpRequest>? FollowUps { get; set; }

    /// <summary>
    /// Target every active contact matching the filters below (and <see cref="RelationType"/>),
    /// resolved on the server. The wizard used to download every contact to the browser and send
    /// the ids back, which cannot work for a large contact base.
    /// </summary>
    public bool SelectAllContacts { get; set; }

    /// <summary>Contact status filter for <see cref="SelectAllContacts"/>; null or "All" means any.</summary>
    public string? ContactStatus { get; set; }

    /// <summary>Contact source filter for <see cref="SelectAllContacts"/>; null or "All" means any.</summary>
    public string? ContactSource { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public List<int>? ContactIds { get; set; }
    public List<int>? GroupIds { get; set; }
    public List<CampaignVariableRequest>? Variables { get; set; }
    public int? ConnectionId { get; set; }
}

/// <param name="Url">Where the file already lives, from the existing upload endpoint.</param>
/// <param name="FileName">Name shown to the recipient.</param>
/// <param name="ContentType">MIME type. A wrong one is what makes an attachment unopenable.</param>
/// <param name="SizeBytes">Used to reject a message that would exceed the provider's limit.</param>
public record CampaignAttachmentRequest(string Url, string FileName, string? ContentType, long? SizeBytes);

public class CampaignVariableRequest
{
    public string VariableName { get; set; } = string.Empty;
    public string? VariableValue { get; set; }
    public string? MergeField { get; set; }
}

public class CampaignResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string RelationType { get; set; } = string.Empty;
    public string ScheduleType { get; set; } = string.Empty;
    public DateTime? ScheduledAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public int TotalRecipients { get; set; }
    public int DeliveredCount { get; set; }
    public int ReadCount { get; set; }
    public int FailedCount { get; set; }
    public string? CreatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsBulkCampaign { get; set; }
    public int? ConnectionId { get; set; }
    public string? ConnectionName { get; set; }
    public string? ConnectionNickname { get; set; }

    /// <summary>
    /// "WhatsApp" or "Email". Always populated — existing campaigns read as WhatsApp — so the
    /// campaigns list can show a channel column without a second lookup.
    /// </summary>
    public string Channel { get; set; } = "WhatsApp";

    public int SentCount { get; set; }
    public int OpenedCount { get; set; }
    public int ClickedCount { get; set; }
    public int RepliedCount { get; set; }
    public int UnsubscribedCount { get; set; }
    public int ComplainedCount { get; set; }

    /// <summary>Recipients excluded by a compliance rule (consent, opt-out, frequency cap).</summary>
    public int SkippedCount { get; set; }
    public string? Topic { get; set; }
    public bool IsTransactional { get; set; }
    public DateTime? LocalSendAt { get; set; }

    /// <summary>Why the system put the campaign on hold; null for a pause an operator chose.</summary>
    public string? PausedReason { get; set; }

    /// <summary>Email-only counters, null for WhatsApp campaigns.</summary>
    public EmailCampaignStatsResponse? EmailStats { get; set; }
}

/// <summary>
/// Email-specific outcome counters.
/// </summary>
public class EmailCampaignStatsResponse
{
    public int Sent { get; set; }
    public int Delivered { get; set; }
    public int Bounced { get; set; }
    public int Complained { get; set; }
    public int Suppressed { get; set; }
    public int Opened { get; set; }
    public int Clicked { get; set; }
    public int Replied { get; set; }
    public int Unsubscribed { get; set; }
    public int Pending { get; set; }
    public int Failed { get; set; }
}

public class CampaignDetailResponse : CampaignResponse
{
    /// <summary>
    /// Whether the sending provider reports delivery. SMTP only tells us the next server accepted
    /// the message, so an SMTP campaign shows "Accepted" instead of a Delivered figure that would
    /// always read zero. Always true for WhatsApp.
    /// </summary>
    public bool ReportsDelivery { get; set; } = true;

    /// <summary>A first page only; use GET /Campaigns/{id}/recipients for the full, paged list.</summary>
    public List<CampaignRecipientResponse> Recipients { get; set; } = [];

    /// <summary>Maker-checker state, when the campaign has been through approval.</summary>
    public CampaignApprovalInfo? Approval { get; set; }

    /// <summary>Follow-ups of this campaign and what they did.</summary>
    public List<WhatsAppCampaignApi.Services.Campaigns.FollowUpInfo> FollowUps { get; set; } = [];

    /// <summary>For a follow-up campaign: the campaign it follows up.</summary>
    public int? ParentCampaignId { get; set; }

    /// <summary>A/B test results, when the campaign is a test.</summary>
    public WhatsAppCampaignApi.Services.Campaigns.AbTestResult? AbTest { get; set; }

    /// <summary>Recipients whose send failed, which "Retry failed" would send to again.</summary>
    public int RetryableCount { get; set; }

    /// <summary>How many times the failed recipients have been retried, and the limit.</summary>
    public int RetryRuns { get; set; }
    public int MaxRetryRuns { get; set; }
    public List<CampaignVariableResponse>? Variables { get; set; } = [];
}

public class CampaignApprovalInfo
{
    public string State { get; set; } = "Pending";
    public string? RequestedBy { get; set; }
    public DateTime RequestedAt { get; set; }
    public string? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? Reason { get; set; }

    /// <summary>Whether the current user may approve or reject: holds the permission and is not the maker.</summary>
    public bool CanDecide { get; set; }
}

/// <summary>Clicks on one link of an email campaign.</summary>
public class CampaignLinkClicks
{
    public string Url { get; set; } = string.Empty;

    /// <summary>Every recorded click (repeat clicks by the same person included).</summary>
    public int TotalClicks { get; set; }

    /// <summary>Distinct recipients who clicked it.</summary>
    public int UniqueClickers { get; set; }

    public DateTime FirstClickAt { get; set; }
    public DateTime LastClickAt { get; set; }
}

/// <summary>
/// An A/B test: the campaign's own template is variant A; <see cref="Variants"/> are B, C…
/// </summary>
public class AbTestRequest
{
    /// <summary>Share of the audience in the test (10–100). At 100 every recipient gets a variant and nobody waits.</summary>
    public int Percent { get; set; } = 20;

    /// <summary>open, click or reply (email); read or reply (WhatsApp).</summary>
    public string? Metric { get; set; }

    /// <summary>Hours after the send starts that the winner is picked (1–168).</summary>
    public int DecideAfterHours { get; set; } = 4;

    public List<AbVariantRequest> Variants { get; set; } = [];
}

public class AbVariantRequest
{
    public int? TemplateId { get; set; }
    public int? EmailTemplateId { get; set; }
    public string? SubjectOverride { get; set; }
}

public class FollowUpRequest
{
    /// <summary>NotOpened, NotClicked, Clicked, Replied, NotReplied, NotRead or Failed.</summary>
    public string Condition { get; set; } = "NotOpened";
    public int DelayHours { get; set; } = 48;

    /// <summary>send or tag.</summary>
    public string Action { get; set; } = "send";

    /// <summary>Email or WhatsApp; defaults to the campaign's own channel.</summary>
    public string? Channel { get; set; }
    public int? TemplateId { get; set; }
    public int? EmailTemplateId { get; set; }
    public int? SenderIdentityId { get; set; }
    public int? ConnectionId { get; set; }
    public string? SubjectOverride { get; set; }
    public string? Tag { get; set; }
}

public class AbDecisionRequest
{
    /// <summary>Choose this variant; null picks the best by the test's metric.</summary>
    public int? VariantId { get; set; }
}

public class CampaignRetryResponse
{
    public int CampaignId { get; set; }
    public int RecipientCount { get; set; }
    public int RetryNumber { get; set; }
    public int MaxRetries { get; set; }
}

public class CampaignApprovalDecisionRequest
{
    public string? Comment { get; set; }
}

public class CsvCampaignCreateResponse : CampaignResponse
{
    public List<CsvRowError> SkippedRows { get; set; } = new();
}

public class CampaignVariableResponse
{
    public string VariableName { get; set; } = string.Empty;
    public string? VariableValue { get; set; }
    public string? MergeField { get; set; }
}

public class CampaignRecipientResponse
{
    public int Id { get; set; }
    public int ContactId { get; set; }
    public string ContactName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? SentAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime? OpenedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public class CreateCsvCampaignRequest
{
    public string Name { get; set; } = string.Empty;
    public string CsvFileUrl { get; set; } = string.Empty;

    /// <summary>
    /// "WhatsApp" (the default when absent) or "Email".
    /// </summary>
    public string? Channel { get; set; }

    /// <summary>
    /// The WhatsApp template. Ignored on the email channel, which uses
    /// <see cref="EmailTemplateId"/> instead.
    /// </summary>
    public int TemplateId { get; set; }

    /// <summary>The Setup-section email template. Required on the email channel.</summary>
    public int? EmailTemplateId { get; set; }

    /// <summary>The verified sender to send from. Required on the email channel.</summary>
    public int? SenderIdentityId { get; set; }

    public string? SubjectOverride { get; set; }
    public string? ReplyToOverride { get; set; }
    public bool TrackOpens { get; set; } = true;
    public bool TrackClicks { get; set; } = true;
    public string RelationType { get; set; } = string.Empty;
    public string ScheduleType { get; set; } = "Immediate";
    public DateTime? ScheduledAt { get; set; }
    public List<CampaignVariableRequest>? Variables { get; set; }
    public int? ConnectionId { get; set; }
}
