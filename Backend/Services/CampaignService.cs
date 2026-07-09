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
                ContactId = cc.ContactId,
                ContactName = cc.Contact.Name,
                Phone = cc.Contact.Phone,
                Status = cc.Status.ToString(),
                SentAt = cc.SentAt,
                DeliveredAt = cc.DeliveredAt,
                ReadAt = cc.ReadAt,
                ErrorMessage = cc.ErrorMessage
            }).ToList()
        };

        return response;
    }

    public async Task<CampaignResponse> CreateAsync(CreateCampaignRequest request)
    {
        // Validate Template
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

        if (campaign.Status is not (CampaignStatus.Scheduled or CampaignStatus.Sending))
            throw new InvalidOperationException("Can only pause Scheduled or Sending campaigns.");

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
            throw new InvalidOperationException("Can only resume Paused campaigns.");

        campaign.Status = campaign.ScheduleType == ScheduleType.Scheduled && campaign.ScheduledAt > DateTime.UtcNow 
            ? CampaignStatus.Scheduled 
            : CampaignStatus.Sending;
            
        await _dbContext.SaveChangesAsync();

        if (campaign.Status == CampaignStatus.Sending)
        {
            _ = Task.Run(() => SendCampaignMessagesAsync(campaign.Id));
        }

        return MapToResponse(campaign);
    }

    public async Task<PagedResponse<CampaignRecipientResponse>> GetRecipientsAsync(int campaignId, PagedRequest request)
    {
        var query = _dbContext.CampaignContacts
            .Include(cc => cc.Contact)
            .Where(cc => cc.CampaignId == campaignId);

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderBy(cc => cc.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync();

        return new PagedResponse<CampaignRecipientResponse>
        {
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize,
            Items = items.Select(cc => new CampaignRecipientResponse
            {
                ContactId = cc.ContactId,
                ContactName = cc.Contact.Name,
                Phone = cc.Contact.Phone,
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

            var campaign = await dbContext.Campaigns
                .Include(c => c.Template)
                .Include(c => c.Variables)
                .Include(c => c.CampaignContacts)
                    .ThenInclude(cc => cc.Contact)
                .FirstOrDefaultAsync(c => c.Id == campaignId);

            if (campaign == null || campaign.Status != CampaignStatus.Sending) return;

            foreach (var cc in campaign.CampaignContacts)
            {
                // Process merge fields for this specific contact
                var messageVars = new Dictionary<string, string>();
                foreach (var v in campaign.Variables)
                {
                    string finalValue = v.VariableValue ?? "";
                    if (v.MergeField == "@name") finalValue = cc.Contact.Name;
                    else if (v.MergeField == "@phone") finalValue = cc.Contact.Phone;
                    
                    messageVars[v.VariableName] = finalValue;
                }

                // Send via WhatsApp API
                var messageId = await whatsAppService.SendTemplateMessageAsync(
                    cc.Contact.Phone, 
                    campaign.Template.Name, 
                    campaign.Template.Language, 
                    messageVars);

                if (messageId != null)
                {
                    cc.WhatsAppMessageId = messageId;
                    // Note: Status will be updated to Sent/Delivered/Read via webhook
                }
                else
                {
                    cc.Status = MessageStatus.Failed;
                    cc.ErrorMessage = "Failed to send via WhatsApp Cloud API";
                    campaign.FailedCount++;
                }

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
