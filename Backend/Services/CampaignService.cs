using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.Campaigns;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services;

public class CampaignService : ICampaignService
{
    private readonly AppDbContext _dbContext;
    private readonly IWhatsAppService _whatsAppService;
    private readonly ILogger<CampaignService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    private readonly IAuditService _auditService;

    public CampaignService(
        AppDbContext dbContext,
        IWhatsAppService whatsAppService,
        ILogger<CampaignService> logger,
        IServiceScopeFactory scopeFactory,
        IAuditService auditService)
    {
        _dbContext = dbContext;
        _whatsAppService = whatsAppService;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _auditService = auditService;
    }

    public async Task<PagedResponse<CampaignResponse>> GetAllAsync(PagedRequest request, string? status = null)
    {
        var query = _dbContext.Campaigns.AsNoTracking().Include(c => c.Template).Include(c => c.Connection).AsQueryable();

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<CampaignStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(c => c.Status == parsedStatus);
        }

        if (!string.IsNullOrEmpty(request.Search))
        {
            var search = request.Search.ToLower();
            query = query.Where(c => c.Name.ToLower().Contains(search));
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
            .Include(c => c.Template).Include(c => c.Connection)
            .Include(c => c.Variables)
            .Include(c => c.CampaignContacts)
                .ThenInclude(cc => cc.Contact)
            // Two collections at the same level: every variable would be repeated once per
            // recipient. One of the few places where the extra round trip is worth it.
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == id);

        if (campaign == null)
            throw new KeyNotFoundException($"Campaign with ID {id} not found.");

        var response = new CampaignDetailResponse
        {
            Id = campaign.Id,
            Name = campaign.Name,
            TemplateName = campaign.Template.Name,
            RelationType = campaign.RelationType.ToString(),
            ScheduleType = campaign.ScheduleType.ToString(),
            ScheduledAt = campaign.ScheduledAt,
            Status = campaign.Status.ToString(),
            TotalRecipients = campaign.TotalRecipients,
            DeliveredCount = campaign.DeliveredCount,
            ReadCount = campaign.ReadCount,
            FailedCount = campaign.FailedCount,
            CreatedBy = campaign.CreatedBy,
            IsDeleted = campaign.IsDeleted,
            DeletedAt = campaign.DeletedAt,
            DeletedBy = campaign.DeletedBy,
            CreatedAt = campaign.CreatedAt,
            UpdatedAt = campaign.UpdatedAt,
            Recipients = campaign.CampaignContacts.Select(cc => new CampaignRecipientResponse
            {
                Id = cc.Id,
                ContactId = cc.ContactId,
                ContactName = cc.Contact.Name,
                Phone = cc.Contact.Phone,
                Message = BuildRecipientMessagePreview(campaign, cc),
                Status = cc.Status.ToString(),
                SentAt = cc.SentAt,
                DeliveredAt = cc.DeliveredAt,
                ReadAt = cc.ReadAt,
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

        var template = await _dbContext.Templates.FindAsync(request.TemplateId);
        if (template == null)
            throw new KeyNotFoundException("Template not found.");
        if (template.Status != TemplateStatus.Approved)
            throw new InvalidOperationException("Can only use APPROVED templates for campaigns.");

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
            TemplateId = request.TemplateId,
            RelationType = NormalizeRelationTypes(request.RelationType),
            ScheduleType = Enum.Parse<ScheduleType>(request.ScheduleType, true),
            ScheduledAt = request.ScheduledAt,
            Status = Enum.Parse<ScheduleType>(request.ScheduleType, true) == ScheduleType.Immediate ? CampaignStatus.Sending : CampaignStatus.Scheduled,
            TotalRecipients = contactIds.Count,
            ConnectionId = request.ConnectionId
        };

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

        // If Immediate, trigger sending asynchronously (in real app, use message queue)
        if (campaign.ScheduleType == ScheduleType.Immediate)
        {
            _ = Task.Run(() => SendCampaignMessagesAsync(campaign.Id));
        }

        var created = await _dbContext.Campaigns.Include(c => c.Template).Include(c => c.Connection).FirstOrDefaultAsync(c => c.Id == campaign.Id);
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
            .Include(c => c.Template).Include(c => c.Connection)
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

        var updated = await _dbContext.Campaigns.Include(c => c.Template).Include(c => c.Connection).FirstOrDefaultAsync(c => c.Id == campaign.Id);
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
        var campaign = await _dbContext.Campaigns.IgnoreQueryFilters().Include(c => c.Template).Include(c => c.Connection).FirstOrDefaultAsync(c => c.Id == id);
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
        var campaign = await _dbContext.Campaigns.IgnoreQueryFilters().Include(c => c.Template).Include(c => c.Connection).FirstOrDefaultAsync(c => c.Id == id);
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
        var campaign = await _dbContext.Campaigns.IgnoreQueryFilters().Include(c => c.Template).Include(c => c.Connection).FirstOrDefaultAsync(c => c.Id == id);
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
            .Include(c => c.Template).Include(c => c.Connection)
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
                Message = chatMessageTextByContact.TryGetValue(cc.Id, out var messageText) && !string.IsNullOrWhiteSpace(messageText)
                    ? messageText
                    : BuildRecipientMessagePreview(campaign, cc),
                Status = cc.Status.ToString(),
                SentAt = cc.SentAt,
                DeliveredAt = cc.DeliveredAt,
                ReadAt = cc.ReadAt,
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
                .Include(c => c.Template).Include(c => c.Connection)
                .Include(c => c.Variables)
                .Include(c => c.CampaignContacts)
                    .ThenInclude(cc => cc.Contact)
                // Same two-collection shape as GetByIdAsync, and this runs in the background
                // where an extra round trip costs nothing.
                .AsSplitQuery()
                .FirstOrDefaultAsync(c => c.Id == campaignId);

            if (campaign == null || campaign.Status != CampaignStatus.Sending) return;

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
                var hasMediaHeader = campaign.Template.HeaderType != HeaderType.None;

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
                    campaign.Template.Name,
                    campaign.Template.Language,
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

        var text = BuildCampaignMessagePreview(campaign.Template.BodyText, messageVars);
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

    private static CampaignResponse MapToResponse(Campaign c)
    {
        return new CampaignResponse
        {
            Id = c.Id,
            Name = c.Name,
            TemplateName = c.Template.Name,
            RelationType = c.RelationType.ToString(),
            ScheduleType = c.ScheduleType.ToString(),
            ScheduledAt = c.ScheduledAt,
            Status = c.Status.ToString(),
            TotalRecipients = c.TotalRecipients,
            DeliveredCount = c.DeliveredCount,
            ReadCount = c.ReadCount,
            FailedCount = c.FailedCount,
            CreatedBy = c.CreatedBy,
            IsDeleted = c.IsDeleted,
            DeletedAt = c.DeletedAt,
            DeletedBy = c.DeletedBy,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt,
            IsBulkCampaign = c.IsBulkCampaign,
            ConnectionId = c.ConnectionId,
            ConnectionName = c.Connection?.Name,
            ConnectionNickname = c.Connection?.Nickname
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

        var lines = await File.ReadAllLinesAsync(filePath);
        if (lines.Length < 2)
            throw new ArgumentException("CSV file must contain a header row and at least one data row.");

        var headers = CsvHelper.SplitCsvRow(lines[0]).Select(h => h.ToLower().Trim()).ToList();
        
        int phoneIdx = headers.FindIndex(h => h == "phone" || h == "phoneno" || h == "phone number" || h == "telephone");
        int firstNameIdx = headers.FindIndex(h => h == "firstname" || h == "first name" || h == "name");
        int lastNameIdx = headers.FindIndex(h => h == "lastname" || h == "last name");
        int emailIdx = headers.FindIndex(h => h == "email" || h == "email address");
        int countryIdx = headers.FindIndex(h => h == "country");

        if (phoneIdx == -1)
            throw new ArgumentException("cannot upload wrong format csv file (Missing phone column)");
        if (firstNameIdx == -1)
            throw new ArgumentException("cannot upload wrong format csv file (Missing name/firstname column)");

        var contactIds = new HashSet<int>();
        var skippedRows = new List<CsvRowError>();

        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            var rowNumber = i + 1;
            var fields = CsvHelper.SplitCsvRow(line);
            if (fields.Count <= Math.Max(phoneIdx, firstNameIdx))
            {
                skippedRows.Add(new CsvRowError { RowNumber = rowNumber, Column = null, Value = line, Reason = "Row has fewer columns than the header row." });
                continue;
            }

            var phoneVal = fields[phoneIdx].Trim();
            // Must stay byte-for-byte equivalent to the validation pass in
            // CampaignsController.ValidateCsv — if the two disagree, csv-validate reports N valid
            // rows and csv-create silently builds a campaign with fewer recipients. Both now call
            // the same helper, which is the only way to keep them honest.
            if (!PhoneNumberHelper.TryNormalize(phoneVal, out var cleanedPhone, out var phoneFailure))
            {
                skippedRows.Add(new CsvRowError { RowNumber = rowNumber, Column = "phone", Value = phoneVal, Reason = phoneFailure! });
                continue;
            }

            var firstName = fields[firstNameIdx].Trim();
            var lastName = lastNameIdx != -1 && lastNameIdx < fields.Count ? fields[lastNameIdx].Trim() : string.Empty;
            var emailVal = emailIdx != -1 && emailIdx < fields.Count ? fields[emailIdx].Trim() : string.Empty;
            var countryVal = countryIdx != -1 && countryIdx < fields.Count ? fields[countryIdx].Trim() : string.Empty;

            var fullName = string.IsNullOrEmpty(lastName) ? firstName : $"{firstName} {lastName}".Trim();
            if (fullName.Length < 2)
            {
                fullName = "CSV User";
            }

            // IgnoreQueryFilters + restore-if-soft-deleted mirrors ContactService.CreateAsync's pattern —
            // without this, a phone number reused from a previously soft-deleted contact is invisible to
            // the filtered lookup below, causing a duplicate-key DbUpdateException on insert.
            var contact = await _dbContext.Contacts.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Phone == cleanedPhone);
            if (contact == null)
            {
                contact = new Contact
                {
                    Name = fullName,
                    Phone = cleanedPhone,
                    Type = request.RelationType,
                    Status = nameof(ContactStatus.New),
                    Source = nameof(ContactSource.Import),
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _dbContext.Contacts.Add(contact);
                await _dbContext.SaveChangesAsync();
            }
            else if (contact.IsDeleted)
            {
                contact.IsDeleted = false;
                contact.IsActive = true;
                contact.Name = fullName;
                contact.Type = request.RelationType;
                contact.Source = nameof(ContactSource.Import);
                contact.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
            }

            contactIds.Add(contact.Id);
        }

        var template = await _dbContext.Templates.FindAsync(request.TemplateId);
        if (template == null)
            throw new KeyNotFoundException("Template not found.");

        if (contactIds.Count == 0)
            throw new ArgumentException("No valid contacts found in the CSV file.");

        var campaign = new Campaign
        {
            Name = request.Name,
            TemplateId = request.TemplateId,
            // CSV path stays single-select (unlike CreateAsync/UpdateAsync above) — still
            // validated via Enum.Parse (throws on an invalid value), just converted to
            // string to match Campaign.RelationType's new type.
            RelationType = Enum.Parse<ContactType>(request.RelationType, true).ToString(),
            ScheduleType = Enum.Parse<ScheduleType>(request.ScheduleType, true),
            ScheduledAt = request.ScheduledAt,
            Status = Enum.Parse<ScheduleType>(request.ScheduleType, true) == ScheduleType.Immediate ? CampaignStatus.Sending : CampaignStatus.Scheduled,
            TotalRecipients = contactIds.Count,
            ConnectionId = request.ConnectionId,
            IsBulkCampaign = true
        };

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

        // Separate event from Campaign.Created: a bulk CSV campaign also creates contacts as a
        // side effect, which is worth being able to find in the trail on its own.
        await _auditService.LogAsync(
            "BulkCampaign.Created", "Data",
            $"Created bulk campaign \"{campaign.Name}\" from CSV \"{fileName}\" with {contactIds.Count} recipient(s).",
            "Campaign", campaign.Id.ToString());

        if (campaign.Status == CampaignStatus.Sending)
        {
            _ = Task.Run(() => SendCampaignMessagesAsync(campaign.Id));
        }

        var response = new CsvCampaignCreateResponse
        {
            Id = campaign.Id,
            Name = campaign.Name,
            TemplateName = template.Name,
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
