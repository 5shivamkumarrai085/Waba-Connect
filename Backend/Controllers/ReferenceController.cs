using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services;
using WhatsAppCampaignApi.Services.Catalogs;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Controllers;

/// <summary>
/// Read-only lists and limits the UI needs for pickers and form rules. Each comes from the same
/// catalogue the server validates against (Services/Catalogs), so the screen and the rule cannot
/// disagree, and nothing is typed twice.
/// </summary>
[ApiController]
[Authorize]
[Route("api/reference")]
public class ReferenceController : ControllerBase
{
    private readonly IOmniSettingsService _settings;
    private readonly IConfiguration _configuration;

    public ReferenceController(IOmniSettingsService settings, IConfiguration configuration)
    {
        _settings = settings;
        _configuration = configuration;
    }

    private ActionResult<ApiResponse<object>> Data(object value) => Ok(new ApiResponse<object> { Success = true, Data = value });

    /// <summary>The time zones this server can resolve, as IANA ids with their current offset.</summary>
    [HttpGet("time-zones")]
    [ResponseCache(Duration = 3600)]
    public ActionResult<ApiResponse<IEnumerable<object>>> TimeZones() =>
        Ok(new ApiResponse<IEnumerable<object>>
        {
            Success = true,
            Data = OmniSettingsCatalog.TimeZoneCatalogue.Select(z => new { value = z.Value, label = z.Label })
        });

    /// <summary>
    /// Older IANA names mapped to current ones (browsers still report e.g. "Asia/Calcutta"), so a
    /// client can default a picker to the viewer's own zone.
    /// </summary>
    [HttpGet("time-zone-aliases")]
    [ResponseCache(Duration = 3600)]
    public ActionResult<ApiResponse<IReadOnlyDictionary<string, string>>> TimeZoneAliases() =>
        Ok(new ApiResponse<IReadOnlyDictionary<string, string>> { Success = true, Data = OmniSettingsCatalog.IanaAliases });

    /// <summary>The consent topics configured under Settings › Compliance ("marketing" by default).</summary>
    [HttpGet("consent-topics")]
    public async Task<ActionResult<ApiResponse<IEnumerable<string>>>> ConsentTopicList()
    {
        var configured = await _settings.GetListAsync("compliance.topics");
        var topics = (configured.Count > 0 ? configured : [ConsentTopics.Marketing])
            .Select(ConsentTopics.Normalize)
            .Where(t => t != ConsentTopics.All)
            .Distinct();
        return Ok(new ApiResponse<IEnumerable<string>> { Success = true, Data = topics });
    }

    /// <summary>A/B metrics, follow-up conditions and actions, and their limits, for one channel.</summary>
    // Catalogue data changes only with a deploy: the browser may reuse it for ten minutes.
    [ResponseCache(Duration = 600, Location = ResponseCacheLocation.Client)]
    [HttpGet("campaign-options")]
    public ActionResult<ApiResponse<object>> CampaignOptions([FromQuery] MessageChannel channel = MessageChannel.WhatsApp) => Data(new
    {
        abMetrics = CampaignFeatureCatalog.AbMetrics(channel),
        maxAbVariants = CampaignFeatureCatalog.MaxExtraVariants + 1,
        abTestPercent = CampaignFeatureCatalog.AbTestPercent,
        abDecideAfterHours = CampaignFeatureCatalog.AbDecideAfterHours,
        abNoSignalGraceHours = CampaignFeatureCatalog.AbNoSignalGraceHours,
        abDecisionReasons = CampaignFeatureCatalog.AbDecisionReasons,
        followUpConditions = CampaignFeatureCatalog.FollowUpConditions(channel),
        followUpActions = CampaignFeatureCatalog.FollowUpActions,
        maxFollowUps = CampaignFeatureCatalog.MaxFollowUps,
        followUpDelayHours = CampaignFeatureCatalog.FollowUpDelayHours,
        maxRetryRuns = Math.Max(1, _configuration.GetValue("Campaigns:Retry:MaxRuns", 3)),
        maxProofAddresses = CampaignFeatureCatalog.MaxProofAddresses
    });

    // Catalogue data changes only with a deploy: the browser may reuse it for ten minutes.
    [ResponseCache(Duration = 600, Location = ResponseCacheLocation.Client)]
    [HttpGet("report-schedule-options")]
    public ActionResult<ApiResponse<object>> ReportScheduleOptions() => Data(new
    {
        frequencies = ReportScheduleCatalog.Frequencies,
        formats = ReportScheduleCatalog.Formats,
        lookbackDays = ReportScheduleCatalog.LookbackDays,
        maxRecipients = ReportScheduleCatalog.MaxRecipients,
        maxDayOfMonth = ReportScheduleCatalog.MaxDayOfMonth,
        defaultTimeOfDay = ReportScheduleCatalog.DefaultTimeOfDay,
        defaultDayOfWeek = ReportScheduleCatalog.DefaultDayOfWeek
    });

    // Catalogue data changes only with a deploy: the browser may reuse it for ten minutes.
    [ResponseCache(Duration = 600, Location = ResponseCacheLocation.Client)]
    [HttpGet("template-options")]
    public ActionResult<ApiResponse<object>> TemplateOptions() => Data(new
    {
        categories = TemplateAuthoringCatalog.Categories,
        buttonTypes = TemplateAuthoringCatalog.ButtonTypes,
        maxButtons = TemplateAuthoringCatalog.MaxButtons,
        maxButtonLabelLength = TemplateAuthoringCatalog.MaxButtonLabelLength,
        maxBodyLength = TemplateAuthoringCatalog.MaxBodyLength,
        maxHeaderLength = TemplateAuthoringCatalog.MaxHeaderLength,
        maxFooterLength = TemplateAuthoringCatalog.MaxFooterLength,
        maxNameLength = TemplateAuthoringCatalog.MaxNameLength,
        namePattern = TemplateAuthoringCatalog.NamePattern
    });

    // Catalogue data changes only with a deploy: the browser may reuse it for ten minutes.
    [ResponseCache(Duration = 600, Location = ResponseCacheLocation.Client)]
    [HttpGet("chat-options")]
    public ActionResult<ApiResponse<object>> ChatOptions() => Data(new
    {
        conversationStatuses = ChatCatalog.ConversationStatuses,
        stateFilters = ChatCatalog.StateFilters,
        readFilters = ChatCatalog.ReadFilters,
        quickViews = ChatCatalog.QuickViews,
        sortOrders = ChatCatalog.SortOrders,
        assigneeFilters = ChatCatalog.AssigneeFilters,
        maxReplyButtons = ChatCatalog.MaxReplyButtons,
        maxReplyButtonLength = ChatCatalog.MaxReplyButtonLength,
        maxInteractiveBodyLength = ChatCatalog.MaxInteractiveBodyLength
    });

    // Catalogue data changes only with a deploy: the browser may reuse it for ten minutes.
    [ResponseCache(Duration = 600, Location = ResponseCacheLocation.Client)]
    [HttpGet("webhook-options")]
    public ActionResult<ApiResponse<object>> WebhookOptions() => Data(new
    {
        eventTypes = Services.Integrations.WebhookEventCatalog.All,
        deliveryStatuses = WebhookCatalog.DeliveryStatuses
    });

    // Catalogue data changes only with a deploy: the browser may reuse it for ten minutes.
    [ResponseCache(Duration = 600, Location = ResponseCacheLocation.Client)]
    [HttpGet("consent-options")]
    public ActionResult<ApiResponse<object>> ConsentOptions() => Data(new
    {
        channels = ConsentCatalog.Channels,
        statuses = ConsentCatalog.Statuses,
        sources = ConsentCatalog.SourceLabels
    });

    /// <summary>What a segment rule can test, which operators each kind of field takes, and the day range.</summary>
    // Catalogue data changes only with a deploy: the browser may reuse it for ten minutes.
    [ResponseCache(Duration = 600, Location = ResponseCacheLocation.Client)]
    [HttpGet("segment-fields")]
    public ActionResult<ApiResponse<object>> SegmentFields() => Data(new
    {
        fields = SegmentFieldCatalog.Fields,
        operators = SegmentFieldCatalog.Operators,
        days = SegmentFieldCatalog.Days,
        ages = ContactFieldCatalog.AgeYears
    });

    /// <summary>
    /// Security modes, well-known ports (and the security each implies), the send-rate range and
    /// the defaults this deployment uses (default send rate, the IMAP port assumed when replies are
    /// read through a mailbox derived from the SMTP host).
    /// </summary>
    // Catalogue data changes only with a deploy: the browser may reuse it for ten minutes.
    [ResponseCache(Duration = 600, Location = ResponseCacheLocation.Client)]
    [HttpGet("email-options")]
    public ActionResult<ApiResponse<object>> EmailOptions([FromServices] Microsoft.Extensions.Options.IOptionsMonitor<Models.Options.EmailOptions> email) => Data(new
    {
        securityModes = EmailConnectionCatalog.SecurityModes,
        smtpPorts = EmailConnectionCatalog.SmtpPorts,
        imapPorts = EmailConnectionCatalog.ImapPorts,
        sendRate = new
        {
            min = EmailConnectionCatalog.MinSendRatePerSecond,
            max = EmailConnectionCatalog.MaxSendRatePerSecond,
            @default = email.CurrentValue.Dispatch.DefaultSendRatePerSecond
        },
        defaultImapPort = email.CurrentValue.Inbound.DefaultImapPort,
        deriveImapFromSmtp = email.CurrentValue.Inbound.DeriveImapFromSmtp
    });

    /// <summary>
    /// Every contact field with its kind and length, which ones are required right now (the
    /// always-required ones plus the administrator's choice), the accepted age range and the
    /// phone format. The contact form reads this; the server validates against the same values.
    /// </summary>
    [HttpGet("contact-fields")]
    public async Task<ActionResult<ApiResponse<object>>> ContactFields()
    {
        var stored = await _settings.GetValueAsync(ContactFieldCatalog.RequiredFieldsSettingKey);
        var required = ContactFieldCatalog.Resolve(ContactFieldCatalog.ParseStored(stored));
        return Data(new
        {
            fields = ContactFieldCatalog.Fields.Select(f => new
            {
                f.Key, f.Label, f.Kind, f.MaxLength, f.AlwaysRequired,
                Required = required.Contains(f.Key)
            }),
            ages = ContactFieldCatalog.AgeYears,
            nameMinLength = ContactFieldCatalog.NameMinLength,
            phonePattern = ContactFieldCatalog.PhonePattern
        });
    }
}
