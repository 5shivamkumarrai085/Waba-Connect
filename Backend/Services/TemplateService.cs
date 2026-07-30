using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.Templates;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services;

public class TemplateService : ITemplateService
{
    private readonly AppDbContext _dbContext;
    private readonly IWhatsAppService _whatsAppService;

    public TemplateService(AppDbContext dbContext, IWhatsAppService whatsAppService)
    {
        _dbContext = dbContext;
        _whatsAppService = whatsAppService;
    }

    public async Task<PagedResponse<TemplateResponse>> GetAllAsync(PagedRequest request, string? status = null, string? category = null)
    {
        var query = _dbContext.Templates.AsNoTracking().Include(t => t.Variables).AsQueryable();

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<TemplateStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(t => t.Status == parsedStatus);
        }

        if (!string.IsNullOrEmpty(category) && Enum.TryParse<TemplateCategory>(category, true, out var parsedCategory))
        {
            query = query.Where(t => t.Category == parsedCategory);
        }

        if (!string.IsNullOrEmpty(request.Search))
        {
            var search = request.Search.ToLower();
            query = query.Where(t => t.Name.ToLower().Contains(search));
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(t => t.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync();

        return new PagedResponse<TemplateResponse>
        {
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize,
            Items = items.Select(MapToResponse).ToList()
        };
    }

    public async Task<TemplateResponse> GetByIdAsync(int id)
    {
        var template = await _dbContext.Templates
            .AsNoTracking()
            .Include(t => t.Variables)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (template == null)
            throw new KeyNotFoundException($"Template with ID {id} not found.");

        return MapToResponse(template);
    }

    public async Task<TemplateResponse> CreateAsync(CreateTemplateRequest request)
    {
        var existing = await _dbContext.Templates.AnyAsync(t => t.Name == request.Name);
        if (existing)
            throw new InvalidOperationException($"Template with name '{request.Name}' already exists.");

        var template = new Template
        {
            Name = request.Name,
            Language = request.Language,
            Category = Enum.Parse<TemplateCategory>(request.Category, true),
            TemplateType = Enum.Parse<TemplateType>(request.TemplateType, true),
            BodyText = request.BodyText,
            HeaderType = Enum.Parse<HeaderType>(request.HeaderType, true),
            HeaderContent = request.HeaderContent,
            FooterText = request.FooterText
        };

        if (request.Variables != null)
        {
            foreach (var v in request.Variables)
            {
                template.Variables.Add(new TemplateVariable
                {
                    Position = v.Position,
                    SampleValue = v.SampleValue,
                    Description = v.Description
                });
            }
        }

        _dbContext.Templates.Add(template);
        await _dbContext.SaveChangesAsync();

        return await GetByIdAsync(template.Id);
    }

    public async Task<TemplateResponse> UpdateAsync(int id, UpdateTemplateRequest request)
    {
        var template = await _dbContext.Templates
            .Include(t => t.Variables)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (template == null)
            throw new KeyNotFoundException($"Template with ID {id} not found.");

        if (template.Name != request.Name)
        {
            var existing = await _dbContext.Templates.AnyAsync(t => t.Name == request.Name);
            if (existing)
                throw new InvalidOperationException($"Another template with name '{request.Name}' already exists.");
        }

        template.Name = request.Name;
        template.Language = request.Language;
        template.Category = Enum.Parse<TemplateCategory>(request.Category, true);
        template.TemplateType = Enum.Parse<TemplateType>(request.TemplateType, true);
        template.BodyText = request.BodyText;
        template.HeaderType = Enum.Parse<HeaderType>(request.HeaderType, true);
        template.HeaderContent = request.HeaderContent;
        template.FooterText = request.FooterText;

        _dbContext.TemplateVariables.RemoveRange(template.Variables);

        if (request.Variables != null)
        {
            foreach (var v in request.Variables)
            {
                template.Variables.Add(new TemplateVariable
                {
                    TemplateId = template.Id,
                    Position = v.Position,
                    SampleValue = v.SampleValue,
                    Description = v.Description
                });
            }
        }

        await _dbContext.SaveChangesAsync();

        return await GetByIdAsync(template.Id);
    }

    public async Task DeleteAsync(int id)
    {
        var template = await _dbContext.Templates.FindAsync(id);
        if (template == null)
            throw new KeyNotFoundException($"Template with ID {id} not found.");

        var isInUse = await _dbContext.Campaigns.AnyAsync(c => c.TemplateId == id);
        if (isInUse)
            throw new InvalidOperationException("Cannot delete a template that is used in campaigns.");

        _dbContext.Templates.Remove(template);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<int> SyncFromWhatsAppAsync()
    {
        var waTemplates = await _whatsAppService.GetTemplatesAsync();
        int newCount = 0;

        foreach (var waTemplate in waTemplates)
        {
            var localTemplate = await _dbContext.Templates.FirstOrDefaultAsync(t => t.Name == waTemplate.Name);
            
            var mappedStatus = waTemplate.Status.ToUpper() switch
            {
                "APPROVED" => TemplateStatus.Approved,
                "REJECTED" => TemplateStatus.Rejected,
                _ => TemplateStatus.Pending
            };

            var mappedCategory = Enum.TryParse<TemplateCategory>(waTemplate.Category, true, out var cat) ? cat : TemplateCategory.Marketing;

            var mappedType = waTemplate.TemplateType?.ToUpper() switch
            {
                "IMAGE" => TemplateType.Image,
                "VIDEO" => TemplateType.Video,
                "DOCUMENT" => TemplateType.Document,
                _ => TemplateType.Text
            };

            if (localTemplate == null)
            {
                var newTemplate = new Template
                {
                    Name = waTemplate.Name,
                    WhatsAppTemplateId = waTemplate.Id,
                    Language = waTemplate.Language,
                    Category = mappedCategory,
                    TemplateType = mappedType,
                    Status = mappedStatus,
                    RejectReason = waTemplate.RejectReason,
                    BodyText = waTemplate.BodyText ?? string.Empty
                };
                _dbContext.Templates.Add(newTemplate);
                newCount++;
            }
            else
            {
                localTemplate.WhatsAppTemplateId = waTemplate.Id;
                localTemplate.Status = mappedStatus;
                localTemplate.TemplateType = mappedType;
                localTemplate.RejectReason = waTemplate.RejectReason;
                if (!string.IsNullOrEmpty(waTemplate.BodyText))
                {
                    localTemplate.BodyText = waTemplate.BodyText;
                }
            }
        }

        await _dbContext.SaveChangesAsync();
        return newCount;
    }

    public async Task<TemplatePreviewResponse> GetPreviewAsync(int id, Dictionary<string, string>? variableValues = null)
    {
        var template = await _dbContext.Templates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (template == null)
            throw new KeyNotFoundException($"Template with ID {id} not found.");

        var preview = template.BodyText;

        if (variableValues != null)
        {
            foreach (var kvp in variableValues)
            {
                preview = preview.Replace("{{" + kvp.Key + "}}", kvp.Value);
            }
        }
        else
        {
            // Use dummy values if none provided
            var variables = await _dbContext.TemplateVariables.AsNoTracking().Where(v => v.TemplateId == id).ToListAsync();
            foreach (var v in variables)
            {
                var val = !string.IsNullOrEmpty(v.SampleValue) ? v.SampleValue : $"[{v.Position}]";
                preview = preview.Replace("{{" + v.Position + "}}", val);
            }
        }

        return new TemplatePreviewResponse
        {
            Id = template.Id,
            Name = template.Name,
            PreviewText = preview
        };
    }

    private static TemplateResponse MapToResponse(Template t)
    {
        return new TemplateResponse
        {
            Id = t.Id,
            Name = t.Name,
            Language = t.Language,
            Category = t.Category.ToString(),
            TemplateType = t.TemplateType.ToString(),
            Status = t.Status.ToString(),
            BodyText = t.BodyText,
            HeaderType = t.HeaderType.ToString(),
            HeaderContent = t.HeaderContent,
            FooterText = t.FooterText,
            WhatsAppTemplateId = t.WhatsAppTemplateId,
            RejectReason = t.RejectReason,
            CreatedAt = t.CreatedAt,
            UpdatedAt = t.UpdatedAt,
            Variables = t.Variables.Select(v => new TemplateVariableRequest
            {
                Position = v.Position,
                SampleValue = v.SampleValue,
                Description = v.Description
            }).ToList()
        };
    }
}
