using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Services.Integrations;

namespace WhatsAppCampaignApi.Controllers;

/// <summary>Saved reports emailed on a timetable.</summary>
[ApiController]
[Authorize]
[Route("api/report-schedules")]
public class ReportSchedulesController : ControllerBase
{
    private readonly IReportScheduleService _schedules;

    public ReportSchedulesController(IReportScheduleService schedules) => _schedules = schedules;

    [HttpGet]
    [RequiresPermission("Reporting.View")]
    public async Task<ActionResult<ApiResponse<List<ReportScheduleDto>>>> List(CancellationToken ct) =>
        Ok(new ApiResponse<List<ReportScheduleDto>> { Success = true, Data = await _schedules.ListAsync(ct) });

    /// <summary>The email senders a schedule can go out from.</summary>
    [HttpGet("senders")]
    [RequiresPermission("Reporting.Schedule")]
    public async Task<ActionResult<ApiResponse<List<ScheduleSenderOption>>>> Senders(CancellationToken ct) =>
        Ok(new ApiResponse<List<ScheduleSenderOption>> { Success = true, Data = await _schedules.GetSendersAsync(ct) });

    [HttpPost]
    [RequiresPermission("Reporting.Schedule")]
    public async Task<ActionResult<ApiResponse<ReportScheduleDto>>> Create([FromBody] SaveReportScheduleRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<ReportScheduleDto> { Success = true, Data = await _schedules.CreateAsync(request, ct), Message = "Report scheduled." });

    [HttpPut("{id:int}")]
    [RequiresPermission("Reporting.Schedule")]
    public async Task<ActionResult<ApiResponse<ReportScheduleDto>>> Update(int id, [FromBody] SaveReportScheduleRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<ReportScheduleDto> { Success = true, Data = await _schedules.UpdateAsync(id, request, ct), Message = "Schedule saved." });

    [HttpDelete("{id:int}")]
    [RequiresPermission("Reporting.Schedule")]
    public async Task<ActionResult<ApiResponse>> Delete(int id, CancellationToken ct)
    {
        await _schedules.DeleteAsync(id, ct);
        return Ok(new ApiResponse { Success = true, Message = "Schedule deleted." });
    }

    /// <summary>Builds and sends the report now, outside the timetable.</summary>
    [HttpPost("{id:int}/run")]
    [RequiresPermission("Reporting.Schedule")]
    public async Task<ActionResult<ApiResponse<ReportScheduleDto>>> RunNow(int id, CancellationToken ct)
    {
        var result = await _schedules.RunNowAsync(id, ct);
        return Ok(new ApiResponse<ReportScheduleDto>
        {
            Success = result.LastStatus == "Sent",
            Data = result,
            Message = result.LastStatus == "Sent" ? "Report sent." : result.LastError ?? "The report could not be sent."
        });
    }
}
