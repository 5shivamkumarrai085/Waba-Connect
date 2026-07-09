using Microsoft.AspNetCore.Mvc;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.Campaigns;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CampaignsController : ControllerBase
{
    private readonly ICampaignService _campaignService;

    public CampaignsController(ICampaignService campaignService)
    {
        _campaignService = campaignService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<CampaignResponse>>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null)
    {
        var request = new PagedRequest { Page = page, PageSize = pageSize, Search = search };
        var data = await _campaignService.GetAllAsync(request, status);
        return Ok(new ApiResponse<PagedResponse<CampaignResponse>> { Success = true, Data = data });
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<CampaignDetailResponse>>> GetById(int id)
    {
        var data = await _campaignService.GetByIdAsync(id);
        return Ok(new ApiResponse<CampaignDetailResponse> { Success = true, Data = data });
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<CampaignResponse>>> Create([FromBody] CreateCampaignRequest request)
    {
        var data = await _campaignService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = data.Id }, new ApiResponse<CampaignResponse> { Success = true, Data = data });
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse>> Delete(int id)
    {
        await _campaignService.DeleteAsync(id);
        return Ok(new ApiResponse { Success = true, Message = "Campaign deleted successfully." });
    }

    [HttpPost("{id}/cancel")]
    public async Task<ActionResult<ApiResponse<CampaignResponse>>> Cancel(int id)
    {
        var data = await _campaignService.CancelAsync(id);
        return Ok(new ApiResponse<CampaignResponse> { Success = true, Data = data, Message = "Campaign cancelled successfully." });
    }

    [HttpPost("{id}/pause")]
    public async Task<ActionResult<ApiResponse<CampaignResponse>>> Pause(int id)
    {
        var data = await _campaignService.PauseAsync(id);
        return Ok(new ApiResponse<CampaignResponse> { Success = true, Data = data, Message = "Campaign paused successfully." });
    }

    [HttpPost("{id}/resume")]
    public async Task<ActionResult<ApiResponse<CampaignResponse>>> Resume(int id)
    {
        var data = await _campaignService.ResumeAsync(id);
        return Ok(new ApiResponse<CampaignResponse> { Success = true, Data = data, Message = "Campaign resumed successfully." });
    }

    [HttpGet("{id}/recipients")]
    public async Task<ActionResult<ApiResponse<PagedResponse<CampaignRecipientResponse>>>> GetRecipients(
        int id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var request = new PagedRequest { Page = page, PageSize = pageSize };
        var data = await _campaignService.GetRecipientsAsync(id, request);
        return Ok(new ApiResponse<PagedResponse<CampaignRecipientResponse>> { Success = true, Data = data });
    }
}
