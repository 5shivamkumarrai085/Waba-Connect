using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.Campaigns;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Email;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services;

public class CampaignService : ICampaignService
{
    private readonly AppDbContext _dbContext;
    private readonly IWhatsAppService _whatsAppService;
    private readonly ILogger<CampaignService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    private readonly IAuditService _auditService;
    private readonly ICurrentUserService _currentUser;

    // Email-channel collaborators. Additive: the WhatsApp path never touches either of them, and
    // campaign creation branches on channel before reaching them.
    private readonly IEmailCampaignDispatcher _emailCampaignDispatcher;
    private readonly IEmailSenderGate _senderGate;
    private readonly WhatsApp.IWhatsAppCampaignDispatcher _whatsAppDispatcher;
    private readonly Storage.IFileStorage _fileStorage;
    private readonly Security.IAccessScope _accessScope;
    private readonly ICampaignExecutionGate _executionGate;
    private readonly IEventPublisher _eventPublisher;
    private readonly IConfiguration _configuration;
    private readonly Segments.ISegmentService _segments;
    private readonly IDeliverabilityService _deliverability;
    private readonly Campaigns.IAbTestService _abTests;
    private readonly Campaigns.IFollowUpService _followUps;
    private readonly IOmniSettingsService _settings;

    private readonly WhatsAppCampaignApi.Services.Integrations.IWebhookEmitter? _webhooks;

    public CampaignService(
        AppDbContext dbContext,
        IWhatsAppService whatsAppService,
        ILogger<CampaignService> logger,
        IServiceScopeFactory scopeFactory,
        IAuditService auditService,
        ICurrentUserService currentUser,
        IEmailCampaignDispatcher emailCampaignDispatcher,
        IEmailSenderGate senderGate,
        WhatsApp.IWhatsAppCampaignDispatcher whatsAppDispatcher,
        Storage.IFileStorage fileStorage,
        Security.IAccessScope accessScope,
        ICampaignExecutionGate executionGate,
        IEventPublisher eventPublisher,
        IConfiguration configuration,
        Segments.ISegmentService segments,
        IDeliverabilityService deliverability,
        Campaigns.IAbTestService abTests,
        Campaigns.IFollowUpService followUps,
        IOmniSettingsService settings,
        WhatsAppCampaignApi.Services.Integrations.IWebhookEmitter? webhooks = null)
    {
        _settings = settings;
        _webhooks = webhooks;
        _followUps = followUps;
        _abTests = abTests;
        _deliverability = deliverability;
        _segments = segments;
        _configuration = configuration;
        _accessScope = accessScope;
        _executionGate = executionGate;
        _eventPublisher = eventPublisher;
        _dbContext = dbContext;
        _whatsAppService = whatsAppService;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _auditService = auditService;
        _currentUser = currentUser;
        _emailCampaignDispatcher = emailCampaignDispatcher;
        _senderGate = senderGate;
        _whatsAppDispatcher = whatsAppDispatcher;
        _fileStorage = fileStorage;
    }

    /// <summary>
    /// When the pipeline starts. For a recipient-local-time send that is 14 hours before the
    /// chosen wall-clock time (UTC+14 is the earliest zone), so every recipient's job is queued
    /// before their local moment arrives; the compliance guard then times each one.
    /// </summary>
    private static DateTime? ResolveScheduledAt(CreateCampaignRequest request)
    {
        if (!request.ScheduleType.Equals(nameof(ScheduleType.RecipientLocalTime), StringComparison.OrdinalIgnoreCase))
            return request.ScheduledAt;

        var local = ResolveLocalSendAt(request)
            ?? throw new ArgumentException("Choose the local date and time to deliver at.");
        var start = DateTime.SpecifyKind(local, DateTimeKind.Utc).AddHours(-14);
        return start > DateTime.UtcNow ? start : DateTime.UtcNow;
    }

    /// <summary>The wall-clock send time, without any offset the client may have attached.</summary>
    private static DateTime? ResolveLocalSendAt(CreateCampaignRequest request) =>
        request.ScheduleType.Equals(nameof(ScheduleType.RecipientLocalTime), StringComparison.OrdinalIgnoreCase)
        && request.LocalSendAt is { } at
            ? DateTime.SpecifyKind(at, DateTimeKind.Unspecified)
            : null;

    /// <summary>
    /// Refuses a campaign whose pre-flight check has a hard failure (no subject, empty body, an
    /// unapproved template…). An administrator may override it; the override is audited.
    /// </summary>
    private async Task EnforcePrecheckAsync(CreateCampaignRequest request, MessageChannel channel)
    {
        var items = await _deliverability.PrecheckAsync(new PrecheckRequest
        {
            Channel = channel.ToString(),
            EmailTemplateId = request.EmailTemplateId,
            SenderIdentityId = request.SenderIdentityId,
            SubjectOverride = request.SubjectOverride,
            TemplateId = request.TemplateId,
            IsTransactional = request.IsTransactional,
            VariableNames = request.Variables?.Select(v => v.VariableName).ToList()
        });

        var failures = items.Where(i => i.Level == "fail").ToList();
        if (failures.Count == 0) return;

        var summary = string.Join(" ", failures.Select(f => $"{f.Title}: {f.Detail}"));
        if (!request.OverridePrecheck)
            throw new ArgumentException($"The pre-flight check failed. {summary}");

        if (!_currentUser.IsAdministrator)
            throw new Security.ForbiddenException("Only an administrator can send a campaign that failed its pre-flight check.");

        await _auditService.LogAsync("Campaign.PrecheckOverridden", "Data",
            $"Pre-flight check overridden for campaign \"{request.Name}\": {summary}", "Campaign", null);
    }

    /// <summary>A validated A/B test, ready to apply once the campaign and its recipients exist.</summary>
    private sealed record AbTestPlan(int Percent, string Metric, int DecideAfterHours, IReadOnlyList<CampaignVariant> ExtraVariants);

    /// <summary>
    /// Validates the A/B settings against the catalog without writing anything. Returns null when
    /// the request has no test.
    /// </summary>
    private async Task<AbTestPlan?> PrepareAbTestAsync(MessageChannel channel, CreateCampaignRequest request)
    {
        var ab = request.AbTest;
        if (ab is null || ab.Variants.Count == 0) return null;

        var percentRange = Catalogs.CampaignFeatureCatalog.AbTestPercent;
        var hoursRange = Catalogs.CampaignFeatureCatalog.AbDecideAfterHours;
        if (ab.Variants.Count > Catalogs.CampaignFeatureCatalog.MaxExtraVariants)
            throw new ArgumentException($"An A/B test can have at most {Catalogs.CampaignFeatureCatalog.MaxExtraVariants + 1} variants.");
        if (ab.Percent < percentRange.Min || ab.Percent > percentRange.Max)
            throw new ArgumentException($"The test share must be between {percentRange.Min}% and {percentRange.Max}%.");
        if (ab.DecideAfterHours < hoursRange.Min || ab.DecideAfterHours > hoursRange.Max)
            throw new ArgumentException($"Pick the winner between {hoursRange.Min} and {hoursRange.Max} hours after sending.");

        var isEmail = channel == MessageChannel.Email;
        var metric = (ab.Metric ?? Campaigns.AbTestService.DefaultMetric(channel)).Trim().ToLowerInvariant();
        var allowed = Catalogs.CampaignFeatureCatalog.AbMetrics(channel).Select(m => m.Value).ToArray();
        if (!allowed.Contains(metric))
            throw new ArgumentException($"For {channel} campaigns the winner is decided by {string.Join(", ", allowed)}.");

        var variants = new List<CampaignVariant>();
        for (var i = 0; i < ab.Variants.Count; i++)
        {
            var v = ab.Variants[i];
            var label = ((char)('B' + i)).ToString();
            if (isEmail)
            {
                if (v.EmailTemplateId is not { } emailTemplateId || !await _dbContext.EmailTemplates.AnyAsync(t => t.Id == emailTemplateId && t.IsEnabled))
                    throw new ArgumentException($"Variant {label} needs an enabled email template.");
            }
            else
            {
                if (v.TemplateId is not { } templateId || !await _dbContext.Templates.AnyAsync(t => t.Id == templateId && t.Status == TemplateStatus.Approved))
                    throw new ArgumentException($"Variant {label} needs an approved WhatsApp template.");
            }

            variants.Add(new CampaignVariant
            {
                Label = label,
                SortOrder = i + 1,
                TemplateId = isEmail ? null : v.TemplateId,
                EmailTemplateId = isEmail ? v.EmailTemplateId : null,
                SubjectOverride = isEmail && !string.IsNullOrWhiteSpace(v.SubjectOverride) ? v.SubjectOverride.Trim() : null
            });
        }

        return new AbTestPlan(ab.Percent, metric, ab.DecideAfterHours, variants);
    }

    /// <summary>
    /// Creates the variants (A is the campaign's own template) and splits the recipients: the
    /// test share across the variants, the rest held for the winner. The decision time is a
    /// provisional one; the winner worker moves it to "first test send + hours" once sending
    /// actually starts, so approvals and local-time scheduling no longer eat into the test window.
    /// </summary>
    private async Task ApplyAbTestAsync(Campaign campaign, AbTestPlan? plan)
    {
        if (plan is null) return;

        var variants = new List<CampaignVariant>
        {
            new() { CampaignId = campaign.Id, Label = "A", SortOrder = 0, TemplateId = campaign.TemplateId, EmailTemplateId = campaign.EmailTemplateId }
        };
        foreach (var extra in plan.ExtraVariants)
        {
            extra.CampaignId = campaign.Id;
            variants.Add(extra);
        }

        _dbContext.CampaignVariants.AddRange(variants);
        campaign.AbTestPercent = plan.Percent;
        campaign.AbWinnerMetric = plan.Metric;
        campaign.AbDecideAfterHours = plan.DecideAfterHours;
        campaign.AbDecideAt = (campaign.ScheduledAt is { } at && at > DateTime.UtcNow ? at : DateTime.UtcNow).AddHours(plan.DecideAfterHours);
        await _dbContext.SaveChangesAsync();

        await Campaigns.AbTestService.AssignAsync(_dbContext, campaign.Id, plan.Percent, variants.Select(v => v.Id).ToArray(), CancellationToken.None);
    }

    public Task<int?> DecideAbTestAsync(int id, int? variantId) => DecideAbTestInternalAsync(id, variantId);

    private async Task<int?> DecideAbTestInternalAsync(int id, int? variantId)
    {
        await EnsureCampaignVisibleAsync(id);
        var winner = await _abTests.DecideAsync(id, variantId);
        await PublishStatusAsync(id, CampaignStatus.Sending);
        return winner;
    }

    private static bool IsSegmentsOnly(CreateCampaignRequest request) =>
        request.SegmentIds is { Count: > 0 }
        && !request.SelectAllContacts
        && request.ContactIds is not { Count: > 0 }
        && request.GroupIds is not { Count: > 0 };

    /// <summary>
    /// The campaign's consent topic, which must be one of the topics configured under
    /// OmniConnect Settings › Compliance (the preference centre offers exactly those, so a campaign
    /// on any other topic could never be opted out of individually). No topic means the default.
    /// </summary>
    private async Task<string?> NormalizeTopicAsync(string? topic)
    {
        if (string.IsNullOrWhiteSpace(topic)) return null;
        var normalized = ConsentTopics.Normalize(topic);
        var configured = (await _settings.GetListAsync("compliance.topics")).Select(ConsentTopics.Normalize).ToList();
        if (configured.Count == 0) configured.Add(ConsentTopics.Marketing);
        if (!configured.Contains(normalized))
            throw new ArgumentException($"\"{topic.Trim()}\" is not a consent topic. Choose one of: {string.Join(", ", configured)}.");
        return normalized;
    }

    /// <summary>
    /// Parses the requested channel, defaulting to WhatsApp.
    ///
    /// <para>
    /// Defaulting rather than rejecting an empty value is what keeps every pre-existing caller
    /// working unchanged — the campaign wizard, the bulk CSV flow and any API client all omitted
    /// this field until the email channel existed.
    /// </para>
    /// </summary>
    /// <summary>
    /// Resolves and validates everything an email campaign needs before it can be created:
    /// the template, the sender, and the connection to send through.
    ///
    /// <para>
    /// Shared by the wizard path and the bulk-CSV path. Both have to enforce the same rules — a
    /// disabled template must not send, and an unverified sending domain must not send — and a
    /// second copy of those checks is how one path quietly ends up more permissive than the other.
    /// </para>
    /// <para>
    /// Everything is checked at creation, while the operator is looking at the screen, rather
    /// than in a background worker where the failure surfaces as silent non-delivery.
    /// </para>
    /// </summary>
    private async Task<(EmailTemplate Template, EmailSenderIdentity Sender, int ConnectionId)>
        ResolveEmailTargetsAsync(int? emailTemplateId, int? senderIdentityId, int? connectionId)
    {
        if (emailTemplateId is not { } templateId)
            throw new ArgumentException("An email template is required for an email campaign.");

        var emailTemplate = await _dbContext.EmailTemplates.FindAsync(templateId)
            ?? throw new KeyNotFoundException("Email template not found.");

        // The email equivalent of the approved-template rule: a disabled template must not be
        // sendable, or an operator turning one off would not actually stop it going out.
        if (!emailTemplate.IsEnabled)
            throw new InvalidOperationException($"Email template \"{emailTemplate.Name}\" is disabled.");

        if (senderIdentityId is not { } senderId)
            throw new ArgumentException("A sender identity is required for an email campaign.");

        var senderIdentity = await _dbContext.EmailSenderIdentities
            .Include(s => s.EmailConfiguration)
            .FirstOrDefaultAsync(s => s.Id == senderId)
            ?? throw new KeyNotFoundException("Sender identity not found.");

        // Fall back to the sender's own connection when the caller did not name one, so the UI
        // does not have to send the same fact twice.
        var resolvedConnectionId = connectionId ?? senderIdentity.EmailConfiguration?.ConnectionId;

        if (resolvedConnectionId is null)
            throw new ArgumentException("The selected sender is not linked to an email connection.");

        var (canSend, reason) = await _senderGate.CanSenderSendAsync(senderId);
        if (!canSend)
            throw new InvalidOperationException(reason ?? "The selected sender cannot send yet.");

        return (emailTemplate, senderIdentity, resolvedConnectionId.Value);
    }

    private static MessageChannel ParseChannel(string? channel) =>
        string.IsNullOrWhiteSpace(channel)
            ? MessageChannel.WhatsApp
            : Enum.TryParse<MessageChannel>(channel, true, out var parsed)
                ? parsed
                : throw new ArgumentException(
                    $"Unknown channel '{channel}'. Valid values: {string.Join(", ", Enum.GetNames<MessageChannel>())}.");

    /// <summary>
    /// Limits a campaign query to the connections this caller may use (connection scoping). A
    /// campaign with no connection is visible only to unrestricted callers.
    /// </summary>
    private async Task<IQueryable<Campaign>> ScopedAsync(IQueryable<Campaign> query)
    {
        if (await _accessScope.GetAllowedConnectionIdsAsync() is not { } allowed) return query;
        var ids = allowed.ToArray();
        return query.Where(c => c.ConnectionId != null && ids.Contains(c.ConnectionId.Value));
    }

    /// <summary>404 unless this caller may see the campaign — the same answer as for one that does not exist.</summary>
    private async Task EnsureCampaignVisibleAsync(int id)
    {
        if (await _accessScope.GetAllowedConnectionIdsAsync() is null) return;

        var visible = await (await ScopedAsync(_dbContext.Campaigns.IgnoreQueryFilters().AsNoTracking()))
            .AnyAsync(c => c.Id == id);
        if (!visible) throw new KeyNotFoundException("Campaign not found.");
    }

    public async Task<PagedResponse<CampaignResponse>> GetAllAsync(PagedRequest request, CampaignListFilter? filter = null)
    {
        filter ??= new CampaignListFilter();
        var query = await ScopedAsync(_dbContext.Campaigns.AsNoTracking().Include(c => c.Template).Include(c => c.EmailTemplate).Include(c => c.Connection).AsQueryable());

        if (!string.IsNullOrEmpty(filter.Status) && Enum.TryParse<CampaignStatus>(filter.Status, true, out var parsedStatus))
        {
            query = query.Where(c => c.Status == parsedStatus);
        }

        if (!string.IsNullOrEmpty(filter.Channel) && Enum.TryParse<MessageChannel>(filter.Channel, true, out var parsedChannel))
        {
            query = query.Where(c => c.Channel == parsedChannel);
        }

        if (!string.IsNullOrWhiteSpace(filter.Template) && !filter.Template.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            var template = filter.Template.Trim();
            query = query.Where(c => (c.Template != null && c.Template.Name == template)
                                  || (c.EmailTemplate != null && c.EmailTemplate.Name == template));
        }

        if (!string.IsNullOrWhiteSpace(filter.RelationType) && !filter.RelationType.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            // RelationType is stored comma-joined ("Lead,Customer"): match the whole token.
            var relation = filter.RelationType.Trim();
            query = query.Where(c => ("," + c.RelationType + ",").Contains("," + relation + ","));
        }

        if (filter.CreatedFrom is { } from)
        {
            var fromUtc = DateTime.SpecifyKind(from.Date, DateTimeKind.Utc);
            query = query.Where(c => c.CreatedAt >= fromUtc);
        }

        if (filter.CreatedTo is { } to)
        {
            var toExclusive = DateTime.SpecifyKind(to.Date.AddDays(1), DateTimeKind.Utc);
            query = query.Where(c => c.CreatedAt < toExclusive);
        }

        if (!string.IsNullOrEmpty(request.Search))
        {
            var search = request.Search.ToLower();

            // Status is an enum, so "failed" or "draft" cannot be matched as text in SQL. The
            // statuses whose names contain the term are resolved here and the query asks for those
            // values, which is translatable and keeps the whole search in one round trip.
            var matchingStatuses = Enum.GetValues<CampaignStatus>()
                .Where(s => s.ToString().ToLower().Contains(search))
                .ToList();

            query = query.Where(c =>
                c.Name.ToLower().Contains(search) ||
                // The template and the connection are both columns on the campaigns table.
                // Both template navigations are checked, since a campaign carries exactly one of
                // them depending on its channel. Null-guarded so the nullable FKs translate to a
                // left join with an explicit predicate rather than relying on SQL null semantics.
                (c.Template != null && c.Template.Name.ToLower().Contains(search)) ||
                (c.EmailTemplate != null && c.EmailTemplate.Name.ToLower().Contains(search)) ||
                (c.Connection != null && c.Connection.Name.ToLower().Contains(search)) ||
                c.RelationType.ToLower().Contains(search) ||
                (c.CreatedBy != null && c.CreatedBy.ToLower().Contains(search)) ||
                matchingStatuses.Contains(c.Status));
        }

        var totalCount = await query.CountAsync();

        // Only known columns are sortable; anything else falls back to newest first.
        var desc = request.SortDescending;
        query = (request.SortBy ?? string.Empty).ToLowerInvariant() switch
        {
            "name" => desc ? query.OrderByDescending(c => c.Name) : query.OrderBy(c => c.Name),
            "template" => desc
                ? query.OrderByDescending(c => c.Template != null ? c.Template.Name : c.EmailTemplate!.Name)
                : query.OrderBy(c => c.Template != null ? c.Template.Name : c.EmailTemplate!.Name),
            "relation" => desc ? query.OrderByDescending(c => c.RelationType) : query.OrderBy(c => c.RelationType),
            "total" => desc ? query.OrderByDescending(c => c.TotalRecipients) : query.OrderBy(c => c.TotalRecipients),
            "delivered" => desc ? query.OrderByDescending(c => c.DeliveredCount) : query.OrderBy(c => c.DeliveredCount),
            "read" => desc ? query.OrderByDescending(c => c.ReadCount) : query.OrderBy(c => c.ReadCount),
            "createdat" => desc ? query.OrderByDescending(c => c.CreatedAt) : query.OrderBy(c => c.CreatedAt),
            "id" => desc ? query.OrderByDescending(c => c.Id) : query.OrderBy(c => c.Id),
            _ => query.OrderByDescending(c => c.Id)
        };

        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync();

        return new PagedResponse<CampaignResponse>
        {
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize,
            Items = items.Select(MapToResponse).ToList()
        };
    }

    public async Task<CampaignDetailResponse> GetByIdAsync(int id)
    {
        await EnsureCampaignVisibleAsync(id);

        var campaign = await _dbContext.Campaigns
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(c => c.Template).Include(c => c.EmailTemplate).Include(c => c.Connection)
            .Include(c => c.Variables)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (campaign == null)
            throw new KeyNotFoundException($"Campaign with ID {id} not found.");

        // A preview only. The full recipient list is served, paged, by GetRecipientsAsync: this
        // used to load every recipient and contact of the campaign into memory for one detail
        // view — a million rows for a large bank campaign.
        var previewRecipients = await _dbContext.CampaignContacts
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(cc => cc.Contact)
            .Where(cc => cc.CampaignId == id)
            .OrderBy(cc => cc.Id)
            .Take(50)
            .ToListAsync();

        var isEmail = campaign.Channel == MessageChannel.Email;

        // WhatsApp reports delivery through Meta's status webhook; SMTP stops at "accepted by the
        // mail server" (bounces arrive later, by IMAP), so email reports acceptance, not delivery.
        var reportsDelivery = !isEmail;

        var response = new CampaignDetailResponse
        {
            ReportsDelivery = reportsDelivery,
            Id = campaign.Id,
            Name = campaign.Name,
            Channel = campaign.Channel.ToString(),
            // Template is nullable since the email channel: a WhatsApp campaign has a Meta
            // template, an email campaign has an EmailTemplate, and exactly one is set.
            TemplateName = ResolveTemplateName(campaign),
            RelationType = campaign.RelationType.ToString(),
            ScheduleType = campaign.ScheduleType.ToString(),
            ScheduledAt = campaign.ScheduledAt,
            Status = campaign.Status.ToString(),
            TotalRecipients = campaign.TotalRecipients,
            DeliveredCount = isEmail ? campaign.SentCount : campaign.DeliveredCount,
            ReadCount = isEmail ? campaign.OpenedCount : campaign.ReadCount,
            FailedCount = campaign.FailedCount,
            SentCount = campaign.SentCount,
            OpenedCount = campaign.OpenedCount,
            ClickedCount = campaign.ClickedCount,
            RepliedCount = campaign.RepliedCount,
            UnsubscribedCount = campaign.UnsubscribedCount,
            ComplainedCount = campaign.ComplainedCount,
            CreatedBy = campaign.CreatedBy,
            IsDeleted = campaign.IsDeleted,
            DeletedAt = campaign.DeletedAt,
            DeletedBy = campaign.DeletedBy,
            CreatedAt = campaign.CreatedAt,
            UpdatedAt = campaign.UpdatedAt,
            ConnectionId = campaign.ConnectionId,
            ConnectionName = campaign.Connection?.Name,
            ConnectionNickname = campaign.Connection?.Nickname,
            SkippedCount = campaign.SkippedCount,
            Topic = campaign.Topic,
            IsTransactional = campaign.IsTransactional,
            LocalSendAt = campaign.LocalSendAt,
            PausedReason = campaign.PausedReason,
            EmailStats = isEmail ? new EmailCampaignStatsResponse
            {
                Sent         = campaign.SentCount,
                Delivered    = campaign.DeliveredCount,
                Failed       = campaign.FailedCount,
                Bounced      = campaign.BouncedCount,
                Complained   = campaign.ComplainedCount,
                Suppressed   = campaign.SuppressedCount,
                Opened       = campaign.OpenedCount,
                Clicked      = campaign.ClickedCount,
                Replied      = campaign.RepliedCount,
                Unsubscribed = campaign.UnsubscribedCount,
                // Skipped recipients (consent, opt-out, frequency cap) are finished too.
                Pending      = Math.Max(0, campaign.TotalRecipients - campaign.SentCount - campaign.FailedCount - campaign.SuppressedCount - campaign.SkippedCount)
            } : null,
            Recipients = previewRecipients.Select(cc => new CampaignRecipientResponse
            {
                Id = cc.Id,
                ContactId = cc.ContactId,
                ContactName = cc.Contact.Name,
                Phone = cc.Contact.Phone,
                Email = cc.Contact.Email,
                Message = BuildRecipientMessagePreview(campaign, cc),
                Status = cc.Status.ToString(),
                SentAt = cc.SentAt,
                DeliveredAt = cc.DeliveredAt,
                ReadAt = isEmail ? (cc.OpenedAt ?? cc.ReadAt) : cc.ReadAt,
                OpenedAt = cc.OpenedAt,
                ErrorMessage = cc.ErrorMessage
            }).ToList(),
            Variables = campaign.Variables.Select(v => new CampaignVariableResponse
            {
                VariableName = v.VariableName,
                VariableValue = v.VariableValue,
                MergeField = v.MergeField
            }).ToList()
        };

        response.Approval = await BuildApprovalInfoAsync(campaign.Id);
        response.AbTest = await _abTests.GetResultAsync(campaign.Id);
        response.FollowUps = (await _followUps.GetForCampaignAsync(campaign.Id)).ToList();
        response.ParentCampaignId = campaign.ParentCampaignId;
        response.RetryableCount = await _dbContext.CampaignContacts.IgnoreQueryFilters()
            .CountAsync(cc => cc.CampaignId == campaign.Id && cc.Status == MessageStatus.Failed);
        response.RetryRuns = await _dbContext.CampaignRetryRuns.CountAsync(r => r.CampaignId == campaign.Id);
        response.MaxRetryRuns = Math.Max(1, _configuration.GetValue("Campaigns:Retry:MaxRuns", 3));

        return response;
    }

    // Splits, validates, and re-normalizes a comma-separated RelationType string (e.g.
    // "lead, customer" -> "Lead,Customer") for the normal (non-CSV) campaign create/update
    // path, which now supports targeting multiple relation types per campaign.
    // Contact types are an administrator-managed lookup, so the accepted values come from the
    // ContactTypes table (not the old Lead/Customer/Vendor enum) and are stored in their canonical
    // spelling.
    private async Task<string> NormalizeRelationTypesAsync(string relationType)
    {
        var tokens = relationType.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var known = await _dbContext.ContactTypes.AsNoTracking().Select(t => t.Value).ToListAsync();
        var normalized = new List<string>();
        foreach (var token in tokens)
        {
            var match = known.FirstOrDefault(k => string.Equals(k, token, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"\"{token}\" is not a contact type.");
            normalized.Add(match);
        }
        if (normalized.Count == 0)
            throw new ArgumentException("Choose at least one contact type.");
        return string.Join(",", normalized.Distinct());
    }

    public async Task<CampaignResponse> CreateAsync(CreateCampaignRequest request)
    {
        // Check for duplicate campaign name
        var normalizedName = request.Name.Trim().ToLower();
        var exists = await _dbContext.Campaigns.IgnoreQueryFilters().AnyAsync(c => !c.IsDeleted && c.Name.ToLower() == normalizedName);
        if (exists)
            throw new InvalidOperationException("The campaign name has already been taken.");

        // Channel defaults to WhatsApp when a caller does not say otherwise, so every request
        // written before the email channel existed still means what it meant before.
        var channel = ParseChannel(request.Channel);

        Template? template = null;
        EmailTemplate? emailTemplate = null;
        EmailSenderIdentity? senderIdentity = null;

        if (channel == MessageChannel.WhatsApp)
        {
            // Unchanged from before the email channel: a WhatsApp campaign still requires a
            // Meta-approved template, and still fails the same way without one.
            template = await _dbContext.Templates.FindAsync(request.TemplateId);
            if (template == null)
                throw new KeyNotFoundException("Template not found.");
            if (template.Status != TemplateStatus.Approved)
                throw new InvalidOperationException("Can only use APPROVED templates for campaigns.");
        }
        else
        {
            (emailTemplate, senderIdentity, request.ConnectionId) = await ResolveEmailTargetsAsync(
                request.EmailTemplateId, request.SenderIdentityId, request.ConnectionId);
        }

        await EnforcePrecheckAsync(request, channel);

        // One resolver for create and update, so both target recipients by the same rules.
        var contactIds = await ResolveTargetContactIdsAsync(request);

        if (contactIds.Count == 0)
            throw new ArgumentException("No active contacts found for the selected targets.");

        // Validated before the campaign is saved: an invalid A/B or follow-up setting used to throw
        // after the save, leaving a Sending/Scheduled campaign that was never dispatched.
        var abPlan = await PrepareAbTestAsync(channel, request);
        var followUpPlan = await _followUps.PrepareRulesAsync(channel, request.ConnectionId, request.FollowUps);

        await _accessScope.EnsureConnectionAllowedAsync(request.ConnectionId);

        // Create Campaign
        var campaign = new Campaign
        {
            Name = request.Name,
            Channel = channel,

            // Exactly one of the two template references is set, per channel. TemplateId became
            // nullable for this; a WhatsApp campaign still stores it exactly as before.
            TemplateId = channel == MessageChannel.WhatsApp ? request.TemplateId : null,
            EmailTemplateId = channel == MessageChannel.Email ? request.EmailTemplateId : null,

            RelationType = await NormalizeRelationTypesAsync(request.RelationType),
            ScheduleType = Enum.Parse<ScheduleType>(request.ScheduleType, true),
            ScheduledAt = ResolveScheduledAt(request),
            LocalSendAt = ResolveLocalSendAt(request),
            Topic = await NormalizeTopicAsync(request.Topic),
            IsTransactional = request.IsTransactional,
            Status = Enum.Parse<ScheduleType>(request.ScheduleType, true) == ScheduleType.Immediate ? CampaignStatus.Sending : CampaignStatus.Scheduled,
            TotalRecipients = contactIds.Count,
            ConnectionId = request.ConnectionId,
            AudienceIsSegmentsOnly = IsSegmentsOnly(request)
        };

        foreach (var segmentId in (request.SegmentIds ?? []).Distinct())
        {
            campaign.Segments.Add(new CampaignSegment { SegmentId = segmentId });
        }

        if (channel == MessageChannel.Email)
        {
            // Email-only settings live in a 1:1 side table rather than as mostly-null columns on
            // the shared Campaign entity — see EmailCampaignDetail.
            campaign.EmailDetail = new EmailCampaignDetail
            {
                SenderIdentityId = senderIdentity!.Id,
                SubjectOverride = string.IsNullOrWhiteSpace(request.SubjectOverride) ? null : request.SubjectOverride.Trim(),
                ReplyToOverride = string.IsNullOrWhiteSpace(request.ReplyToOverride) ? null : request.ReplyToOverride.Trim(),
                TrackOpens = request.TrackOpens,
                TrackClicks = request.TrackClicks,
                AttachmentsJson = request.Attachments is { Count: > 0 }
                    ? System.Text.Json.JsonSerializer.Serialize(request.Attachments)
                    : null
            };
        }

        // Add variables
        if (request.Variables != null)
        {
            foreach (var v in request.Variables)
            {
                campaign.Variables.Add(new CampaignVariable
                {
                    VariableName = v.VariableName,
                    VariableValue = v.VariableValue,
                    MergeField = v.MergeField
                });
            }

            var fileVar = request.Variables.FirstOrDefault(v => v.VariableName.Equals("file", StringComparison.OrdinalIgnoreCase));
            if (fileVar != null && !string.IsNullOrEmpty(fileVar.VariableValue))
            {
                campaign.FileUrl = fileVar.VariableValue;
                campaign.FileName = System.IO.Path.GetFileName(fileVar.VariableValue);
                campaign.FileType = GetMediaTypeFromUrl(fileVar.VariableValue);
            }
        }

        // Add Contacts
        foreach (var cid in contactIds)
        {
            campaign.CampaignContacts.Add(new CampaignContact
            {
                ContactId = cid,
                Status = MessageStatus.Pending
            });
        }

        _dbContext.Campaigns.Add(campaign);
        await _dbContext.SaveChangesAsync();

        await ApplyAbTestAsync(campaign, abPlan);
        await _followUps.AttachRulesAsync(campaign, followUpPlan);

        // Logged before the send is queued, so the audit entry precedes any delivery activity.
        await _auditService.LogAsync(
            "Campaign.Created", "Data",
            $"Created campaign \"{campaign.Name}\" ({campaign.ScheduleType}) with {campaign.CampaignContacts.Count} recipient(s).",
            "Campaign", campaign.Id.ToString());

        if (channel == MessageChannel.Email)
        {
            // The email channel goes through the durable queue: the dispatcher consults the
            // execution gate and enqueues expansion, and the queue handles scheduling, retries
            // and restart recovery. Awaited rather than fire-and-forget, so a rejected or parked
            // campaign is reflected in the response the operator sees.
            await _emailCampaignDispatcher.SubmitAsync(campaign.Id, requestedByUserId: _currentUser.UserId);
        }
        else
        {
            // WhatsApp goes through the durable queue too: survives restarts, retries Meta's
            // throttling, and is safe with several API instances running.
            await StartWhatsAppAsync(campaign);
        }

        var created = await _dbContext.Campaigns.Include(c => c.Template).Include(c => c.EmailTemplate).Include(c => c.Connection).FirstOrDefaultAsync(c => c.Id == campaign.Id);
        return MapToResponse(created!);
    }

    public async Task<CampaignResponse> UpdateAsync(int id, CreateCampaignRequest request)
    {
        await EnsureCampaignVisibleAsync(id);

        // Check for duplicate campaign name
        var normalizedName = request.Name.Trim().ToLower();
        var exists = await _dbContext.Campaigns.IgnoreQueryFilters().AnyAsync(c => c.Id != id && !c.IsDeleted && c.Name.ToLower() == normalizedName);
        if (exists)
            throw new InvalidOperationException("The campaign name has already been taken.");

        // Recipients are never loaded here: a campaign can have hundreds of thousands of them, and
        // loading them alongside Variables was also a two-collection include (a cartesian product,
        // which Development refuses with MultipleCollectionIncludeWarning). They are replaced
        // set-based below.
        var campaign = await _dbContext.Campaigns
            .IgnoreQueryFilters()
            .Include(c => c.Template).Include(c => c.EmailTemplate).Include(c => c.Connection)
            .Include(c => c.Variables)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (campaign == null)
            throw new KeyNotFoundException($"Campaign with ID {id} not found.");

        if (campaign.IsDeleted)
            throw new InvalidOperationException("Deleted campaigns cannot be edited or rescheduled.");

        if (campaign.Status is CampaignStatus.Sending or CampaignStatus.Sent or CampaignStatus.Cancelled)
            throw new InvalidOperationException("Your Campaign is already executed");

        // Each channel validates its own template. This used to require a WhatsApp template
        // unconditionally, so an email campaign could not be edited at all.
        EmailSenderIdentity? senderIdentity = null;
        if (campaign.Channel == MessageChannel.WhatsApp)
        {
            var template = await _dbContext.Templates.FindAsync(request.TemplateId);
            if (template == null)
                throw new KeyNotFoundException("Template not found.");
            if (template.Status != TemplateStatus.Approved)
                throw new InvalidOperationException("Can only use APPROVED templates for campaigns.");
        }
        else
        {
            (_, senderIdentity, request.ConnectionId) = await ResolveEmailTargetsAsync(
                request.EmailTemplateId, request.SenderIdentityId, request.ConnectionId ?? campaign.ConnectionId);
        }

        await EnforcePrecheckAsync(request, campaign.Channel);

        var contactIds = await ResolveTargetContactIdsAsync(request);
        if (contactIds.Count == 0)
            throw new ArgumentException("No active contacts found for the selected targets.");

        // Everything that can reject the request is checked before anything is written, so a bad
        // A/B or follow-up setting can no longer leave a half-updated campaign behind.
        var abPlan = await PrepareAbTestAsync(campaign.Channel, request);
        var followUpPlan = await _followUps.PrepareRulesAsync(campaign.Channel, request.ConnectionId ?? campaign.ConnectionId, request.FollowUps);

        var scheduleType = Enum.Parse<ScheduleType>(request.ScheduleType, true);

        await _accessScope.EnsureConnectionAllowedAsync(request.ConnectionId ?? campaign.ConnectionId);

        campaign.Name = request.Name;
        if (campaign.Channel == MessageChannel.WhatsApp)
        {
            campaign.TemplateId = request.TemplateId;
        }
        else
        {
            campaign.EmailTemplateId = request.EmailTemplateId;
            campaign.ConnectionId = request.ConnectionId;

            var detail = await _dbContext.EmailCampaignDetails.FirstOrDefaultAsync(d => d.CampaignId == campaign.Id);
            if (detail is null)
            {
                detail = new EmailCampaignDetail { CampaignId = campaign.Id };
                _dbContext.EmailCampaignDetails.Add(detail);
            }

            detail.SenderIdentityId = senderIdentity!.Id;
            detail.SubjectOverride = string.IsNullOrWhiteSpace(request.SubjectOverride) ? null : request.SubjectOverride.Trim();
            detail.ReplyToOverride = string.IsNullOrWhiteSpace(request.ReplyToOverride) ? null : request.ReplyToOverride.Trim();
            detail.TrackOpens = request.TrackOpens;
            detail.TrackClicks = request.TrackClicks;
            detail.AttachmentsJson = request.Attachments is { Count: > 0 }
                ? System.Text.Json.JsonSerializer.Serialize(request.Attachments)
                : null;
        }
        campaign.RelationType = await NormalizeRelationTypesAsync(request.RelationType);
        campaign.ScheduleType = scheduleType;
        campaign.ScheduledAt = ResolveScheduledAt(request);
        campaign.LocalSendAt = ResolveLocalSendAt(request);
        campaign.Topic = await NormalizeTopicAsync(request.Topic);
        campaign.IsTransactional = request.IsTransactional;
        campaign.AudienceIsSegmentsOnly = IsSegmentsOnly(request);

        await _dbContext.CampaignSegments.Where(cs => cs.CampaignId == campaign.Id).ExecuteDeleteAsync();
        foreach (var segmentId in (request.SegmentIds ?? []).Distinct())
        {
            _dbContext.CampaignSegments.Add(new CampaignSegment { CampaignId = campaign.Id, SegmentId = segmentId });
        }
        campaign.Status = scheduleType == ScheduleType.Immediate ? CampaignStatus.Sending : CampaignStatus.Scheduled;
        campaign.TotalRecipients = contactIds.Count;
        campaign.DeliveredCount = 0;
        campaign.ReadCount = 0;
        campaign.FailedCount = 0;
        campaign.SentCount = 0;
        campaign.BouncedCount = 0;
        campaign.SuppressedCount = 0;
        campaign.OpenedCount = 0;
        campaign.ClickedCount = 0;
        campaign.RepliedCount = 0;
        campaign.UnsubscribedCount = 0;
        campaign.ComplainedCount = 0;

        _dbContext.CampaignVariables.RemoveRange(campaign.Variables);
        campaign.Variables.Clear();
        campaign.FileUrl = null;
        campaign.FileName = null;
        campaign.FileType = null;

        if (request.Variables != null)
        {
            foreach (var variable in request.Variables)
            {
                campaign.Variables.Add(new CampaignVariable
                {
                    VariableName = variable.VariableName,
                    VariableValue = variable.VariableValue,
                    MergeField = variable.MergeField
                });
            }

            var fileVar = request.Variables.FirstOrDefault(v => v.VariableName.Equals("file", StringComparison.OrdinalIgnoreCase));
            if (fileVar != null && !string.IsNullOrEmpty(fileVar.VariableValue))
            {
                campaign.FileUrl = fileVar.VariableValue;
                campaign.FileName = System.IO.Path.GetFileName(fileVar.VariableValue);
                campaign.FileType = GetMediaTypeFromUrl(fileVar.VariableValue);
            }
        }

        // A/B settings are reset in the same save as the rest of the edit; the new test (if any)
        // is applied after the recipients exist.
        campaign.AbTestPercent = null;
        campaign.AbWinnerMetric = null;
        campaign.AbDecideAt = null;
        campaign.AbDecideAfterHours = null;
        campaign.AbWinnerVariantId = null;
        campaign.AbDecidedAt = null;

        // Set-based replacement: the old recipients are deleted in one statement (their events keep
        // their history through SetNull) and the new ones inserted in one batch, in one transaction,
        // so a failed save can never leave the campaign with no recipients. Run through the
        // execution strategy because the connection retries transient failures; a retry starts
        // over with fresh rows.
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync();
            await _dbContext.CampaignContacts.IgnoreQueryFilters().Where(cc => cc.CampaignId == campaign.Id).ExecuteDeleteAsync();
            var recipients = contactIds.Select(contactId => new CampaignContact
            {
                CampaignId = campaign.Id,
                ContactId = contactId,
                Status = MessageStatus.Pending
            }).ToList();
            _dbContext.CampaignContacts.AddRange(recipients);
            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch
            {
                foreach (var recipient in recipients) _dbContext.Entry(recipient).State = EntityState.Detached;
                throw;
            }
            await transaction.CommitAsync();
        });

        await _auditService.LogAsync(
            "Campaign.Updated", "Data",
            $"Updated campaign \"{campaign.Name}\".",
            "Campaign", campaign.Id.ToString());

        // What is sent must be what was approved: an edit clears any earlier decision, so under
        // maker-checker the edited campaign waits for a new approval.
        await _dbContext.CampaignApprovalStates.Where(a => a.CampaignId == campaign.Id).ExecuteDeleteAsync();

        await _dbContext.CampaignVariants.Where(v => v.CampaignId == campaign.Id).ExecuteDeleteAsync();
        await ApplyAbTestAsync(campaign, abPlan);

        await _dbContext.FollowUpRules.Where(r => r.CampaignId == campaign.Id && r.Status == "Pending").ExecuteDeleteAsync();
        await _followUps.AttachRulesAsync(campaign, followUpPlan);

        if (campaign.Channel == MessageChannel.Email)
        {
            await _emailCampaignDispatcher.SubmitAsync(campaign.Id, requestedByUserId: _currentUser.UserId, runId: Guid.NewGuid().ToString("N"));
        }
        else
        {
            await StartWhatsAppAsync(campaign);
        }

        var updated = await _dbContext.Campaigns.Include(c => c.Template).Include(c => c.EmailTemplate).Include(c => c.Connection).FirstOrDefaultAsync(c => c.Id == campaign.Id);
        return MapToResponse(updated!);
    }

    public async Task DeleteAsync(int id)
    {
        await EnsureCampaignVisibleAsync(id);

        var campaign = await _dbContext.Campaigns
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == id);

        if (campaign == null)
            throw new KeyNotFoundException($"Campaign with ID {id} not found.");

        _logger.LogInformation("Soft deleting campaign {CampaignId} ({CampaignName}) for audit.", campaign.Id, campaign.Name);

        campaign.IsDeleted = true;
        campaign.DeletedAt = DateTime.UtcNow;
        // Who actually deleted it, for the audit trail — never a hardcoded placeholder.
        campaign.DeletedBy = _currentUser.UserName ?? _currentUser.Email ?? "Unknown";
        campaign.Status = CampaignStatus.Cancelled;
        await _dbContext.SaveChangesAsync();

        // Set-based: deleting a large campaign must not load its recipients into memory.
        var cancelledCount = await MarkPendingRecipientsCancelledAsync(campaign.Id, "Campaign cancelled due to deletion.");

        await _auditService.LogAsync(
            "Campaign.Deleted", "Data",
            cancelledCount > 0
                ? $"Deleted campaign \"{campaign.Name}\"; {cancelledCount} pending recipient(s) cancelled."
                : $"Deleted campaign \"{campaign.Name}\".",
            "Campaign", campaign.Id.ToString());
    }

    public async Task<CampaignResponse> CancelAsync(int id)
    {
        await EnsureCampaignVisibleAsync(id);

        var campaign = await _dbContext.Campaigns.IgnoreQueryFilters().Include(c => c.Template).Include(c => c.EmailTemplate).Include(c => c.Connection).FirstOrDefaultAsync(c => c.Id == id);
        if (campaign == null)
            throw new KeyNotFoundException($"Campaign with ID {id} not found.");

        if (campaign.IsDeleted)
            throw new InvalidOperationException("Deleted campaigns cannot be edited or rescheduled.");

        if (campaign.Status is not (CampaignStatus.Scheduled or CampaignStatus.Sending or CampaignStatus.Paused or CampaignStatus.AwaitingApproval))
            throw new InvalidOperationException("Only a scheduled, sending, paused or awaiting-approval campaign can be cancelled.");

        var wasSending = campaign.Status == CampaignStatus.Sending;
        campaign.Status = CampaignStatus.Cancelled;
        await _dbContext.SaveChangesAsync();

        // Queued send jobs see the Cancelled status and stop; what never went out is recorded as
        // such, so the campaign's figures add up to its recipient total.
        var cancelledRecipients = await MarkPendingRecipientsCancelledAsync(campaign.Id, "Campaign cancelled before this message was sent.");

        await _auditService.LogAsync(
            "Campaign.Cancelled", "Data",
            $"Cancelled {(wasSending ? "running" : "scheduled")} campaign \"{campaign.Name}\"; {cancelledRecipients} unsent recipient(s) cancelled.",
            "Campaign", campaign.Id.ToString());

        return MapToResponse(campaign);
    }

    public async Task<CampaignResponse> PauseAsync(int id)
    {
        await EnsureCampaignVisibleAsync(id);

        var campaign = await _dbContext.Campaigns.IgnoreQueryFilters().Include(c => c.Template).Include(c => c.EmailTemplate).Include(c => c.Connection).FirstOrDefaultAsync(c => c.Id == id);
        if (campaign == null)
            throw new KeyNotFoundException($"Campaign with ID {id} not found.");

        if (campaign.IsDeleted)
            throw new InvalidOperationException("Deleted campaigns cannot be edited or rescheduled.");

        if (campaign.Status == CampaignStatus.Paused)
            return MapToResponse(campaign);

        // A running campaign can be paused too: queued send jobs check the status before every
        // recipient, so sending stops within one message per worker and resumes where it left off.
        if (campaign.Status is not (CampaignStatus.Scheduled or CampaignStatus.Sending))
            throw new InvalidOperationException("Your Campaign is already executed");

        campaign.Status = CampaignStatus.Paused;
        campaign.PausedReason = null;
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            "Campaign.Paused", "Data",
            $"Paused campaign \"{campaign.Name}\".",
            "Campaign", campaign.Id.ToString());

        return MapToResponse(campaign);
    }

    public async Task<CampaignResponse> ResumeAsync(int id)
    {
        await EnsureCampaignVisibleAsync(id);

        var campaign = await _dbContext.Campaigns.IgnoreQueryFilters().Include(c => c.Template).Include(c => c.EmailTemplate).Include(c => c.Connection).FirstOrDefaultAsync(c => c.Id == id);
        if (campaign == null)
            throw new KeyNotFoundException($"Campaign with ID {id} not found.");

        if (campaign.IsDeleted)
            throw new InvalidOperationException("Deleted campaigns cannot be edited or rescheduled.");

        if (campaign.Status != CampaignStatus.Paused)
            throw new InvalidOperationException("Only paused campaigns can be resumed.");

        // A hold the system placed is lifted by resuming; the next send re-checks the connection
        // and holds again, with a fresh reason, if it is still unusable.
        campaign.PausedReason = null;
        await _dbContext.SaveChangesAsync();

        if (campaign.Channel == MessageChannel.Email)
        {
            // A new run: the original expansion completed without sending while the campaign was
            // paused, and reusing its queue key would silently queue nothing.
            await _emailCampaignDispatcher.SubmitAsync(campaign.Id, requestedByUserId: _currentUser.UserId, runId: Guid.NewGuid().ToString("N"));

            await _auditService.LogAsync(
                "Campaign.Resumed", "Data",
                $"Resumed campaign \"{campaign.Name}\".",
                "Campaign", campaign.Id.ToString());
        }
        else if (campaign.ScheduleType != ScheduleType.Immediate && campaign.ScheduledAt.HasValue && campaign.ScheduledAt > DateTime.UtcNow)
        {
            campaign.Status = CampaignStatus.Scheduled;
            await _dbContext.SaveChangesAsync();

            await _auditService.LogAsync(
                "Campaign.Resumed", "Data",
                $"Resumed campaign \"{campaign.Name}\"; still scheduled for {campaign.ScheduledAt:u}.",
                "Campaign", campaign.Id.ToString());
        }
        else
        {
            campaign.Status = CampaignStatus.Sending;
            await _dbContext.SaveChangesAsync();

            // Logged before the background send starts, for the same scope-lifetime reason as
            // CreateAsync.
            await _auditService.LogAsync(
                "Campaign.Resumed", "Data",
                $"Resumed campaign \"{campaign.Name}\"; sending started.",
                "Campaign", campaign.Id.ToString());

            await _whatsAppDispatcher.StartAsync(campaign.Id, notBefore: null);
        }

        return MapToResponse(campaign);
    }

    public async Task<PagedResponse<CampaignRecipientResponse>> GetRecipientsAsync(int campaignId, PagedRequest request, string? state = null)
    {
        await EnsureCampaignVisibleAsync(campaignId);

        var campaign = await _dbContext.Campaigns
            .AsNoTracking()
            .Include(c => c.Template).Include(c => c.EmailTemplate).Include(c => c.Connection)
            .Include(c => c.Variables)
            .FirstOrDefaultAsync(c => c.Id == campaignId);

        if (campaign == null)
            throw new KeyNotFoundException($"Campaign with ID {campaignId} not found.");

        var query = _dbContext.CampaignContacts
            .AsNoTracking()
            .Include(cc => cc.Contact)
            .Where(cc => cc.CampaignId == campaignId);

        // The detail page's Queue / Executed tabs and its search box, applied in the database
        // rather than over a full download of the recipient list.
        if (string.Equals(state, "queue", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(cc => cc.Status == MessageStatus.Pending);
        }
        else if (string.Equals(state, "executed", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(cc => cc.Status != MessageStatus.Pending);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(cc =>
                cc.Contact.Name.ToLower().Contains(term)
                || cc.Contact.Phone.Contains(term)
                || (cc.Contact.Email != null && cc.Contact.Email.ToLower().Contains(term)));
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderBy(cc => cc.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync();

        var campaignContactIds = items.Select(i => i.Id).ToList();
        var chatMessageTextByContact = await _dbContext.ChatMessages
            .Where(m => m.CampaignContactId.HasValue && campaignContactIds.Contains(m.CampaignContactId.Value))
            .GroupBy(m => m.CampaignContactId!.Value)
            .Select(g => new { CampaignContactId = g.Key, Text = g.OrderByDescending(m => m.Id).Select(m => m.Text).FirstOrDefault() })
            .ToDictionaryAsync(m => m.CampaignContactId, m => m.Text ?? string.Empty);

        return new PagedResponse<CampaignRecipientResponse>
        {
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize,
            Items = items.Select(cc => new CampaignRecipientResponse
            {
                Id = cc.Id,
                ContactId = cc.ContactId,
                ContactName = cc.Contact.Name,
                Phone = cc.Contact.Phone,
                Email = cc.Contact.Email,
                Message = chatMessageTextByContact.TryGetValue(cc.Id, out var messageText) && !string.IsNullOrWhiteSpace(messageText)
                    ? messageText
                    : BuildRecipientMessagePreview(campaign, cc),
                Status = cc.Status.ToString(),
                SentAt = cc.SentAt,
                DeliveredAt = cc.DeliveredAt,
                ReadAt = campaign.Channel == MessageChannel.Email ? (cc.OpenedAt ?? cc.ReadAt) : cc.ReadAt,
                OpenedAt = cc.OpenedAt,
                ErrorMessage = cc.ErrorMessage
            }).ToList()
        };
    }

    /// <summary>
    /// Safety net for scheduled WhatsApp campaigns. Scheduled sends are queued with their start
    /// time when created, so this only matters for campaigns scheduled before the queue carried
    /// WhatsApp; the scheduled run key is deterministic, so re-queuing an already-queued one is a
    /// no-op. Email campaigns are the email queue's business and are left alone.
    /// </summary>
    public async Task<(int Queued, int Executed)> GetRecipientCountsAsync(int campaignId)
    {
        await EnsureCampaignVisibleAsync(campaignId);

        // One grouped index scan on (CampaignId, Status).
        var byStatus = await _dbContext.CampaignContacts
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(cc => cc.CampaignId == campaignId)
            .GroupBy(cc => cc.Status == MessageStatus.Pending)
            .Select(g => new { Pending = g.Key, Count = g.Count() })
            .ToListAsync();

        var queued = byStatus.FirstOrDefault(x => x.Pending)?.Count ?? 0;
        var executed = byStatus.FirstOrDefault(x => !x.Pending)?.Count ?? 0;
        return (queued, executed);
    }

    public async Task ExportRecipientsCsvAsync(int campaignId, Stream output, CancellationToken ct)
    {
        await EnsureCampaignVisibleAsync(campaignId);

        var isEmail = await _dbContext.Campaigns
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.Id == campaignId)
            .Select(c => (MessageChannel?)c.Channel)
            .FirstOrDefaultAsync(ct) ?? throw new KeyNotFoundException($"Campaign with ID {campaignId} not found.");

        const int batchSize = 1000;
        var lastId = 0;
        var first = true;

        while (!ct.IsCancellationRequested)
        {
            // Keyset batches: memory stays flat whatever the campaign size, and the response
            // streams to the browser while later batches are still being read.
            var batch = await _dbContext.CampaignContacts
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(cc => cc.CampaignId == campaignId && cc.Id > lastId)
                .OrderBy(cc => cc.Id)
                .Take(batchSize)
                .Select(cc => new
                {
                    cc.Id,
                    Name = cc.Contact.Name,
                    cc.Contact.Phone,
                    cc.Contact.Email,
                    cc.Status,
                    cc.SentAt,
                    cc.DeliveredAt,
                    cc.ReadAt,
                    cc.OpenedAt,
                    cc.ClickedAt,
                    cc.RepliedAt,
                    cc.ErrorMessage
                })
                .ToListAsync(ct);

            if (batch.Count == 0) break;
            lastId = batch[^1].Id;

            var writer = new CsvWriter();
            if (first)
            {
                writer.WriteHeader("Recipient ID", "Name", "Phone", "Email", "Status", "Sent At (UTC)", "Delivered At (UTC)",
                    isEmail == MessageChannel.Email ? "Opened At (UTC)" : "Read At (UTC)", "Clicked At (UTC)", "Replied At (UTC)", "Error");
            }

            foreach (var r in batch)
            {
                writer.WriteRow(r.Id, r.Name, r.Phone, r.Email, r.Status.ToString(), r.SentAt, r.DeliveredAt,
                    isEmail == MessageChannel.Email ? r.OpenedAt : r.ReadAt, r.ClickedAt, r.RepliedAt, r.ErrorMessage);
            }

            var bytes = writer.ToBytes();
            // The byte-order mark belongs at the start of the file only.
            var preamble = System.Text.Encoding.UTF8.GetPreamble().Length;
            await output.WriteAsync(first ? bytes : bytes.AsMemory(preamble), ct);
            await output.FlushAsync(ct);
            first = false;

            if (batch.Count < batchSize) break;
        }
    }

    public async Task ProcessScheduledCampaignsAsync(CancellationToken cancellationToken)
    {
        var due = await _dbContext.Campaigns
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => !c.IsDeleted
                     && c.Channel == MessageChannel.WhatsApp
                     && c.Status == CampaignStatus.Scheduled
                     && c.ScheduledAt <= DateTime.UtcNow)
            .Select(c => new { c.Id, c.ScheduledAt })
            .Take(500)
            .ToListAsync(cancellationToken);

        foreach (var campaign in due)
        {
            await _whatsAppDispatcher.StartAsync(campaign.Id, campaign.ScheduledAt, cancellationToken);
        }

        // Recovery: a WhatsApp campaign in Sending with recipients still pending but no queued or
        // running job for it will never progress — the in-memory sender this replaced left
        // campaigns like that behind on every restart. A new run picks them up; recipients already
        // sent are skipped by the send worker.
        var stalledBefore = DateTime.UtcNow.AddMinutes(-5);
        var stalled = await _dbContext.Campaigns
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => !c.IsDeleted
                     && c.Channel == MessageChannel.WhatsApp
                     && c.Status == CampaignStatus.Sending
                     && c.UpdatedAt < stalledBefore
                     && _dbContext.CampaignContacts.IgnoreQueryFilters()
                            .Any(cc => cc.CampaignId == c.Id && cc.Status == MessageStatus.Pending)
                     && !_dbContext.JobQueue.Any(j =>
                            (j.QueueName == Queue.QueueNames.WhatsAppSend || j.QueueName == Queue.QueueNames.WhatsAppCampaignExpansion)
                            && j.PartitionKey == c.Id.ToString()
                            && (j.Status == JobStatus.Pending || j.Status == JobStatus.Leased)))
            .Select(c => c.Id)
            .Take(50)
            .ToListAsync(cancellationToken);

        foreach (var campaignId in stalled)
        {
            _logger.LogWarning("WhatsApp campaign {CampaignId} was stalled in Sending; re-queuing its pending recipients.", campaignId);
            await _whatsAppDispatcher.StartAsync(campaignId, notBefore: null, cancellationToken);
        }
    }

    /// <summary>
    /// Queues a WhatsApp campaign — now, or at its scheduled time — once the execution gate allows
    /// it. The email dispatcher consults the same gate, so maker-checker covers both channels.
    /// </summary>
    private async Task StartWhatsAppAsync(Campaign campaign)
    {
        var existing = await _dbContext.CampaignApprovalStates.FirstOrDefaultAsync(a => a.CampaignId == campaign.Id);

        var decision = await _executionGate.EvaluateAsync(new CampaignExecutionRequest(
            campaign.Id,
            campaign.Name,
            campaign.Channel.ToString(),
            campaign.TotalRecipients,
            campaign.Template?.Name,
            _currentUser.UserId,
            existing?.ExternalReferenceId));

        if (decision.Outcome != CampaignExecutionOutcome.Approved)
        {
            var parked = decision.Outcome == CampaignExecutionOutcome.AwaitingExternalApproval;
            campaign.Status = parked ? CampaignStatus.AwaitingApproval : CampaignStatus.Cancelled;

            if (existing is null)
            {
                _dbContext.CampaignApprovalStates.Add(new CampaignApprovalState
                {
                    CampaignId = campaign.Id,
                    State = parked ? "Pending" : "Rejected",
                    ExternalReferenceId = decision.ExternalReferenceId,
                    Reason = decision.Reason,
                    RequestedByUserId = _currentUser.UserId,
                    RequestedAt = DateTime.UtcNow,
                    DecidedAt = parked ? null : DateTime.UtcNow
                });
            }
            else if (parked)
            {
                existing.State = "Pending";
                existing.RequestedByUserId = _currentUser.UserId ?? existing.RequestedByUserId;
            }

            await _dbContext.SaveChangesAsync();

            await _auditService.LogAsync(
                parked ? "Campaign.AwaitingApproval" : "Campaign.ExecutionRejected", "Data",
                parked
                    ? $"Campaign \"{campaign.Name}\" is waiting for approval."
                    : $"Campaign \"{campaign.Name}\" was rejected by the {_executionGate.GateName} execution gate: {decision.Reason}",
                "Campaign", campaign.Id.ToString());
            return;
        }

        await _whatsAppDispatcher.StartAsync(
            campaign.Id,
            campaign.ScheduleType != ScheduleType.Immediate ? campaign.ScheduledAt : null);
    }

    // ── Link report ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Clicks per link: total and unique, from the recorded click events. One grouped query on the
    /// (CampaignId, EventKind, OccurredAt) index; the list is capped because a message with more
    /// than a few hundred links is not a real campaign.
    /// </summary>
    public async Task<IReadOnlyList<CampaignLinkClicks>> GetLinkReportAsync(int id, CancellationToken ct = default)
    {
        await EnsureCampaignVisibleAsync(id);

        return await _dbContext.EmailEvents.AsNoTracking()
            .Where(e => e.CampaignId == id && e.EventKind == EmailEventKind.Clicked && e.OriginalUrl != null)
            .GroupBy(e => e.OriginalUrl!)
            .Select(g => new CampaignLinkClicks
            {
                Url = g.Key,
                TotalClicks = g.Count(),
                UniqueClickers = g.Select(e => e.CampaignContactId).Distinct().Count(),
                FirstClickAt = g.Min(e => e.OccurredAt),
                LastClickAt = g.Max(e => e.OccurredAt)
            })
            .OrderByDescending(x => x.TotalClicks)
            .ThenBy(x => x.Url)
            .Take(500)
            .ToListAsync(ct);
    }

    // ── Retry failed recipients ───────────────────────────────────────────────────────────

    /// <summary>
    /// Puts a finished campaign's failed recipients back in the queue and sends to them again.
    /// </summary>
    /// <remarks>
    /// Only recipients whose send failed are retried. Bounced, complained and suppressed
    /// recipients are never retried: mailing an address that does not exist, or a person who
    /// objected, again is exactly what deliverability rules and consent forbid. Each recipient
    /// goes back through every check a first send gets (suppression, rate limits, the approval
    /// gate), under a new queue run so no earlier job is mistaken for it.
    /// </remarks>
    public async Task<CampaignRetryResponse> RetryFailedAsync(int id)
    {
        await EnsureCampaignVisibleAsync(id);

        var campaign = await _dbContext.Campaigns
            .Include(c => c.Template).Include(c => c.EmailTemplate).Include(c => c.Connection)
            .FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new KeyNotFoundException("Campaign not found.");

        if (campaign.Status is not (CampaignStatus.Sent or CampaignStatus.PartiallyFailed or CampaignStatus.Failed))
            throw new InvalidOperationException("Only a finished campaign can have its failed recipients retried.");

        var maxRuns = Math.Max(1, _configuration.GetValue("Campaigns:Retry:MaxRuns", 3));
        var previousRuns = await _dbContext.CampaignRetryRuns.CountAsync(r => r.CampaignId == id);
        if (previousRuns >= maxRuns)
            throw new InvalidOperationException($"This campaign has already been retried {previousRuns} time(s), the maximum allowed.");

        var runId = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;

        // One set-based UPDATE: reset the failed recipients so the pipeline treats them as unsent.
        var reset = await _dbContext.CampaignContacts
            .IgnoreQueryFilters()
            .Where(cc => cc.CampaignId == id && cc.Status == MessageStatus.Failed)
            .ExecuteUpdateAsync(u => u
                .SetProperty(cc => cc.Status, MessageStatus.Pending)
                .SetProperty(cc => cc.SendAttemptedAt, (DateTime?)null)
                .SetProperty(cc => cc.ErrorMessage, (string?)null)
                .SetProperty(cc => cc.ProviderMessageId, (string?)null)
                .SetProperty(cc => cc.WhatsAppMessageId, (string?)null));

        if (reset == 0)
            throw new InvalidOperationException("This campaign has no failed recipients to retry.");

        _dbContext.CampaignRetryRuns.Add(new CampaignRetryRun
        {
            CampaignId = id,
            RunId = runId,
            RecipientCount = reset,
            RequestedByUserId = _currentUser.UserId,
            RequestedAt = now
        });

        campaign.Status = CampaignStatus.Sending;
        campaign.UpdatedAt = now;
        await _dbContext.SaveChangesAsync();

        // The Failed figure drops by the recipients just reset; the rest are recomputed with it.
        if (campaign.Channel == MessageChannel.Email)
            await CampaignFinalizer.ReconcileEmailCountersAsync(_dbContext, id, CancellationToken.None);
        else
            await CampaignFinalizer.ReconcileWhatsAppCountersAsync(_dbContext, id, CancellationToken.None);

        await _auditService.LogAsync(
            "Campaign.RetryFailed", "Data",
            $"Retrying {reset} failed recipient(s) of campaign \"{campaign.Name}\" (retry {previousRuns + 1} of {maxRuns}).",
            "Campaign", id.ToString());

        if (campaign.Channel == MessageChannel.Email)
            await _emailCampaignDispatcher.SubmitAsync(id, _currentUser.UserId, runId: runId);
        else
            await _whatsAppDispatcher.StartAsync(id, notBefore: null);

        await PublishStatusAsync(id, campaign.Status);

        return new CampaignRetryResponse { CampaignId = id, RecipientCount = reset, RetryNumber = previousRuns + 1, MaxRetries = maxRuns };
    }

    // ── Maker-checker ─────────────────────────────────────────────────────────────────────

    public async Task<int> GetPendingApprovalCountAsync() =>
        await (await ScopedAsync(_dbContext.Campaigns.AsNoTracking()))
            .CountAsync(c => c.Status == CampaignStatus.AwaitingApproval);

    /// <summary>
    /// Approves a parked campaign and sends it (or schedules it) through the normal pipeline.
    /// The approver must hold Campaign.Approve (checked by the controller) and must not be the
    /// user who submitted it — enforced here, for administrators too.
    /// </summary>
    public async Task<CampaignResponse> ApproveAsync(int id, string? comment)
    {
        var (campaign, approval) = await LoadForDecisionAsync(id);

        approval.State = "Approved";
        approval.DecidedAt = DateTime.UtcNow;
        approval.DecidedByUserId = _currentUser.UserId;
        approval.DecidedBy = Truncate(_currentUser.UserName ?? _currentUser.Email, 200);
        approval.Reason = Truncate(string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(), 1000);

        // A scheduled time that passed while the campaign waited means "send now".
        var isScheduled = campaign.ScheduleType != ScheduleType.Immediate && campaign.ScheduledAt > DateTime.UtcNow;
        campaign.Status = isScheduled ? CampaignStatus.Scheduled : CampaignStatus.Sending;
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            "Campaign.Approved", "Data",
            $"Approved campaign \"{campaign.Name}\"{(approval.Reason is null ? "" : $": {approval.Reason}")}.",
            "Campaign", campaign.Id.ToString());

        if (campaign.Channel == MessageChannel.Email)
        {
            await _emailCampaignDispatcher.SubmitAsync(campaign.Id, approval.RequestedByUserId);
        }
        else
        {
            await _whatsAppDispatcher.StartAsync(campaign.Id, isScheduled ? campaign.ScheduledAt : null);
        }

        await EmitApprovalAsync(campaign, approval);
        await PublishStatusAsync(campaign.Id, campaign.Status);
        return MapToResponse(campaign);
    }

    /// <summary>Rejects a parked campaign: it is cancelled and nothing is sent.</summary>
    public async Task<CampaignResponse> RejectAsync(int id, string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
            throw new ArgumentException("Give a reason for rejecting the campaign.");

        var (campaign, approval) = await LoadForDecisionAsync(id);

        approval.State = "Rejected";
        approval.DecidedAt = DateTime.UtcNow;
        approval.DecidedByUserId = _currentUser.UserId;
        approval.DecidedBy = Truncate(_currentUser.UserName ?? _currentUser.Email, 200);
        approval.Reason = Truncate(comment.Trim(), 1000);
        campaign.Status = CampaignStatus.Cancelled;
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            "Campaign.Rejected", "Data",
            $"Rejected campaign \"{campaign.Name}\": {approval.Reason}",
            "Campaign", campaign.Id.ToString());

        await EmitApprovalAsync(campaign, approval);
        await PublishStatusAsync(campaign.Id, campaign.Status);
        return MapToResponse(campaign);
    }

    private async Task EmitApprovalAsync(Campaign campaign, CampaignApprovalState approval)
    {
        if (_webhooks is null) return;
        await _webhooks.EmitAsync("approval.decided", campaign.ConnectionId, new Dictionary<string, object?>
        {
            ["campaignId"] = campaign.Id,
            ["campaignName"] = campaign.Name,
            ["decision"] = approval.State,
            ["decidedByUserId"] = approval.DecidedByUserId,
            ["requestedByUserId"] = approval.RequestedByUserId,
            ["reason"] = approval.Reason
        });
    }

    private async Task<(Campaign Campaign, CampaignApprovalState Approval)> LoadForDecisionAsync(int id)
    {
        await EnsureCampaignVisibleAsync(id);

        var campaign = await _dbContext.Campaigns
            .Include(c => c.Template).Include(c => c.EmailTemplate).Include(c => c.Connection)
            .FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new KeyNotFoundException("Campaign not found.");

        if (campaign.Status != CampaignStatus.AwaitingApproval)
            throw new InvalidOperationException("This campaign is not waiting for approval.");

        var approval = await _dbContext.CampaignApprovalStates.FirstOrDefaultAsync(a => a.CampaignId == id)
            ?? throw new InvalidOperationException("This campaign has no pending approval request.");

        if (approval.RequestedByUserId is { } maker && maker == _currentUser.UserId)
            throw new Security.ForbiddenException("A campaign must be approved by someone other than the person who submitted it.");

        return (campaign, approval);
    }

    private async Task<CampaignApprovalInfo?> BuildApprovalInfoAsync(int campaignId)
    {
        var approval = await _dbContext.CampaignApprovalStates.AsNoTracking()
            .FirstOrDefaultAsync(a => a.CampaignId == campaignId);
        if (approval is null) return null;

        var requestedBy = approval.RequestedByUserId is { } makerId
            ? await _dbContext.AppUsers.AsNoTracking()
                .Where(u => u.Id == makerId)
                .Select(u => (u.FirstName + " " + (u.LastName ?? "")).Trim())
                .FirstOrDefaultAsync()
            : null;

        return new CampaignApprovalInfo
        {
            State = approval.State,
            RequestedBy = requestedBy,
            RequestedAt = approval.RequestedAt,
            DecidedBy = approval.DecidedBy,
            DecidedAt = approval.DecidedAt,
            Reason = approval.Reason,
            CanDecide = approval.State == "Pending"
                && approval.RequestedByUserId != _currentUser.UserId
                && await _currentUser.HasPermissionAsync("Campaign.Approve")
        };
    }

    /// <summary>Pushes a status change to open campaign pages (no counter changes).</summary>
    private async Task PublishStatusAsync(int campaignId, CampaignStatus status)
    {
        try
        {
            await _eventPublisher.PublishEmailEventAsync(new CampaignEmailEventNotification(
                CampaignId: campaignId,
                Kind: EmailEventKind.Sent,
                CampaignContactId: null,
                RecipientAddress: null,
                OccurredAt: DateTime.UtcNow,
                NewCampaignStatus: status.ToString()));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not publish the status change of campaign {CampaignId}.", campaignId);
        }
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];

    /// <summary>
    /// Marks every still-pending recipient of a stopped campaign as failed with the given reason,
    /// set-based, and keeps the campaign's Failed figure in step. Returns how many were marked.
    /// </summary>
    private async Task<int> MarkPendingRecipientsCancelledAsync(int campaignId, string reason)
    {
        var marked = await _dbContext.CampaignContacts
            .IgnoreQueryFilters()
            .Where(cc => cc.CampaignId == campaignId && cc.Status == MessageStatus.Pending)
            .ExecuteUpdateAsync(u => u
                .SetProperty(cc => cc.Status, MessageStatus.Failed)
                .SetProperty(cc => cc.ErrorMessage, reason));

        if (marked > 0)
        {
            await _dbContext.Campaigns
                .IgnoreQueryFilters()
                .Where(c => c.Id == campaignId)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.FailedCount, c => c.FailedCount + marked));
        }

        return marked;
    }

    private static string BuildCampaignMessagePreview(string bodyText, Dictionary<string, string> variables)
    {
        var preview = bodyText;
        foreach (var variable in variables)
        {
            preview = preview.Replace("{{" + variable.Key + "}}", variable.Value);
        }

        return preview;
    }

    private async Task<HashSet<int>> ResolveTargetContactIdsAsync(CreateCampaignRequest request)
    {
        if (request.SelectAllContacts)
        {
            // Resolved entirely in the database: every active contact of the chosen relation
            // types, narrowed by status and source when given.
            var query = _dbContext.Contacts.AsNoTracking().Where(c => c.IsActive && !c.IsDeleted);

            var types = request.RelationType
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => t.ToLower())
                .ToList();
            if (types.Count > 0) query = query.Where(c => types.Contains(c.Type.ToLower()));

            if (!string.IsNullOrWhiteSpace(request.ContactStatus) && !request.ContactStatus.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                var status = request.ContactStatus.Trim().ToLower();
                query = query.Where(c => c.Status.ToLower() == status);
            }

            if (!string.IsNullOrWhiteSpace(request.ContactSource) && !request.ContactSource.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                var source = request.ContactSource.Trim().ToLower();
                query = query.Where(c => c.Source.ToLower() == source);
            }

            return (await query.Select(c => c.Id).ToListAsync()).ToHashSet();
        }

        var contactIds = new HashSet<int>();

        // Segments: their members as of now. The expansion step resolves them again at send time.
        if (request.SegmentIds is { Count: > 0 })
        {
            contactIds.UnionWith(await _segments.ResolveContactIdsAsync(request.SegmentIds));
        }

        if (request.ContactIds != null)
        {
            foreach (var contactId in request.ContactIds)
            {
                contactIds.Add(contactId);
            }
        }

        if (request.GroupIds != null && request.GroupIds.Any())
        {
            var groupContacts = await _dbContext.ContactGroupMembers
                .Where(gm => request.GroupIds.Contains(gm.GroupId))
                .Select(gm => gm.ContactId)
                .ToListAsync();

            foreach (var contactId in groupContacts)
            {
                contactIds.Add(contactId);
            }
        }

        var activeContactIds = await _dbContext.Contacts
            .Where(c => contactIds.Contains(c.Id) && c.IsActive && !c.IsDeleted)
            .Select(c => c.Id)
            .ToListAsync();

        return new HashSet<int>(activeContactIds);
    }

    internal static string BuildRecipientMessagePreview(Campaign campaign, CampaignContact campaignContact)
    {
        if (campaignContact.Contact == null)
        {
            return "[Inactive Contact]";
        }
        var messageVars = new Dictionary<string, string>();
        string? attachmentUrl = null;
        foreach (var variable in campaign.Variables)
        {
            if (variable.VariableName.Equals("file", StringComparison.OrdinalIgnoreCase))
            {
                attachmentUrl = variable.VariableValue;
                continue;
            }

            var finalValue = variable.VariableValue ?? string.Empty;
            if (variable.MergeField == "@name") finalValue = campaignContact.Contact.Name;
            else if (variable.MergeField == "@phone") finalValue = campaignContact.Contact.Phone;

            messageVars[variable.VariableName] = finalValue;
        }

        // Template is nullable since the email channel. This preview is WhatsApp's body text;
        // the email equivalent is rendered by the email pipeline from the EmailTemplate, so an
        // email campaign legitimately has nothing to substitute into here.
        var text = BuildCampaignMessagePreview(campaign.Template?.BodyText ?? string.Empty, messageVars);
        if (!string.IsNullOrEmpty(attachmentUrl))
        {
            var fileName = System.IO.Path.GetFileName(attachmentUrl);
            text = $"[Attachment: {fileName}]\n\n" + text;
        }
        return text;
    }

    public async Task<bool> CheckNameExistsAsync(string name, int? excludeId = null)
    {
        var query = _dbContext.Campaigns.AsQueryable();
        if (excludeId.HasValue)
        {
            query = query.Where(c => c.Id != excludeId.Value);
        }
        return await query.AnyAsync(c => c.Name.ToLower() == name.Trim().ToLower());
    }

    /// <summary>
    /// The template name to show for a campaign on either channel.
    ///
    /// <para>
    /// Campaign.Template became nullable when the email channel was added, so this is the single
    /// place that decides what "the template" means per channel. Falls back to an empty string
    /// rather than throwing: a campaign whose template row has since been removed should still be
    /// listable, which is exactly when an operator most needs to see it.
    /// </para>
    /// </summary>
    private static string ResolveTemplateName(Campaign c) =>
        c.Channel == MessageChannel.Email
            ? c.EmailTemplate?.Name ?? string.Empty
            : c.Template?.Name ?? string.Empty;

    private static CampaignResponse MapToResponse(Campaign c)
    {
        var isEmail = c.Channel == MessageChannel.Email;

        return new CampaignResponse
        {
            Id = c.Id,
            Name = c.Name,
            // Always populated, so the campaigns list can render a channel column without a
            // second lookup. Existing rows read as WhatsApp.
            Channel = c.Channel.ToString(),
            TemplateName = ResolveTemplateName(c),
            SkippedCount = c.SkippedCount,
            Topic = c.Topic,
            IsTransactional = c.IsTransactional,
            LocalSendAt = c.LocalSendAt,
            PausedReason = c.PausedReason,
            RelationType = c.RelationType.ToString(),
            ScheduleType = c.ScheduleType.ToString(),
            ScheduledAt = c.ScheduledAt,
            Status = c.Status.ToString(),
            TotalRecipients = c.TotalRecipients,
            // Shared delivery counters — for WhatsApp these come from the WA delivery webhook;
            // for Email they mirror the email-specific counters so shared reports still work.
            DeliveredCount = isEmail ? c.SentCount    : c.DeliveredCount,
            ReadCount      = isEmail ? c.OpenedCount  : c.ReadCount,
            FailedCount    = isEmail ? c.FailedCount  : c.FailedCount,
            SentCount      = c.SentCount,
            OpenedCount    = c.OpenedCount,
            ClickedCount   = c.ClickedCount,
            RepliedCount   = c.RepliedCount,
            UnsubscribedCount = c.UnsubscribedCount,
            ComplainedCount = c.ComplainedCount,
            CreatedBy = c.CreatedBy,
            IsDeleted = c.IsDeleted,
            DeletedAt = c.DeletedAt,
            DeletedBy = c.DeletedBy,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt,
            IsBulkCampaign = c.IsBulkCampaign,
            ConnectionId = c.ConnectionId,
            ConnectionName = c.Connection?.Name,
            ConnectionNickname = c.Connection?.Nickname,

            // Email-only engagement counters. Null for WhatsApp so the client can branch on it.
            EmailStats = isEmail ? new EmailCampaignStatsResponse
            {
                Sent         = c.SentCount,
                Delivered    = c.DeliveredCount,   // future: from DSN/webhook
                Failed       = c.FailedCount,
                Bounced      = 0,                  // future: from bounce events
                Complained   = c.ComplainedCount,
                Suppressed   = 0,
                Opened       = c.OpenedCount,
                Clicked      = c.ClickedCount,
                Replied      = c.RepliedCount,
                Unsubscribed = c.UnsubscribedCount,
                Pending      = Math.Max(0, c.TotalRecipients - c.SentCount - c.FailedCount)
            } : null
        };
    }

    internal static string GetMediaTypeFromUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return "document";
        var ext = System.IO.Path.GetExtension(url).Split('?')[0].ToLower();
        if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".gif" || ext == ".webp")
            return "image";
        if (ext == ".mp4" || ext == ".avi" || ext == ".mov" || ext == ".mkv" || ext == ".3gp")
            return "video";
        return "document";
    }

    /// <summary>
    /// Builds a campaign from a previously validated CSV.
    ///
    /// <para>
    /// This used to walk the file a row at a time, issuing a <c>SELECT</c> to look the contact up
    /// and a <c>SaveChanges</c> to insert it — two round trips per row, against a Postgres instance
    /// that is a network hop away. At a realistic 40ms per round trip a 10,000-row file needed over
    /// thirteen minutes of pure latency, so the browser gave up long before the import did and the
    /// feature simply appeared not to work. The row count at which it broke was a function of
    /// network latency, which is why it seemed to work on small files and fail without a message on
    /// real ones.
    /// </para>
    /// <para>
    /// It now reads the file as a stream, resolves every contact in batched queries, and inserts in
    /// batches — turning 2N round trips into roughly 2N/1000. The same 10,000-row file costs about
    /// twenty round trips. Memory is bounded by the number of distinct phone numbers rather than by
    /// the file, and nothing is loaded twice.
    /// </para>
    /// </summary>
    public async Task<CsvCampaignCreateResponse> CreateCsvCampaignAsync(CreateCsvCampaignRequest request)
    {
        var normalizedName = request.Name.Trim().ToLower();
        var exists = await _dbContext.Campaigns.IgnoreQueryFilters().AnyAsync(c => !c.IsDeleted && c.Name.ToLower() == normalizedName);
        if (exists)
            throw new InvalidOperationException("The campaign name has already been taken.");

        // The reference is only ever used for its file name, and that name is resolved inside the
        // storage roots — never joined onto a path the caller controls. Private storage first (where
        // validation now puts CSVs), then the legacy public locations for files uploaded earlier.
        var fileName = Path.GetFileName(Uri.TryCreate(request.CsvFileUrl, UriKind.Absolute, out var uri)
            ? uri.LocalPath
            : request.CsvFileUrl);

        var filePath = _fileStorage.ResolvePrivatePath($"csv/{fileName}")
            ?? _fileStorage.ResolvePublicPath($"/uploads/csv/{fileName}")
            ?? _fileStorage.ResolvePublicPath($"/uploads/{fileName}")
            ?? throw new FileNotFoundException($"CSV file not found: {fileName}");

        var channel = ParseChannel(request.Channel);

        Template? template = null;
        EmailTemplate? emailTemplate = null;
        EmailSenderIdentity? senderIdentity = null;

        if (channel == MessageChannel.WhatsApp)
        {
            template = await _dbContext.Templates.FindAsync(request.TemplateId);
            if (template == null)
                throw new KeyNotFoundException("Template not found.");
        }
        else
        {
            (emailTemplate, senderIdentity, request.ConnectionId) = await ResolveEmailTargetsAsync(
                request.EmailTemplateId, request.SenderIdentityId, request.ConnectionId);
        }

        await _accessScope.EnsureConnectionAllowedAsync(request.ConnectionId);

        // ── Read ────────────────────────────────────────────────────────────────────────────────
        // Streamed, and judged by the same helper the validate endpoint used, so the recipient
        // count here cannot differ from the number the user was shown before confirming.
        var rowsByPhone = new Dictionary<string, CsvContactRow>(StringComparer.Ordinal);
        var skippedRows = new List<CsvRowError>();
        CsvColumnMap? map = null;

        await using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
        {
            await foreach (var (rowNumber, fields) in BulkCampaignCsv.ReadRecordsAsync(stream))
            {
                if (map == null)
                {
                    map = BulkCampaignCsv.MapColumns(fields, channel);
                    if (map == null)
                        throw new ArgumentException(channel == MessageChannel.Email
                            ? "An email campaign's file needs a phone column, a name column and an email column. Download the sample file to see the expected format."
                            : "The file needs a phone column and a name column. Download the sample file to see the expected format.");
                    continue;
                }

                if (!BulkCampaignCsv.TryReadRow(fields, rowNumber, map, out var row, out var rowErrors, channel))
                {
                    if (skippedRows.Count < BulkCampaignCsv.MaxReportedErrors)
                        skippedRows.AddRange(rowErrors.Take(BulkCampaignCsv.MaxReportedErrors - skippedRows.Count));
                    continue;
                }

                // The same number twice in one file is one recipient, not two — the first row wins,
                // which is also what the validate pass counted.
                rowsByPhone.TryAdd(row.Phone, row);
            }
        }

        if (rowsByPhone.Count == 0)
            throw new ArgumentException("No valid contacts found in the CSV file.");

        // ── Resolve contacts ────────────────────────────────────────────────────────────────────
        // Chunked because a single IN clause with a hundred thousand parameters is refused by the
        // driver long before Postgres sees it.
        const int LookupChunk = 1000;

        var phones = rowsByPhone.Keys.ToList();
        var contactIdsByPhone = new Dictionary<string, int>(StringComparer.Ordinal);
        var toRestore = new List<Contact>();

        foreach (var chunk in phones.Chunk(LookupChunk))
        {
            // IgnoreQueryFilters mirrors ContactService.CreateAsync's pattern — without it a phone
            // number belonging to a soft-deleted contact is invisible here, and the insert below
            // fails on the unique index instead of restoring the row that already exists.
            var found = await _dbContext.Contacts
                .IgnoreQueryFilters()
                .Where(c => chunk.Contains(c.Phone))
                .ToListAsync();

            foreach (var contact in found)
            {
                contactIdsByPhone[contact.Phone] = contact.Id;
                if (contact.IsDeleted) toRestore.Add(contact);
            }
        }

        // ── Restore ─────────────────────────────────────────────────────────────────────────────
        if (toRestore.Count > 0)
        {
            foreach (var contact in toRestore)
            {
                var row = rowsByPhone[contact.Phone];
                contact.IsDeleted = false;
                contact.IsActive = true;
                contact.Name = row.FullName;
                contact.Type = request.RelationType;
                contact.Source = nameof(ContactSource.Import);
                contact.UpdatedAt = DateTime.UtcNow;
            }

            await _dbContext.SaveChangesAsync();
        }

        // ── Insert what is new ──────────────────────────────────────────────────────────────────
        const int InsertChunk = 500;

        var newRows = rowsByPhone.Values
            .Where(r => !contactIdsByPhone.ContainsKey(r.Phone))
            .ToList();

        foreach (var chunk in newRows.Chunk(InsertChunk))
        {
            var now = DateTime.UtcNow;
            var contacts = chunk.Select(r => new Contact
            {
                Name = r.FullName,
                Phone = r.Phone,
                Email = string.IsNullOrWhiteSpace(r.Email) ? null : r.Email,
                Country = string.IsNullOrWhiteSpace(r.Country) ? null : r.Country,
                Type = request.RelationType,
                Status = nameof(ContactStatus.New),
                Source = nameof(ContactSource.Import),
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            }).ToList();

            _dbContext.Contacts.AddRange(contacts);
            await _dbContext.SaveChangesAsync();

            foreach (var contact in contacts)
            {
                contactIdsByPhone[contact.Phone] = contact.Id;
            }

            // Tracked entities accumulate across chunks and slow every subsequent SaveChanges as
            // the change tracker rescans them. They are saved and will not be touched again.
            _dbContext.ChangeTracker.Clear();
        }

        // Clearing the tracker above detached the template, and the response reads its name.
        var templateName = channel == MessageChannel.WhatsApp ? template!.Name : emailTemplate!.Name;

        // ── Campaign ────────────────────────────────────────────────────────────────────────────
        var campaign = new Campaign
        {
            Name = request.Name,
            Channel = channel,

            // Exactly one of the two template references is set, per channel — the same rule the
            // wizard path follows. A WhatsApp bulk campaign still stores it exactly as before.
            TemplateId = channel == MessageChannel.WhatsApp ? request.TemplateId : null,
            EmailTemplateId = channel == MessageChannel.Email ? request.EmailTemplateId : null,
            // CSV path stays single-select (unlike CreateAsync/UpdateAsync above) — still validated
            // via Enum.Parse (throws on an invalid value), just converted to string to match
            // Campaign.RelationType's type.
            RelationType = await NormalizeRelationTypesAsync(request.RelationType),
            ScheduleType = Enum.Parse<ScheduleType>(request.ScheduleType, true),
            ScheduledAt = request.ScheduledAt,
            Status = Enum.Parse<ScheduleType>(request.ScheduleType, true) == ScheduleType.Immediate ? CampaignStatus.Sending : CampaignStatus.Scheduled,
            TotalRecipients = contactIdsByPhone.Count,
            ConnectionId = request.ConnectionId,
            IsBulkCampaign = true
        };

        if (channel == MessageChannel.Email)
        {
            // Email-only settings live in a 1:1 side table rather than as mostly-null columns on
            // the shared Campaign entity — the same shape the wizard path uses.
            campaign.EmailDetail = new EmailCampaignDetail
            {
                SenderIdentityId = senderIdentity!.Id,
                SubjectOverride = string.IsNullOrWhiteSpace(request.SubjectOverride) ? null : request.SubjectOverride.Trim(),
                ReplyToOverride = string.IsNullOrWhiteSpace(request.ReplyToOverride) ? null : request.ReplyToOverride.Trim(),
                TrackOpens = request.TrackOpens,
                TrackClicks = request.TrackClicks
            };
        }

        if (request.Variables != null)
        {
            foreach (var v in request.Variables)
            {
                campaign.Variables.Add(new CampaignVariable
                {
                    VariableName = v.VariableName,
                    VariableValue = v.VariableValue,
                    MergeField = v.MergeField
                });
            }

            var fileVar = request.Variables.FirstOrDefault(v => v.VariableName.Equals("file", StringComparison.OrdinalIgnoreCase));
            if (fileVar != null && !string.IsNullOrEmpty(fileVar.VariableValue))
            {
                campaign.FileUrl = fileVar.VariableValue;
                campaign.FileName = System.IO.Path.GetFileName(fileVar.VariableValue);
                campaign.FileType = GetMediaTypeFromUrl(fileVar.VariableValue);
            }
        }

        foreach (var contactId in contactIdsByPhone.Values)
        {
            campaign.CampaignContacts.Add(new CampaignContact
            {
                ContactId = contactId,
                Status = MessageStatus.Pending
            });
        }

        _dbContext.Campaigns.Add(campaign);
        await _dbContext.SaveChangesAsync();

        // Separate event from Campaign.Created: a bulk CSV campaign also creates contacts as a side
        // effect, which is worth being able to find in the trail on its own.
        await _auditService.LogAsync(
            "BulkCampaign.Created", "Data",
            $"Created bulk campaign \"{campaign.Name}\" from CSV \"{fileName}\" with {campaign.TotalRecipients} recipient(s).",
            "Campaign", campaign.Id.ToString());

        // Both channels go through the durable queue, immediate or scheduled. A scheduled bulk
        // campaign used to be left for a poller that only understood WhatsApp, so a scheduled
        // email one was flipped to Sending and then never sent.
        if (campaign.Status is CampaignStatus.Sending or CampaignStatus.Scheduled)
        {
            if (channel == MessageChannel.Email)
            {
                await _emailCampaignDispatcher.SubmitAsync(campaign.Id, requestedByUserId: _currentUser.UserId);
            }
            else
            {
                await StartWhatsAppAsync(campaign);
            }
        }

        var response = new CsvCampaignCreateResponse
        {
            Id = campaign.Id,
            Name = campaign.Name,
            TemplateName = templateName,
            RelationType = campaign.RelationType.ToString(),
            ScheduleType = campaign.ScheduleType.ToString(),
            ScheduledAt = campaign.ScheduledAt,
            Status = campaign.Status.ToString(),
            TotalRecipients = campaign.TotalRecipients,
            CreatedAt = campaign.CreatedAt,
            UpdatedAt = campaign.UpdatedAt,
            SkippedRows = skippedRows
        };

        // The recipients now live in the database; the uploaded list (personal data) has no further
        // use, and keeping copies of customer files around is exactly what data-minimisation rules
        // forbid. Best-effort: a file that cannot be removed is not a reason to fail the campaign.
        try
        {
            if (filePath.StartsWith(_fileStorage.PrivateRoot, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(filePath);
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not delete the imported CSV for campaign {CampaignId}.", campaign.Id);
        }

        return response;
    }

}
