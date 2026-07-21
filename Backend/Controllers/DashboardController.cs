using Microsoft.AspNetCore.Mvc;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly IDashboardCacheService _dashboardCacheService;

    public DashboardController(IDashboardCacheService dashboardCacheService)
    {
        _dashboardCacheService = dashboardCacheService;
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary([FromQuery] string timeFilter = "all")
    {
        var data = await _dashboardCacheService.GetSummaryAsync(timeFilter);
        return Ok(new ApiResponse<object> { Success = true, Data = data });
    }
}
