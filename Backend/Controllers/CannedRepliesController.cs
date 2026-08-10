using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.Setup;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Controllers;

[ApiController]
[Route("api/setup/canned-replies")]
[Authorize]
public class CannedRepliesController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IAuditService _auditService;
    private readonly ICurrentUserService _currentUser;

    public CannedRepliesController(AppDbContext dbContext, IAuditService auditService, ICurrentUserService currentUser)
    {
        _dbContext = dbContext;
        _auditService = auditService;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Replies the caller can see: every public one, plus their own private ones.
    /// </summary>
    /// <param name="activeOnly">
    /// Set by the chat composer, which should only offer replies that are switched on. The
    /// management list leaves it false so inactive replies remain editable.
    /// </param>
    [HttpGet]
    [RequiresPermission("CannedReply.View")]
    public async Task<IActionResult> GetAll([FromQuery] bool activeOnly = false)
    {
        var userId = _currentUser.UserId;

        var query = _dbContext.CannedReplies
            .AsNoTracking()
            .Where(r => r.IsPublic || (userId != null && r.CreatedByUserId == userId));

        if (activeOnly) query = query.Where(r => r.IsActive);

        var data = await query
            .OrderByDescending(r => r.IsPublic)
            .ThenBy(r => r.Title)
            .Select(r => new CannedReplyResponse
            {
                Id = r.Id,
                Title = r.Title,
                Description = r.Description,
                IsPublic = r.IsPublic,
                IsActive = r.IsActive,
                IsMine = userId != null && r.CreatedByUserId == userId,
                CreatedAt = r.CreatedAt
            })
            .ToListAsync();

        return Ok(new ApiResponse<List<CannedReplyResponse>> { Success = true, Data = data });
    }

    [HttpPost]
    [RequiresPermission("CannedReply.Create")]
    public async Task<IActionResult> Create([FromBody] SaveCannedReplyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Description))
            return BadRequest(new ApiResponse { Success = false, Message = "Title and description are required." });

        var entity = new CannedReply
        {
            Title = request.Title.Trim(),
            Description = request.Description,
            IsPublic = request.IsPublic,
            IsActive = request.IsActive,
            CreatedByUserId = _currentUser.UserId
        };

        _dbContext.CannedReplies.Add(entity);
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync("CannedReply.Created", "Settings", $"Created canned reply '{entity.Title}'.", nameof(CannedReply), entity.Id.ToString());
        return Ok(new ApiResponse { Success = true, Message = "Canned reply created successfully." });
    }

    [HttpPut("{id:int}")]
    [RequiresPermission("CannedReply.Edit")]
    public async Task<IActionResult> Update(int id, [FromBody] SaveCannedReplyRequest request)
    {
        var entity = await _dbContext.CannedReplies.FirstOrDefaultAsync(r => r.Id == id);
        if (entity is null) return NotFound(new ApiResponse { Success = false, Message = "Canned reply not found." });

        entity.Title = request.Title.Trim();
        entity.Description = request.Description;
        entity.IsPublic = request.IsPublic;
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
        await _auditService.LogAsync("CannedReply.Updated", "Settings", $"Updated canned reply '{entity.Title}'.", nameof(CannedReply), id.ToString());

        return Ok(new ApiResponse { Success = true, Message = "Canned reply updated successfully." });
    }

    /// <summary>Flips the public/private flag from the list, matching the toggle in the reference UI.</summary>
    [HttpPatch("{id:int}/toggle-public")]
    [RequiresPermission("CannedReply.Edit")]
    public async Task<IActionResult> TogglePublic(int id)
    {
        var entity = await _dbContext.CannedReplies.FirstOrDefaultAsync(r => r.Id == id);
        if (entity is null) return NotFound(new ApiResponse { Success = false, Message = "Canned reply not found." });

        entity.IsPublic = !entity.IsPublic;
        entity.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        return Ok(new ApiResponse
        {
            Success = true,
            Message = entity.IsPublic ? "Reply is now shared with everyone." : "Reply is now private to you."
        });
    }

    [HttpDelete("{id:int}")]
    [RequiresPermission("CannedReply.Delete")]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await _dbContext.CannedReplies.FirstOrDefaultAsync(r => r.Id == id);
        if (entity is null) return NotFound(new ApiResponse { Success = false, Message = "Canned reply not found." });

        _dbContext.CannedReplies.Remove(entity);
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync("CannedReply.Deleted", "Settings", $"Deleted canned reply '{entity.Title}'.", nameof(CannedReply), id.ToString());
        return Ok(new ApiResponse { Success = true, Message = "Canned reply deleted successfully." });
    }
}
