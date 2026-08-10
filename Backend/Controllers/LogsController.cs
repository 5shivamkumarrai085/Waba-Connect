using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.Setup;
using WhatsAppCampaignApi.Services;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Controllers;

[ApiController]
[Route("api/setup/logs")]
[Authorize]
public class LogsController : ControllerBase
{
    private readonly ILogFileService _logFileService;
    private readonly IAuditService _auditService;

    public LogsController(ILogFileService logFileService, IAuditService auditService)
    {
        _logFileService = logFileService;
        _auditService = auditService;
    }

    [HttpGet("files")]
    [RequiresPermission("SystemLog.View")]
    public IActionResult GetFiles() =>
        Ok(new ApiResponse<List<LogFileResponse>> { Success = true, Data = _logFileService.GetFiles() });

    [HttpGet("entries")]
    [RequiresPermission("SystemLog.View")]
    public async Task<IActionResult> GetEntries(
        [FromQuery] string file,
        [FromQuery] string? level,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 15)
    {
        if (string.IsNullOrWhiteSpace(file))
            return BadRequest(new ApiResponse { Success = false, Message = "A log file must be specified." });

        var data = await _logFileService.GetEntriesAsync(file, level, search, page, pageSize);
        return Ok(new ApiResponse<LogEntriesResponse> { Success = true, Data = data });
    }

    [HttpDelete("files/{fileName}")]
    [RequiresPermission("SystemLog.Delete")]
    public async Task<IActionResult> DeleteFile(string fileName)
    {
        var error = await _logFileService.DeleteFileAsync(fileName);
        if (error is not null)
            return Conflict(new ApiResponse { Success = false, Message = error });

        // The record of a deletion has to outlive the thing deleted.
        await _auditService.LogAsync("SystemLog.Deleted", "Settings", $"Deleted log file '{fileName}'.");
        return Ok(new ApiResponse { Success = true, Message = $"Deleted {fileName}." });
    }

    [HttpDelete("files")]
    [RequiresPermission("SystemLog.Clear")]
    public async Task<IActionResult> ClearAll()
    {
        var (deleted, skipped) = await _logFileService.ClearAllAsync();

        await _auditService.LogAsync("SystemLog.Cleared", "Settings", $"Cleared {deleted} log file(s).");

        // Say plainly that today's file survived, rather than reporting a clean sweep that
        // didn't happen.
        var message = skipped > 0
            ? $"Deleted {deleted} log file(s). Today's active file was kept — it rolls over at midnight."
            : $"Deleted {deleted} log file(s).";

        return Ok(new ApiResponse { Success = true, Message = message });
    }
}
