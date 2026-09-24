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

    /// <summary>Email-only counters, null for WhatsApp campaigns.</summary>
    public EmailCampaignStatsResponse? EmailStats { get; set; }
}

/// <summary>
/// Email-specific outcome counters.
///
/// <para>
/// Separate from the shared <c>DeliveredCount</c>/<c>ReadCount</c>/<c>FailedCount</c> because the
/// vocabularies genuinely differ: WhatsApp has "read", email has opens, clicks, bounces and
/// complaints, and a bounce is not the same kind of failure as a rejected send. Folding them
/// together would make both channels' numbers misleading.
/// </para>
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
    public int Pending { get; set; }
    public int Failed { get; set; }
}

public class CampaignDetailResponse : CampaignResponse
{
    public List<CampaignRecipientResponse> Recipients { get; set; } = [];
    public List<CampaignVariableResponse>? Variables { get; set; } = [];
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
    public string Message { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? SentAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? ReadAt { get; set; }
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
