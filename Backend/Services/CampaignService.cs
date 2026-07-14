using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
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

    public CampaignService(AppDbContext dbContext, IWhatsAppService whatsAppService, ILogger<CampaignService> logger, IServiceScopeFactory scopeFactory)
    {
        _dbContext = dbContext;
        _whatsAppService = whatsAppService;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public async Task<PagedResponse<CampaignResponse>> GetAllAsync(PagedRequest request, string? status = null)
    {
        var query = _dbContext.Campaigns.Include(c => c.Template).AsQueryable();

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
            .Include(c => c.Template)
            .Include(c => c.Variables)
            .Include(c => c.CampaignContacts)
                .ThenInclude(cc => cc.Contact)
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

    public async Task<CampaignResponse> CreateAsync(CreateCampaignRequest request)
    {
        // Check for duplicate campaign name
        var normalizedName = request.Name.Trim().ToLower();
        var exists = await _dbContext.Campaigns.AnyAsync(c => c.Name.ToLower() == normalizedName);
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

        if (contactIds.Count == 0)
            throw new ArgumentException("No active contacts found for the selected targets.");

        // Create Campaign
        var campaign = new Campaign
        {
            Name = request.Name,
            TemplateId = request.TemplateId,
            RelationType = Enum.Parse<ContactType>(request.RelationType, true),
            ScheduleType = Enum.Parse<ScheduleType>(request.ScheduleType, true),
            ScheduledAt = request.ScheduledAt,
            Status = Enum.Parse<ScheduleType>(request.ScheduleType, true) == ScheduleType.Immediate ? CampaignStatus.Sending : CampaignStatus.Scheduled,
            TotalRecipients = contactIds.Count
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

        // If Immediate, trigger sending asynchronously (in real app, use message queue)
        if (campaign.ScheduleType == ScheduleType.Immediate)
        {
            _ = Task.Run(() => SendCampaignMessagesAsync(campaign.Id));
        }

        var created = await _dbContext.Campaigns.Include(c => c.Template).FirstOrDefaultAsync(c => c.Id == campaign.Id);
        return MapToResponse(created!);
    }

    public async Task<CampaignResponse> UpdateAsync(int id, CreateCampaignRequest request)
    {
        // Check for duplicate campaign name
        var normalizedName = request.Name.Trim().ToLower();
        var exists = await _dbContext.Campaigns.AnyAsync(c => c.Id != id && c.Name.ToLower() == normalizedName);
        if (exists)
            throw new InvalidOperationException("The campaign name has already been taken.");

        var campaign = await _dbContext.Campaigns
            .Include(c => c.Template)
            .Include(c => c.Variables)
            .Include(c => c.CampaignContacts)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (campaign == null)
            throw new KeyNotFoundException($"Campaign with ID {id} not found.");

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
        campaign.RelationType = Enum.Parse<ContactType>(request.RelationType, true);
        campaign.ScheduleType = scheduleType;
        campaign.ScheduledAt = request.ScheduledAt;
        campaign.Status = scheduleType == ScheduleType.Immediate ? CampaignStatus.Sending : CampaignStatus.Scheduled;
        campaign.TotalRecipients = contactIds.Count;
        campaign.DeliveredCount = 0;
        campaign.ReadCount = 0;
        campaign.FailedCount = 0;

        _dbContext.CampaignVariables.RemoveRange(campaign.Variables);
        campaign.Variables.Clear();
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

        if (scheduleType == ScheduleType.Immediate)
        {
            _ = Task.Run(() => SendCampaignMessagesAsync(campaign.Id));
        }

        var updated = await _dbContext.Campaigns.Include(c => c.Template).FirstOrDefaultAsync(c => c.Id == campaign.Id);
        return MapToResponse(updated!);
    }

    public async Task DeleteAsync(int id)
    {
        var campaign = await _dbContext.Campaigns.FindAsync(id);
        if (campaign == null)
            throw new KeyNotFoundException($"Campaign with ID {id} not found.");

        if (campaign.Status is not (CampaignStatus.Draft or CampaignStatus.Failed or CampaignStatus.Cancelled))
            throw new InvalidOperationException("Can only delete campaigns in Draft, Failed, or Cancelled status.");

        _dbContext.Campaigns.Remove(campaign);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<CampaignResponse> CancelAsync(int id)
    {
        var campaign = await _dbContext.Campaigns.Include(c => c.Template).FirstOrDefaultAsync(c => c.Id == id);
        if (campaign == null)
            throw new KeyNotFoundException($"Campaign with ID {id} not found.");

        if (campaign.Status != CampaignStatus.Scheduled)
            throw new InvalidOperationException("Can only cancel a Scheduled campaign.");

        campaign.Status = CampaignStatus.Cancelled;
        await _dbContext.SaveChangesAsync();

        return MapToResponse(campaign);
    }

    public async Task<CampaignResponse> PauseAsync(int id)
    {
        var campaign = await _dbContext.Campaigns.Include(c => c.Template).FirstOrDefaultAsync(c => c.Id == id);
        if (campaign == null)
            throw new KeyNotFoundException($"Campaign with ID {id} not found.");

        if (campaign.Status == CampaignStatus.Paused)
            return MapToResponse(campaign);

        if (campaign.Status != CampaignStatus.Scheduled)
            throw new InvalidOperationException("Your Campaign is already executed");

        campaign.Status = CampaignStatus.Paused;
        await _dbContext.SaveChangesAsync();

        return MapToResponse(campaign);
    }

    public async Task<CampaignResponse> ResumeAsync(int id)
    {
        var campaign = await _dbContext.Campaigns.Include(c => c.Template).FirstOrDefaultAsync(c => c.Id == id);
        if (campaign == null)
            throw new KeyNotFoundException($"Campaign with ID {id} not found.");

        if (campaign.Status != CampaignStatus.Paused)
            throw new InvalidOperationException("Only paused campaigns can be resumed.");

        if (campaign.ScheduleType == ScheduleType.Scheduled && campaign.ScheduledAt.HasValue && campaign.ScheduledAt > DateTime.UtcNow)
        {
            campaign.Status = CampaignStatus.Scheduled;
            await _dbContext.SaveChangesAsync();
        }
        else
        {
            campaign.Status = CampaignStatus.Sending;
            await _dbContext.SaveChangesAsync();
            _ = Task.Run(() => SendCampaignMessagesAsync(campaign.Id));
        }

        return MapToResponse(campaign);
    }

    public async Task<PagedResponse<CampaignRecipientResponse>> GetRecipientsAsync(int campaignId, PagedRequest request)
    {
        var campaign = await _dbContext.Campaigns
            .Include(c => c.Template)
            .Include(c => c.Variables)
            .FirstOrDefaultAsync(c => c.Id == campaignId);

        if (campaign == null)
            throw new KeyNotFoundException($"Campaign with ID {campaignId} not found.");

        var query = _dbContext.CampaignContacts
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
            .Where(c => c.Status == CampaignStatus.Scheduled && c.ScheduledAt <= DateTime.UtcNow)
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
                .Include(c => c.Template)
                .Include(c => c.Variables)
                .Include(c => c.CampaignContacts)
                    .ThenInclude(cc => cc.Contact)
                .FirstOrDefaultAsync(c => c.Id == campaignId);

            if (campaign == null || campaign.Status != CampaignStatus.Sending) return;

            foreach (var cc in campaign.CampaignContacts)
            {
                if (cc.Contact == null)
                {
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

                var previewText = BuildRecipientMessagePreview(campaign, cc);
                var chatMessage = await chatService.CreateOrUpdateCampaignMessageAsync(campaign, cc, previewText);

                // Send via WhatsApp API
                var sendResult = await whatsAppService.SendTemplateMessageWithResultAsync(
                    cc.Contact.Phone, 
                    campaign.Template.Name, 
                    campaign.Template.Language, 
                    messageVars);

                if (sendResult.Success && !string.IsNullOrWhiteSpace(sendResult.MessageId))
                {
                    cc.WhatsAppMessageId = sendResult.MessageId;
                    cc.Status = MessageStatus.Sent;
                    await chatService.MarkCampaignMessageSentAsync(chatMessage.Id, sendResult.MessageId);
                    // Note: Status will be updated to Sent/Delivered/Read via webhook
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

            campaign.Status = CampaignStatus.Sent;
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

        return contactIds;
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
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt
        };
    }
}
