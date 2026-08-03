using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.BotFlow;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BotFlowsController : ControllerBase
{
    private readonly IBotFlowService _flowService;
    private readonly IDashboardCacheService _dashboardCacheService;

    public BotFlowsController(IBotFlowService flowService, IDashboardCacheService dashboardCacheService)
    {
        _flowService = flowService;
        _dashboardCacheService = dashboardCacheService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<BotFlowResponse>>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? search = null,
        [FromQuery] int? connectionId = null)
    {
        var request = new PagedRequest
        {
            Page = page,
            PageSize = pageSize,
            Search = search
        };

        var data = await _flowService.GetPagedAsync(request, isActive, connectionId);
        return Ok(new ApiResponse<PagedResponse<BotFlowResponse>> { Success = true, Data = data });
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<BotFlowResponse>>> GetById(int id)
    {
        try
        {
            var data = await _flowService.GetByIdAsync(id);
            return Ok(new ApiResponse<BotFlowResponse> { Success = true, Data = data });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ApiResponse<BotFlowResponse> { Success = false, Message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<BotFlowResponse>>> Create([FromBody] CreateBotFlowRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new ApiResponse<BotFlowResponse> { Success = false, Message = "Validation failed." });
        }

        var data = await _flowService.CreateAsync(request);
        _dashboardCacheService.InvalidateCache();
        return CreatedAtAction(nameof(GetById), new { id = data.Id }, new ApiResponse<BotFlowResponse> { Success = true, Data = data });
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<BotFlowResponse>>> Update(int id, [FromBody] UpdateBotFlowRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new ApiResponse<BotFlowResponse> { Success = false, Message = "Validation failed." });
        }

        try
        {
            var data = await _flowService.UpdateAsync(id, request);
            _dashboardCacheService.InvalidateCache();
            return Ok(new ApiResponse<BotFlowResponse> { Success = true, Data = data });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ApiResponse<BotFlowResponse> { Success = false, Message = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(int id)
    {
        var result = await _flowService.DeleteAsync(id);
        if (!result)
        {
            return NotFound(new ApiResponse<bool> { Success = false, Message = $"Bot flow with ID {id} not found." });
        }
        _dashboardCacheService.InvalidateCache();
        return Ok(new ApiResponse<bool> { Success = true, Data = true });
    }

    [HttpPatch("toggle/{id}")]
    public async Task<ActionResult<ApiResponse<BotFlowResponse>>> ToggleActive(int id)
    {
        try
        {
            var data = await _flowService.ToggleActiveAsync(id);
            _dashboardCacheService.InvalidateCache();
            return Ok(new ApiResponse<BotFlowResponse> { Success = true, Data = data });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ApiResponse<BotFlowResponse> { Success = false, Message = ex.Message });
        }
    }
}
