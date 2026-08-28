using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.Reporting;
using WhatsAppCampaignApi.Services.Interfaces;

using WhatsAppCampaignApi.Helpers;

namespace WhatsAppCampaignApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReportingController : ControllerBase
{
    private readonly IReportingService _reportingService;
    private readonly IReportQueryService _reportQueryService;
    private readonly IReportExportService _reportExportService;

    public ReportingController(
        IReportingService reportingService,
        IReportQueryService reportQueryService,
        IReportExportService reportExportService)
    {
        _reportingService = reportingService;
        _reportQueryService = reportQueryService;
        _reportExportService = reportExportService;
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

    // ── Report builder ───────────────────────────────────────────────────────

    /// <summary>The columns the builder can offer, so the client holds no parallel list.</summary>
    [HttpGet("report/columns")]
    [RequiresPermission("Reporting.View")]
    public IActionResult GetReportColumns() =>
        Ok(new ApiResponse<object> { Success = true, Data = _reportQueryService.GetColumns() });

    /// <summary>Filter options built from values actually present in the data.</summary>
    [HttpGet("report/filter-options")]
    [RequiresPermission("Reporting.View")]
    public async Task<IActionResult> GetReportFilterOptions()
    {
        var data = await _reportQueryService.GetFilterOptionsAsync();
        return Ok(new ApiResponse<ReportFilterOptionsDto> { Success = true, Data = data });
    }

    /// <summary>
    /// One page of report rows.
    ///
    /// POST rather than GET because the filter set is a structured object with several
    /// multi-select lists; encoding that into a query string would produce URLs long enough to be
    /// truncated by proxies, and would put contact identifiers in server access logs.
    /// </summary>
    [HttpPost("report/query")]
    [RequiresPermission("Reporting.View")]
    public async Task<IActionResult> RunReport([FromBody] ReportQueryRequest request)
    {
        var data = await _reportQueryService.QueryAsync(request ?? new ReportQueryRequest());
        return Ok(new ApiResponse<PagedResponse<ReportRowDto>> { Success = true, Data = data });
    }

    /// <summary>
    /// Exports the current result set as CSV, Excel or PDF.
    ///
    /// The filter set arrives in the body for the same reason as above, and the response is a file
    /// with a Content-Disposition filename — which CORS exposes explicitly, so the browser can
    /// read it rather than falling back to a generic name.
    /// </summary>
    [HttpPost("report/export")]
    [RequiresPermission("Reporting.Export")]
    public async Task<IActionResult> ExportReport(
        [FromBody] ReportExportRequest request,
        [FromQuery] string format = "csv")
    {
        var result = await _reportExportService.ExportAsync(
            request?.Filters ?? new ReportQueryRequest(),
            request?.Columns,
            format);

        return File(result.Content, result.ContentType, result.FileName);
    }

    // ── Saved reports ────────────────────────────────────────────────────────

    [HttpGet("report/saved")]
    [RequiresPermission("Reporting.View")]
    public async Task<IActionResult> GetSavedReports()
    {
        var data = await _reportQueryService.GetSavedReportsAsync();
        return Ok(new ApiResponse<List<SavedReportDto>> { Success = true, Data = data });
    }

    [HttpPost("report/saved")]
    [RequiresPermission("Reporting.Manage")]
    public async Task<IActionResult> CreateSavedReport([FromBody] SaveReportRequest request)
    {
        var data = await _reportQueryService.CreateSavedReportAsync(request);
        return Ok(new ApiResponse<SavedReportDto>
        {
            Success = true,
            Message = $"Report \"{data.Name}\" saved.",
            Data = data
        });
    }

    [HttpPut("report/saved/{id:int}")]
    [RequiresPermission("Reporting.Manage")]
    public async Task<IActionResult> UpdateSavedReport(int id, [FromBody] SaveReportRequest request)
    {
        var data = await _reportQueryService.UpdateSavedReportAsync(id, request);
        return Ok(new ApiResponse<SavedReportDto>
        {
            Success = true,
            Message = $"Report \"{data.Name}\" updated.",
            Data = data
        });
    }

    [HttpDelete("report/saved/{id:int}")]
    [RequiresPermission("Reporting.Manage")]
    public async Task<IActionResult> DeleteSavedReport(int id)
    {
        await _reportQueryService.DeleteSavedReportAsync(id);
        return Ok(new ApiResponse { Success = true, Message = "Report deleted." });
    }

    /// <summary>Records that a saved report was run, for the "Last Run" column.</summary>
    [HttpPost("report/saved/{id:int}/run")]
    [RequiresPermission("Reporting.View")]
    public async Task<IActionResult> TouchSavedReport(int id)
    {
        await _reportQueryService.TouchSavedReportAsync(id);
        return Ok(new ApiResponse { Success = true });
    }
}

/// <summary>Export payload: the filters to run, and which columns the file should carry.</summary>
public class ReportExportRequest
{
    public ReportQueryRequest Filters { get; set; } = new();

    /// <summary>Column keys in the author's order. Null falls back to the default set.</summary>
    public List<string>? Columns { get; set; }
}
