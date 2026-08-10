using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Services.Interfaces;

using WhatsAppCampaignApi.Helpers;

namespace WhatsAppCampaignApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReportingController : ControllerBase
{
    private readonly IReportingService _reportingService;

    public ReportingController(IReportingService reportingService)
    {
        _reportingService = reportingService;
    }

    [HttpGet("metrics")]
    [RequiresPermission("Reporting.View")]
    public async Task<IActionResult> GetMetrics([FromQuery] string filter = "all")
    {
        var data = await _reportingService.GetMetricsAsync(filter);
        return Ok(new ApiResponse<object> { Success = true, Data = data });
    }

    [HttpGet("accuracy")]
    [RequiresPermission("Reporting.View")]
    public async Task<IActionResult> GetAccuracy()
    {
        var data = await _reportingService.GetAccuracyAsync();
        return Ok(new ApiResponse<object> { Success = true, Data = data });
    }

    [HttpGet("freshness")]
    [RequiresPermission("Reporting.View")]
    public async Task<IActionResult> GetFreshness()
    {
        var data = await _reportingService.GetFreshnessAsync();
        return Ok(new ApiResponse<object> { Success = true, Data = data });
    }

    [HttpGet("exports")]
    [RequiresPermission("Reporting.View")]
    public async Task<IActionResult> GetExports()
    {
        var data = await _reportingService.GetExportsAsync();
        return Ok(new ApiResponse<object> { Success = true, Data = data });
    }

    [HttpGet("customisation")]
    [RequiresPermission("Reporting.View")]
    public IActionResult GetCustomisation()
    {
        var data = _reportingService.GetCustomisationFeatures();
        return Ok(new ApiResponse<object> { Success = true, Data = data });
    }

    [HttpGet("export/metrics")]
    [RequiresPermission("Reporting.Export")]
    public async Task<IActionResult> ExportMetricsCsv([FromQuery] string filter = "all")
    {
        var bytes = await _reportingService.BuildMetricsCsvAsync(filter);
        return File(bytes, "text/csv", $"reporting_metrics_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv");
    }

    [HttpGet("export/contacts")]
    [RequiresPermission("Reporting.Export")]
    public async Task<IActionResult> ExportContactsCsv()
    {
        var bytes = await _reportingService.BuildContactsCsvAsync();
        return File(bytes, "text/csv", $"contacts_export_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv");
    }

    [HttpGet("export/chats")]
    [RequiresPermission("Reporting.Export")]
    public async Task<IActionResult> ExportChatsCsv()
    {
        var bytes = await _reportingService.BuildChatsCsvAsync();
        return File(bytes, "text/csv", $"chats_export_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv");
    }
}
