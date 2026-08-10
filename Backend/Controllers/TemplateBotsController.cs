using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.TemplateBot;
using WhatsAppCampaignApi.Services.Interfaces;

using WhatsAppCampaignApi.Helpers;

namespace WhatsAppCampaignApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TemplateBotsController : ControllerBase
{
    private readonly ITemplateBotService _botService;

    public TemplateBotsController(ITemplateBotService botService)
    {
        _botService = botService;
    }

    [HttpGet]
    [RequiresPermission("TemplateBot.View")]
    public async Task<ActionResult<ApiResponse<PagedResponse<TemplateBotResponse>>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? relationType = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool sortDescending = false,
        [FromQuery] int? connectionId = null)
    {
        var request = new PagedRequest
        {
            Page = page,
            PageSize = pageSize,
            Search = search,
            SortBy = sortBy,
            SortDescending = sortDescending
        };

        var data = await _botService.GetPagedAsync(request, relationType, isActive, connectionId);
        return Ok(new ApiResponse<PagedResponse<TemplateBotResponse>> { Success = true, Data = data });
    }

    [HttpGet("{id}")]
    [RequiresPermission("TemplateBot.View")]
    public async Task<ActionResult<ApiResponse<TemplateBotResponse>>> GetById(int id)
    {
        try
        {
            var data = await _botService.GetByIdAsync(id);
            return Ok(new ApiResponse<TemplateBotResponse> { Success = true, Data = data });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ApiResponse<TemplateBotResponse> { Success = false, Message = ex.Message });
        }
    }

    [HttpPost]
    [RequiresPermission("TemplateBot.Create")]
    public async Task<ActionResult<ApiResponse<TemplateBotResponse>>> Create([FromBody] CreateTemplateBotRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new ApiResponse<TemplateBotResponse> { Success = false, Message = "Validation failed." });
        }

        try
        {
            var data = await _botService.CreateAsync(request);
            return CreatedAtAction(nameof(GetById), new { id = data.Id }, new ApiResponse<TemplateBotResponse> { Success = true, Data = data });
        }
        catch (Exception ex)
        {
            return BadRequest(new ApiResponse<TemplateBotResponse> { Success = false, Message = ex.Message });
        }
    }

    [HttpPut("{id}")]
    [RequiresPermission("TemplateBot.Edit")]
    public async Task<ActionResult<ApiResponse<TemplateBotResponse>>> Update(int id, [FromBody] UpdateTemplateBotRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new ApiResponse<TemplateBotResponse> { Success = false, Message = "Validation failed." });
        }

        try
        {
            var data = await _botService.UpdateAsync(id, request);
            return Ok(new ApiResponse<TemplateBotResponse> { Success = true, Data = data });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ApiResponse<TemplateBotResponse> { Success = false, Message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new ApiResponse<TemplateBotResponse> { Success = false, Message = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    [RequiresPermission("TemplateBot.Delete")]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(int id)
    {
        var result = await _botService.DeleteAsync(id);
        if (!result)
        {
            return NotFound(new ApiResponse<bool> { Success = false, Message = $"Template bot with ID {id} not found." });
        }
        return Ok(new ApiResponse<bool> { Success = true, Data = true });
    }

    [HttpPost("clone/{id}")]
    [RequiresPermission("TemplateBot.Clone")]
    public async Task<ActionResult<ApiResponse<TemplateBotResponse>>> Clone(int id)
    {
        try
        {
            var data = await _botService.CloneAsync(id);
            return Ok(new ApiResponse<TemplateBotResponse> { Success = true, Data = data });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ApiResponse<TemplateBotResponse> { Success = false, Message = ex.Message });
        }
    }

    [HttpPatch("toggle/{id}")]
    [RequiresPermission("TemplateBot.Edit")]
    public async Task<ActionResult<ApiResponse<TemplateBotResponse>>> ToggleActive(int id)
    {
        try
        {
            var data = await _botService.ToggleActiveAsync(id);
            return Ok(new ApiResponse<TemplateBotResponse> { Success = true, Data = data });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ApiResponse<TemplateBotResponse> { Success = false, Message = ex.Message });
        }
    }

    [HttpGet("check-keywords")]
    [RequiresPermission("TemplateBot.View")]
    public async Task<ActionResult<ApiResponse<List<string>>>> CheckKeywords(
        [FromQuery] string keywords,
        [FromQuery] int ignoreTemplateBotId = 0,
        [FromQuery] int ignoreBotFlowId = 0)
    {
        var warnings = await _botService.CheckKeywordsAsync(keywords, ignoreTemplateBotId, ignoreBotFlowId);
        return Ok(new ApiResponse<List<string>> { Success = true, Data = warnings });
    }
}
