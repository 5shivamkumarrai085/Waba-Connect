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

    // Email-channel collaborators. Additive: the WhatsApp path never touches either of them, and
    // campaign creation branches on channel before reaching them.
    private readonly IEmailCampaignDispatcher _emailCampaignDispatcher;
    private readonly IEmailDomainService _emailDomainService;

    public CampaignService(
        AppDbContext dbContext,
        IWhatsAppService whatsAppService,
        ILogger<CampaignService> logger,
        IServiceScopeFactory scopeFactory,
        IAuditService auditService,
        IEmailCampaignDispatcher emailCampaignDispatcher,
        IEmailDomainService emailDomainService)
    {
        _dbContext = dbContext;
        _whatsAppService = whatsAppService;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _auditService = auditService;
        _emailCampaignDispatcher = emailCampaignDispatcher;
        _emailDomainService = emailDomainService;
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

        var (canSend, reason) = await _emailDomainService.CanSenderSendAsync(senderId);
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

    public async Task<PagedResponse<CampaignResponse>> GetAllAsync(PagedRequest request, string? status = null)
    {
        var query = _dbContext.Campaigns.AsNoTracking().Include(c => c.Template).Include(c => c.EmailTemplate).Include(c => c.Connection).AsQueryable();

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<CampaignStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(c => c.Status == parsedStatus);
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
        var items = await query
            .OrderByDescending(c => c.Id)
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
        var campaign = await _dbContext.Campaigns
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(c => c.Template).Include(c => c.EmailTemplate).Include(c => c.Connection)
            .Include(c => c.Variables)
            .Include(c => c.CampaignContacts)
                .ThenInclude(cc => cc.Contact)
            // Two collections at the same level: every variable would be repeated once per
            // recipient. One of the few places where the extra round trip is worth it.
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == id);

        if (campaign == null)
            throw new KeyNotFoundException($"Campaign with ID {id} not found.");

        var isEmail = campaign.Channel == MessageChannel.Email;

        var response = new CampaignDetailResponse
        {
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
            EmailStats = isEmail ? new EmailCampaignStatsResponse
            {
                Sent         = campaign.SentCount,
                Delivered    = campaign.DeliveredCount,
                Failed       = campaign.FailedCount,
                Bounced      = 0,
                Complained   = campaign.ComplainedCount,
                Suppressed   = 0,
                Opened       = campaign.OpenedCount,
                Clicked      = campaign.ClickedCount,
                Replied      = campaign.RepliedCount,
                Unsubscribed = campaign.UnsubscribedCount,
                Pending      = Math.Max(0, campaign.TotalRecipients - campaign.SentCount - campaign.FailedCount)
            } : null,
            Recipients = campaign.CampaignContacts.Select(cc => new CampaignRecipientResponse
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

        return response;
    }

    // Splits, validates, and re-normalizes a comma-separated RelationType string (e.g.
    // "lead, customer" -> "Lead,Customer") for the normal (non-CSV) campaign create/update
    // path, which now supports targeting multiple relation types per campaign.
    private static string NormalizeRelationTypes(string relationType)
    {
        var tokens = relationType.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var normalized = new List<string>();
        foreach (var token in tokens)
        {
            if (!Enum.TryParse<ContactType>(token, true, out var parsed))
                throw new ArgumentException($"Invalid RelationType value: '{token}'.");
            normalized.Add(parsed.ToString());
        }
        if (normalized.Count == 0)
            throw new ArgumentException("RelationType is required.");
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

        // Resolve Contacts
        var contactIds = new HashSet<int>();
        
        if (request.ContactIds != null)
        {
            foreach (var cid in request.ContactIds) contactIds.Add(cid);
        }

        if (request.GroupIds != null && request.GroupIds.Any())
        {
            var groupContacts = await _dbContext.ContactGroupMembers
                .Where(gm => request.GroupIds.Contains(gm.GroupId))
                .Select(gm => gm.ContactId)
                .ToListAsync();
                
            foreach (var cid in groupContacts) contactIds.Add(cid);
        }

        var activeContactIds = await _dbContext.Contacts
            .Where(c => contactIds.Contains(c.Id) && c.IsActive && !c.IsDeleted)
            .Select(c => c.Id)
            .ToListAsync();

        contactIds = new HashSet<int>(activeContactIds);

        if (contactIds.Count == 0)
            throw new ArgumentException("No active contacts found for the selected targets.");

        // Create Campaign
        var campaign = new Campaign
        {
            Name = request.Name,
            Channel = channel,

            // Exactly one of the two template references is set, per channel. TemplateId became
            // nullable for this; a WhatsApp campaign still stores it exactly as before.
            TemplateId = channel == MessageChannel.WhatsApp ? request.TemplateId : null,
            EmailTemplateId = channel == MessageChannel.Email ? request.EmailTemplateId : null,

            RelationType = NormalizeRelationTypes(request.RelationType),
            ScheduleType = Enum.Parse<ScheduleType>(request.ScheduleType, true),
            ScheduledAt = request.ScheduledAt,
            Status = Enum.Parse<ScheduleType>(request.ScheduleType, true) == ScheduleType.Immediate ? CampaignStatus.Sending : CampaignStatus.Scheduled,
            TotalRecipients = contactIds.Count,
            ConnectionId = request.ConnectionId
        };

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

        // Logged before the send is kicked off: SendCampaignMessagesAsync runs on a background
        // task with its own scope, and this scoped DbContext may be disposed by the time it
        // finishes.
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
            await _emailCampaignDispatcher.SubmitAsync(campaign.Id, requestedByUserId: null);
        }
        else if (campaign.ScheduleType == ScheduleType.Immediate)
        {
            // Unchanged WhatsApp path. Still fire-and-forget, still recovered by the existing
            // scheduler — deliberately untouched, since migrating it onto the queue would change
            // behaviour this work is required not to change.
            _ = Task.Run(() => SendCampaignMessagesAsync(campaign.Id));
        }

        var created = await _dbContext.Campaigns.Include(c => c.Template).Include(c => c.EmailTemplate).Include(c => c.Connection).FirstOrDefaultAsync(c => c.Id == campaign.Id);
        return MapToResponse(created!);
    }

    public async Task<CampaignResponse> UpdateAsync(int id, CreateCampaignRequest request)
    {
        // Check for duplicate campaign name
        var normalizedName = request.Name.Trim().ToLower();
        var exists = await _dbContext.Campaigns.IgnoreQueryFilters().AnyAsync(c => c.Id != id && !c.IsDeleted && c.Name.ToLower() == normalizedName);
        if (exists)
            throw new InvalidOperationException("The campaign name has already been taken.");

        var campaign = await _dbContext.Campaigns
            .IgnoreQueryFilters()
            .Include(c => c.Template).Include(c => c.EmailTemplate).Include(c => c.Connection)
            .Include(c => c.Variables)
            .Include(c => c.CampaignContacts)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (campaign == null)
            throw new KeyNotFoundException($"Campaign with ID {id} not found.");

        if (campaign.IsDeleted)
            throw new InvalidOperationException("Deleted campaigns cannot be edited or rescheduled.");

        if (campaign.Status is CampaignStatus.Sending or CampaignStatus.Sent or CampaignStatus.Cancelled)
            throw new InvalidOperationException("Your Campaign is already executed");

        var template = await _dbContext.Templates.FindAsync(request.TemplateId);
        if (template == null)
            throw new KeyNotFoundException("Template not found.");
        if (template.Status != TemplateStatus.Approved)
            throw new InvalidOperationException("Can only use APPROVED templates for campaigns.");

        var contactIds = await ResolveTargetContactIdsAsync(request);
        if (contactIds.Count == 0)
            throw new ArgumentException("No active contacts found for the selected targets.");

        var scheduleType = Enum.Parse<ScheduleType>(request.ScheduleType, true);

        campaign.Name = request.Name;
        campaign.TemplateId = request.TemplateId;
        campaign.RelationType = NormalizeRelationTypes(request.RelationType);
        campaign.ScheduleType = scheduleType;
        campaign.ScheduledAt = request.ScheduledAt;
        campaign.Status = scheduleType == ScheduleType.Immediate ? CampaignStatus.Sending : CampaignStatus.Scheduled;
        campaign.TotalRecipients = contactIds.Count;
        campaign.DeliveredCount = 0;
        campaign.ReadCount = 0;
        campaign.FailedCount = 0;

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

        _dbContext.CampaignContacts.RemoveRange(campaign.CampaignContacts);
        campaign.CampaignContacts.Clear();
        foreach (var contactId in contactIds)
        {
            campaign.CampaignContacts.Add(new CampaignContact
            {
                ContactId = contactId,
                Status = MessageStatus.Pending
            });
        }

        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            "Campaign.Updated", "Data",
            $"Updated campaign \"{campaign.Name}\".",
            "Campaign", campaign.Id.ToString());

        if (scheduleType == ScheduleType.Immediate)
        {
            _ = Task.Run(() => SendCampaignMessagesAsync(campaign.Id));
        }

        var updated = await _dbContext.Campaigns.Include(c => c.Template).Include(c => c.EmailTemplate).Include(c => c.Connection).FirstOrDefaultAsync(c => c.Id == campaign.Id);
        return MapToResponse(updated!);
    }

    public async Task DeleteAsync(int id)
    {
        var campaign = await _dbContext.Campaigns
            .IgnoreQueryFilters()
            .Include(c => c.CampaignContacts)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (campaign == null)
            throw new KeyNotFoundException($"Campaign with ID {id} not found.");

        _logger.LogInformation("Soft deleting campaign {CampaignId} ({CampaignName}) for audit.", campaign.Id, campaign.Name);

        campaign.IsDeleted = true;
        campaign.DeletedAt = DateTime.UtcNow;
        campaign.DeletedBy = "Admin";
        campaign.Status = CampaignStatus.Cancelled;

        // Cancel any remaining pending recipients
        var cancelledCount = campaign.CampaignContacts.Count(cc => cc.Status == MessageStatus.Pending);
        foreach (var cc in campaign.CampaignContacts.Where(cc => cc.Status == MessageStatus.Pending))
        {
            cc.Status = MessageStatus.Failed;
            cc.ErrorMessage = "Campaign cancelled due to deletion.";
        }

        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            "Campaign.Deleted", "Data",
            cancelledCount > 0
                ? $"Deleted campaign \"{campaign.Name}\"; {cancelledCount} pending recipient(s) cancelled."
                : $"Deleted campaign \"{campaign.Name}\".",
            "Campaign", campaign.Id.ToString());
    }

    public async Task<CampaignResponse> CancelAsync(int id)
    {
        var campaign = await _dbContext.Campaigns.IgnoreQueryFilters().Include(c => c.Template).Include(c => c.EmailTemplate).Include(c => c.Connection).FirstOrDefaultAsync(c => c.Id == id);
        if (campaign == null)
            throw new KeyNotFoundException($"Campaign with ID {id} not found.");

        if (campaign.IsDeleted)
            throw new InvalidOperationException("Deleted campaigns cannot be edited or rescheduled.");

        if (campaign.Status != CampaignStatus.Scheduled)
            throw new InvalidOperationException("Can only cancel a Scheduled campaign.");

        campaign.Status = CampaignStatus.Cancelled;
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            "Campaign.Cancelled", "Data",
            $"Cancelled scheduled campaign \"{campaign.Name}\".",
            "Campaign", campaign.Id.ToString());

        return MapToResponse(campaign);
    }

    public async Task<CampaignResponse> PauseAsync(int id)
    {
        var campaign = await _dbContext.Campaigns.IgnoreQueryFilters().Include(c => c.Template).Include(c => c.EmailTemplate).Include(c => c.Connection).FirstOrDefaultAsync(c => c.Id == id);
        if (campaign == null)
            throw new KeyNotFoundException($"Campaign with ID {id} not found.");

        if (campaign.IsDeleted)
            throw new InvalidOperationException("Deleted campaigns cannot be edited or rescheduled.");

        if (campaign.Status == CampaignStatus.Paused)
            return MapToResponse(campaign);

        if (campaign.Status != CampaignStatus.Scheduled)
            throw new InvalidOperationException("Your Campaign is already executed");

        campaign.Status = CampaignStatus.Paused;
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            "Campaign.Paused", "Data",
            $"Paused campaign \"{campaign.Name}\".",
            "Campaign", campaign.Id.ToString());

        return MapToResponse(campaign);
    }

    public async Task<CampaignResponse> ResumeAsync(int id)
    {
        var campaign = await _dbContext.Campaigns.IgnoreQueryFilters().Include(c => c.Template).Include(c => c.EmailTemplate).Include(c => c.Connection).FirstOrDefaultAsync(c => c.Id == id);
        if (campaign == null)
            throw new KeyNotFoundException($"Campaign with ID {id} not found.");

        if (campaign.IsDeleted)
            throw new InvalidOperationException("Deleted campaigns cannot be edited or rescheduled.");

        if (campaign.Status != CampaignStatus.Paused)
            throw new InvalidOperationException("Only paused campaigns can be resumed.");

        if (campaign.ScheduleType == ScheduleType.Scheduled && campaign.ScheduledAt.HasValue && campaign.ScheduledAt > DateTime.UtcNow)
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

            _ = Task.Run(() => SendCampaignMessagesAsync(campaign.Id));
        }

        return MapToResponse(campaign);
    }

    public async Task<PagedResponse<CampaignRecipientResponse>> GetRecipientsAsync(int campaignId, PagedRequest request)
    {
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

    public async Task ProcessScheduledCampaignsAsync(CancellationToken cancellationToken)
    {
        var campaignsToProcess = await _dbContext.Campaigns
            .IgnoreQueryFilters()
            .Where(c => !c.IsDeleted && c.Status == CampaignStatus.Scheduled && c.ScheduledAt <= DateTime.UtcNow)
            .ToListAsync(cancellationToken);

        foreach (var campaign in campaignsToProcess)
        {
            campaign.Status = CampaignStatus.Sending;
            await _dbContext.SaveChangesAsync(cancellationToken);
            
            _ = Task.Run(() => SendCampaignMessagesAsync(campaign.Id), cancellationToken);
        }
    }

    private async Task SendCampaignMessagesAsync(int campaignId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var whatsAppService = scope.ServiceProvider.GetRequiredService<IWhatsAppService>();
            var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();

            var campaign = await dbContext.Campaigns
                .IgnoreQueryFilters()
                // No EmailTemplate include here, unlike the read paths: this is the WhatsApp
                // sender, it rejects any other channel below, and an extra left join on the
                // hottest query in the send loop buys nothing.
                .Include(c => c.Template).Include(c => c.Connection)
                .Include(c => c.Variables)
                .Include(c => c.CampaignContacts)
                    .ThenInclude(cc => cc.Contact)
                // Same two-collection shape as GetByIdAsync, and this runs in the background
                // where an extra round trip costs nothing.
                .AsSplitQuery()
                .FirstOrDefaultAsync(c => c.Id == campaignId);

            if (campaign == null || campaign.Status != CampaignStatus.Sending) return;

            // This method is the WhatsApp sender and nothing else. Email campaigns are expanded
            // into queued jobs and delivered by the email workers, so they must never reach here
            // — and if one ever did, it has no Meta template to send. Guarding at the top keeps
            // that a clear no-op instead of a null dereference three loops down.
            if (campaign.Channel != MessageChannel.WhatsApp)
            {
                _logger.LogWarning(
                    "Campaign {CampaignId} is on the {Channel} channel and was routed to the WhatsApp sender. Ignoring.",
                    campaignId, campaign.Channel);
                return;
            }

            // Captured once so the null-state survives the awaits inside the loop below. A
            // WhatsApp campaign cannot be created without an approved template, so a null here
            // means the template row was deleted underneath a sending campaign.
            var template = campaign.Template;
            if (template == null)
            {
                _logger.LogError(
                    "Campaign {CampaignId} has no template and cannot be sent. Marking it failed.",
                    campaignId);
                campaign.Status = CampaignStatus.Failed;
                await dbContext.SaveChangesAsync();
                return;
            }

            foreach (var cc in campaign.CampaignContacts)
            {
                await dbContext.Entry(campaign).ReloadAsync();
                if (campaign.Status == CampaignStatus.Cancelled || campaign.IsDeleted)
                {
                    _logger.LogInformation("Campaign {CampaignId} has been cancelled or deleted. Stopping message sending.", campaignId);
                    break;
                }

                // Check daily limit for campaign connection
                if (campaign.ConnectionId.HasValue)
                {
                    var todayUtc = DateTime.UtcNow.Date;
                    int sentToday = await dbContext.ChatMessages
                        .CountAsync(m => m.ConnectionId == campaign.ConnectionId.Value && m.Direction == ChatMessageDirection.Outgoing && (m.IsTemplate || m.CampaignContactId != null) && m.CreatedAt >= todayUtc);

                    var phone = await dbContext.WabaPhoneNumbers.FirstOrDefaultAsync(p => p.ConnectionId == campaign.ConnectionId.Value);
                    int limit = 1000;
                    if (phone != null && int.TryParse(phone.MessageLimit, out int customLimit) && customLimit > 0)
                    {
                        limit = customLimit;
                    }

                    if (sentToday >= limit)
                    {
                        _logger.LogWarning("Campaign {CampaignId} stopped: Daily message limit reached ({SentToday}/{Limit}) for connection {ConnectionId}.", campaignId, sentToday, limit, campaign.ConnectionId);
                        cc.Status = MessageStatus.Failed;
                        cc.ErrorMessage = $"Daily message limit reached ({sentToday}/{limit}) for this connection.";
                        campaign.FailedCount++;
                        // Don't set campaign.Status here — let the end-of-loop aggregation
                        // (below) decide Sent/PartiallyFailed/Failed from what actually
                        // happened. Contacts already sent before the limit was hit make
                        // this a partial failure, not a blanket failure.
                        await dbContext.SaveChangesAsync();
                        break;
                    }
                }

                if (cc.Contact == null)
                {
                    continue;
                }

                if (!cc.Contact.IsActive || cc.Contact.IsDeleted)
                {
                    cc.Status = MessageStatus.Failed;
                    cc.ErrorMessage = "Contact is inactive or deleted.";
                    campaign.FailedCount++;
                    await dbContext.SaveChangesAsync();
                    continue;
                }
                // Process merge fields for this specific contact
                var messageVars = new Dictionary<string, string>();
                foreach (var v in campaign.Variables)
                {
                    string finalValue = v.VariableValue ?? "";
                    if (v.MergeField == "@name") finalValue = cc.Contact.Name;
                    else if (v.MergeField == "@phone") finalValue = cc.Contact.Phone;
                    
                    messageVars[v.VariableName] = finalValue;
                }

                // Check if there is an attachment on the campaign object
                string? attachmentUrl = campaign.FileUrl;
                string? mediaType = null;
                string? mediaFileName = null;
                if (!string.IsNullOrEmpty(attachmentUrl))
                {
                    mediaType = GetMediaTypeFromUrl(attachmentUrl);
                    mediaFileName = campaign.FileName ?? System.IO.Path.GetFileName(attachmentUrl);
                }

                var previewText = BuildRecipientMessagePreview(campaign, cc);

                // Determine template header type
                var hasMediaHeader = template.HeaderType != HeaderType.None;

                // Create the campaign message log
                // If the template has a media header, the media is part of the template, so log it with the template ChatMessage
                // If it does NOT have a media header, we only log the template text in the template ChatMessage
                var chatMessage = await chatService.CreateOrUpdateCampaignMessageAsync(
                    campaign, 
                    cc, 
                    previewText,
                    hasMediaHeader ? attachmentUrl : null,
                    hasMediaHeader ? mediaType : null,
                    hasMediaHeader ? mediaFileName : null);

                // Send via WhatsApp API
                var sendResult = await whatsAppService.SendTemplateMessageWithResultAsync(
                    cc.Contact.Phone,
                    template.Name,
                    template.Language,
                    messageVars,
                    campaign.ConnectionId,
                    // Campaign sends run under CampaignSchedulerService, a hosted service with
                    // no HttpContext — so TriggeredBy is stated explicitly rather than inferred.
                    new MessageSendContext
                    {
                        Category = "Campaign",
                        SourceName = campaign.Name,
                        SourceId = campaign.Id,
                        ContactId = cc.ContactId,
                        RelationType = cc.Contact.Type.ToString(),
                        TriggeredBy = "Scheduler"
                    });

                if (sendResult.Success && !string.IsNullOrWhiteSpace(sendResult.MessageId))
                {
                    cc.WhatsAppMessageId = sendResult.MessageId;
                    cc.Status = MessageStatus.Sent;
                    cc.SentAt ??= DateTime.UtcNow;
                    await chatService.MarkCampaignMessageSentAsync(chatMessage.Id, sendResult.MessageId);
                    
                    // Case B: No media header in template, but attachment is present
                    // We must send the attachment as a SEPARATE media message!
                    if (!hasMediaHeader && !string.IsNullOrEmpty(attachmentUrl))
                    {
                        // Resolve fromPhoneNumberId for media send
                        string? campaignPhoneNumberId = null;
                        if (campaign.ConnectionId.HasValue)
                        {
                            var phone = await dbContext.WabaPhoneNumbers.FirstOrDefaultAsync(p => p.ConnectionId == campaign.ConnectionId.Value);
                            campaignPhoneNumberId = phone?.PhoneNumberId;
                        }

                        var mediaSendResult = await whatsAppService.SendMediaMessageAsync(
                            cc.Contact.Phone,
                            attachmentUrl,
                            mediaType!,
                            mediaFileName,
                            null,
                            campaignPhoneNumberId);

                        if (mediaSendResult.Success)
                        {
                            // Create a ChatMessage log for the separate media message
                            var conversation = await chatService.GetOrCreateConversationAsync(cc.ContactId, campaign.ConnectionId);
                            var mediaMessage = new ChatMessage
                            {
                                ConversationId = conversation.Id,
                                ContactId = cc.ContactId,
                                Direction = ChatMessageDirection.Outgoing,
                                Status = ChatMessageStatus.Sent,
                                Text = $"[Attachment: {mediaFileName}]",
                                IsTemplate = false,
                                MediaUrl = attachmentUrl,
                                MediaType = mediaType,
                                MediaFileName = mediaFileName,
                                WhatsAppMessageId = mediaSendResult.MessageId
                            };
                            dbContext.ChatMessages.Add(mediaMessage);
                        }
                    }
                }
                else
                {
                    cc.Status = MessageStatus.Failed;
                    cc.ErrorMessage = sendResult.ErrorMessage ?? "Failed to send via WhatsApp Cloud API";
                    campaign.FailedCount++;
                    await chatService.MarkCampaignMessageFailedAsync(chatMessage.Id, cc.ErrorMessage);
                }

                await dbContext.SaveChangesAsync();

                // Add delay to respect rate limits (simple approach)
                await Task.Delay(100); 
            }

            // Aggregate the real outcome instead of unconditionally overwriting Status to
            // Sent — this used to clobber the Failed status the daily-limit branch above
            // had just set, and never accounted for ordinary per-contact send failures at
            // all (both of which produced the "Success" header / "Failed" row mismatch).
            // Anchored on actual per-contact status rather than the full recipient count,
            // so an early break (daily limit hit partway through) is correctly classified:
            // contacts never reached stay Pending and aren't counted as failures.
            var succeededCount = campaign.CampaignContacts.Count(c => c.Status == MessageStatus.Sent);
            campaign.Status = campaign.FailedCount == 0
                ? CampaignStatus.Sent
                : succeededCount == 0
                    ? CampaignStatus.Failed
                    : CampaignStatus.PartiallyFailed;
            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing campaign {CampaignId}", campaignId);
        }
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
        var contactIds = new HashSet<int>();

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

    private static string BuildRecipientMessagePreview(Campaign campaign, CampaignContact campaignContact)
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

    private static string GetMediaTypeFromUrl(string url)
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

        var uri = new Uri(request.CsvFileUrl);
        var fileName = Path.GetFileName(uri.LocalPath);
        var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", fileName);

        if (!File.Exists(filePath))
        {
            var csvFolderFile = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "csv", fileName);
            if (File.Exists(csvFolderFile))
            {
                filePath = csvFolderFile;
            }
            else
            {
                throw new FileNotFoundException($"CSV file not found: {fileName}");
            }
        }

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
            RelationType = Enum.Parse<ContactType>(request.RelationType, true).ToString(),
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

        if (campaign.Status == CampaignStatus.Sending)
        {
            if (channel == MessageChannel.Email)
            {
                // Email goes through the durable queue: the recipients are expanded and sent by
                // workers with retries, backoff and a dead-letter path. The fire-and-forget
                // Task.Run below is the pre-existing WhatsApp behaviour and is left as it was.
                await _emailCampaignDispatcher.SubmitAsync(campaign.Id, requestedByUserId: null);
            }
            else
            {
                _ = Task.Run(() => SendCampaignMessagesAsync(campaign.Id));
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

        return response;
    }

}
