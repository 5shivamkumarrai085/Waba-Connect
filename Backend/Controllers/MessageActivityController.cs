using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Controllers;

/// <summary>
/// The Setup → Activity Log view: every outbound template send and Meta's response to it.
/// </summary>
[ApiController]
[Route("api/setup/activity-log")]
[Authorize]
public class MessageActivityController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IAuditService _auditService;

    public MessageActivityController(AppDbContext dbContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _auditService = auditService;
    }

    /// <summary>
    /// The category values actually present in the log, for the filter dropdown.
    ///
    /// <para>
    /// Derived from the data rather than from a literal list. The frontend previously hardcoded
    /// <c>['All', 'Campaign', 'TemplateBot', 'InitiateChat']</c>, which meant a category added
    /// by a new send path would never appear as a filter option.
    /// </para>
    /// </summary>
    [HttpGet("categories")]
    [RequiresPermission("ActivityLog.View")]
    public async Task<IActionResult> GetCategories()
    {
        var categories = await _dbContext.MessageActivityLogs
            .AsNoTracking()
            .Select(l => l.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();

        return Ok(new ApiResponse<List<string>> { Success = true, Data = categories });
    }

    [HttpGet]
    [RequiresPermission("ActivityLog.View")]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search,
        [FromQuery] string? category,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var query = _dbContext.MessageActivityLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(category) && category != "All")
            query = query.Where(l => l.Category == category);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(l =>
                (l.Name != null && l.Name.ToLower().Contains(term)) ||
                (l.TemplateName != null && l.TemplateName.ToLower().Contains(term)) ||
                (l.ContactPhone != null && l.ContactPhone.Contains(term)));
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new
            {
                l.Id,
                l.Category,
                l.Name,
                l.TemplateName,
                l.ResponseCode,
                l.RelationType,
                l.ContactPhone,
                l.IsSuccess,
                l.ErrorMessage,
                l.TriggeredBy,
                l.IpAddress,
                l.WhatsAppMessageId,
                l.RequestPayload,
                l.ResponsePayload,
                l.CreatedAt
            })
            .ToListAsync();

        return Ok(new ApiResponse<object>
        {
            Success = true,
            Data = new { items, totalCount, page, pageSize }
        });
    }

    [HttpDelete("{id:int}")]
    [RequiresPermission("ActivityLog.Delete")]
    public async Task<IActionResult> Delete(int id)
    {
        var entry = await _dbContext.MessageActivityLogs.FirstOrDefaultAsync(l => l.Id == id);
        if (entry is null) return NotFound(new ApiResponse { Success = false, Message = "Log entry not found." });

        // Audited for the same reason Clear() is: deleting log rows must not be the one action
        // that leaves no trace anywhere.
        _dbContext.MessageActivityLogs.Remove(entry);
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            "ActivityLog.EntryDeleted", "Settings",
            $"Deleted message activity log entry #{id} ({entry.ContactPhone ?? entry.Category}).",
            "MessageActivityLog", id.ToString());

        return Ok(new ApiResponse { Success = true, Message = "Log entry deleted." });
    }

    [HttpPost("bulk-delete")]
    [RequiresPermission("ActivityLog.Delete")]
    public async Task<IActionResult> BulkDelete([FromBody] BulkDeleteRequest request)
    {
        if (request.Ids.Count == 0)
            return BadRequest(new ApiResponse { Success = false, Message = "No entries selected." });

        var entries = await _dbContext.MessageActivityLogs.Where(l => request.Ids.Contains(l.Id)).ToListAsync();
        var deletedCount = entries.Count;

        _dbContext.MessageActivityLogs.RemoveRange(entries);
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            "ActivityLog.EntriesDeleted", "Settings",
            $"Deleted {deletedCount} message activity log entr{(deletedCount == 1 ? "y" : "ies")}.",
            "MessageActivityLog", null);

        return Ok(new ApiResponse { Success = true, Message = $"{entries.Count} entries deleted." });
    }

    /// <summary>
    /// Empties the log. Writes an audit row first — the record that the log was cleared has to
    /// outlive the log itself, or the action leaves no trace anywhere.
    /// </summary>
    [HttpDelete("clear")]
    [RequiresPermission("ActivityLog.Clear")]
    public async Task<IActionResult> Clear()
    {
        var count = await _dbContext.MessageActivityLogs.CountAsync();

        await _auditService.LogAsync(
            "ActivityLog.Cleared", "Settings",
            $"Cleared the message activity log ({count} entries).");

        await _dbContext.MessageActivityLogs.ExecuteDeleteAsync();

        return Ok(new ApiResponse { Success = true, Message = $"Cleared {count} log entries." });
    }

    public class BulkDeleteRequest
    {
        public List<int> Ids { get; set; } = new();
    }
}
