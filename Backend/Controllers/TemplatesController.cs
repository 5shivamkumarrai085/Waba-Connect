using Microsoft.AspNetCore.Mvc;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.Templates;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TemplatesController : ControllerBase
{
    private readonly ITemplateService _templateService;

    public TemplatesController(ITemplateService templateService)
    {
        _templateService = templateService;
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
        return CreatedAtAction(nameof(GetById), new { id = data.Id }, new ApiResponse<TemplateResponse> { Success = true, Data = data });
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<TemplateResponse>>> Update(int id, [FromBody] UpdateTemplateRequest request)
    {
        var data = await _templateService.UpdateAsync(id, request);
        return Ok(new ApiResponse<TemplateResponse> { Success = true, Data = data });
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse>> Delete(int id)
    {
        await _templateService.DeleteAsync(id);
        return Ok(new ApiResponse { Success = true, Message = "Template deleted successfully." });
    }

    [HttpPost("sync")]
    public async Task<ActionResult<ApiResponse>> SyncFromWhatsApp()
    {
        var count = await _templateService.SyncFromWhatsAppAsync();
        return Ok(new ApiResponse { Success = true, Message = $"Successfully synced templates. Added/Updated: {count}" });
    }

    [HttpPost("{id}/preview")]
    public async Task<ActionResult<ApiResponse<TemplatePreviewResponse>>> GetPreview(int id, [FromBody] Dictionary<string, string>? variables)
    {
        var data = await _templateService.GetPreviewAsync(id, variables);
        return Ok(new ApiResponse<TemplatePreviewResponse> { Success = true, Data = data });
    }
}
