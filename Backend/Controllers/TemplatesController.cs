using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.Templates;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TemplatesController : ControllerBase
{
    private readonly ITemplateService _templateService;
    private readonly IWhatsAppService _whatsAppService;
    private readonly AppDbContext _dbContext;
    private readonly IDashboardCacheService _dashboardCacheService;

    public TemplatesController(ITemplateService templateService, IWhatsAppService whatsAppService, AppDbContext dbContext, IDashboardCacheService dashboardCacheService)
    {
        _templateService = templateService;
        _whatsAppService = whatsAppService;
        _dbContext = dbContext;
        _dashboardCacheService = dashboardCacheService;
    }

    [HttpGet("by-connection/{connectionId}")]
    public async Task<ActionResult<ApiResponse<List<TemplateResponse>>>> GetByConnection(int connectionId)
    {
        var metaTemplates = await _whatsAppService.GetTemplatesForConnectionAsync(connectionId);
        var approvedMetaTemplates = metaTemplates.Where(t => t.Status.Equals("APPROVED", StringComparison.OrdinalIgnoreCase)).ToList();

        var dbTemplates = await _dbContext.Templates.Include(t => t.Variables).ToListAsync();
        
        var results = new List<TemplateResponse>();
        foreach (var mt in approvedMetaTemplates)
        {
            var dbMatch = dbTemplates.FirstOrDefault(t => t.Name.Equals(mt.Name, StringComparison.OrdinalIgnoreCase));
            if (dbMatch != null)
            {
                var fullDbRes = await _templateService.GetByIdAsync(dbMatch.Id);
                results.Add(fullDbRes);
            }
            else
            {
                results.Add(new TemplateResponse
                {
                    Id = Math.Abs(mt.Name.GetHashCode()),
                    Name = mt.Name,
                    Language = mt.Language,
                    Category = mt.Category,
                    TemplateType = mt.TemplateType ?? "Text",
                    Status = "Approved",
                    BodyText = mt.BodyText ?? string.Empty,
                    HeaderType = "None",
                    HeaderContent = mt.HeaderContent,
                    FooterText = mt.FooterText,
                    WhatsAppTemplateId = mt.Id
                });
            }
        }

        return Ok(new ApiResponse<List<TemplateResponse>> { Success = true, Data = results });
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<TemplateResponse>>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] string? category = null,
        [FromQuery] string? search = null)
    {
        var request = new PagedRequest { Page = page, PageSize = pageSize, Search = search };
        var data = await _templateService.GetAllAsync(request, status, category);
        return Ok(new ApiResponse<PagedResponse<TemplateResponse>> { Success = true, Data = data });
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<TemplateResponse>>> GetById(int id)
    {
        var data = await _templateService.GetByIdAsync(id);
        return Ok(new ApiResponse<TemplateResponse> { Success = true, Data = data });
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<TemplateResponse>>> Create([FromBody] CreateTemplateRequest request)
    {
        var data = await _templateService.CreateAsync(request);
        _dashboardCacheService.InvalidateCache();
        return CreatedAtAction(nameof(GetById), new { id = data.Id }, new ApiResponse<TemplateResponse> { Success = true, Data = data });
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<TemplateResponse>>> Update(int id, [FromBody] UpdateTemplateRequest request)
    {
        var data = await _templateService.UpdateAsync(id, request);
        _dashboardCacheService.InvalidateCache();
        return Ok(new ApiResponse<TemplateResponse> { Success = true, Data = data });
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse>> Delete(int id)
    {
        await _templateService.DeleteAsync(id);
        _dashboardCacheService.InvalidateCache();
        return Ok(new ApiResponse { Success = true, Message = "Template deleted successfully." });
    }

    [HttpGet("languages")]
    public ActionResult<ApiResponse<IEnumerable<object>>> GetLanguages()
    {
        var data = new[]
        {
            new { code = "en", name = "English" },
            new { code = "es", name = "Spanish" },
            new { code = "pt", name = "Portuguese" }
        };
        return Ok(new ApiResponse<IEnumerable<object>> { Success = true, Data = data });
    }

    [HttpGet("categories")]
    public ActionResult<ApiResponse<IEnumerable<object>>> GetCategories()
    {
        var data = new[]
        {
            new { id = "MARKETING", name = "Marketing" },
            new { id = "UTILITY", name = "Utility" },
            new { id = "AUTHENTICATION", name = "Authentication" }
        };
        return Ok(new ApiResponse<IEnumerable<object>> { Success = true, Data = data });
    }

    [HttpGet("statuses")]
    public ActionResult<ApiResponse<IEnumerable<object>>> GetStatuses()
    {
        var data = new[]
        {
            new { id = "APPROVED", name = "Approved" },
            new { id = "PENDING", name = "Pending" },
            new { id = "REJECTED", name = "Rejected" },
            new { id = "PAUSED", name = "Paused" },
            new { id = "DISABLED", name = "Disabled" }
        };
        return Ok(new ApiResponse<IEnumerable<object>> { Success = true, Data = data });
    }

    [HttpGet("types")]
    public ActionResult<ApiResponse<IEnumerable<object>>> GetTypes()
    {
        var data = new[]
        {
            new { id = "TEXT", name = "Text" },
            new { id = "MEDIA", name = "Media" },
            new { id = "INTERACTIVE", name = "Interactive" }
        };
        return Ok(new ApiResponse<IEnumerable<object>> { Success = true, Data = data });
    }

    [HttpPost("sync")]
    public async Task<ActionResult<ApiResponse>> SyncFromWhatsApp()
    {
        var count = await _templateService.SyncFromWhatsAppAsync();
        _dashboardCacheService.InvalidateCache();
        return Ok(new ApiResponse { Success = true, Message = $"Successfully synced templates. Added/Updated: {count}" });
    }

    [HttpPost("{id}/preview")]
    public async Task<ActionResult<ApiResponse<TemplatePreviewResponse>>> GetPreview(int id, [FromBody] Dictionary<string, string>? variables)
    {
        var data = await _templateService.GetPreviewAsync(id, variables);
        return Ok(new ApiResponse<TemplatePreviewResponse> { Success = true, Data = data });
    }
}
