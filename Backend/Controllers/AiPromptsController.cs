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
[Route("api/setup/ai-prompts")]
[Authorize]
public class AiPromptsController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IAuditService _auditService;

    public AiPromptsController(AppDbContext dbContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _auditService = auditService;
    }

    [HttpGet]
    [RequiresPermission("AiPrompt.View")]
    public async Task<IActionResult> GetAll()
    {
        var data = await _dbContext.AiPrompts
            .AsNoTracking()
            .OrderByDescending(p => p.IsDefault)
            .ThenBy(p => p.Name)
            .Select(p => new AiPromptResponse
            {
                Id = p.Id,
                Name = p.Name,
                PromptText = p.PromptText,
                Description = p.Description,
                IsActive = p.IsActive,
                IsDefault = p.IsDefault,
                CreatedAt = p.CreatedAt
            })
            .ToListAsync();

        return Ok(new ApiResponse<List<AiPromptResponse>> { Success = true, Data = data });
    }

    [HttpPost]
    [RequiresPermission("AiPrompt.Create")]
    public async Task<IActionResult> Create([FromBody] SaveAiPromptRequest request)
    {
        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(request.PromptText))
            return BadRequest(new ApiResponse { Success = false, Message = "Name and prompt text are required." });

        if (await _dbContext.AiPrompts.AnyAsync(p => p.Name.ToLower() == name.ToLower()))
            return Conflict(new ApiResponse { Success = false, Message = "A prompt with this name already exists." });

        var entity = new AiPrompt
        {
            Name = name,
            PromptText = request.PromptText,
            Description = request.Description,
            IsActive = request.IsActive,
            IsDefault = false
        };

        _dbContext.AiPrompts.Add(entity);
        await _dbContext.SaveChangesAsync();

        if (request.IsDefault) await SetDefaultAsync(entity.Id);

        await _auditService.LogAsync("AiPrompt.Created", "Settings", $"Created AI prompt '{name}'.", nameof(AiPrompt), entity.Id.ToString());
        return Ok(new ApiResponse { Success = true, Message = "AI prompt created successfully." });
    }

    [HttpPut("{id:int}")]
    [RequiresPermission("AiPrompt.Edit")]
    public async Task<IActionResult> Update(int id, [FromBody] SaveAiPromptRequest request)
    {
        var entity = await _dbContext.AiPrompts.FirstOrDefaultAsync(p => p.Id == id);
        if (entity is null) return NotFound(new ApiResponse { Success = false, Message = "AI prompt not found." });

        var name = request.Name.Trim();
        if (await _dbContext.AiPrompts.AnyAsync(p => p.Name.ToLower() == name.ToLower() && p.Id != id))
            return Conflict(new ApiResponse { Success = false, Message = "A prompt with this name already exists." });

        // The default prompt is the bot's fallback, so it can't also be switched off.
        if (entity.IsDefault && !request.IsActive)
            return Conflict(new ApiResponse { Success = false, Message = "The default prompt cannot be deactivated. Make another prompt the default first." });

        entity.Name = name;
        entity.PromptText = request.PromptText;
        entity.Description = request.Description;
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        if (request.IsDefault && !entity.IsDefault) await SetDefaultAsync(id);

        await _auditService.LogAsync("AiPrompt.Updated", "Settings", $"Updated AI prompt '{name}'.", nameof(AiPrompt), id.ToString());
        return Ok(new ApiResponse { Success = true, Message = "AI prompt updated successfully." });
    }

    [HttpDelete("{id:int}")]
    [RequiresPermission("AiPrompt.Delete")]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await _dbContext.AiPrompts.FirstOrDefaultAsync(p => p.Id == id);
        if (entity is null) return NotFound(new ApiResponse { Success = false, Message = "AI prompt not found." });

        // Deleting the fallback would leave the bot router with nothing to fall back to.
        if (entity.IsDefault)
            return Conflict(new ApiResponse { Success = false, Message = "The default prompt cannot be deleted. Make another prompt the default first." });

        _dbContext.AiPrompts.Remove(entity);
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync("AiPrompt.Deleted", "Settings", $"Deleted AI prompt '{entity.Name}'.", nameof(AiPrompt), id.ToString());
        return Ok(new ApiResponse { Success = true, Message = "AI prompt deleted successfully." });
    }

    private async Task SetDefaultAsync(int promptId)
    {
        var all = await _dbContext.AiPrompts.ToListAsync();
        foreach (var prompt in all) prompt.IsDefault = prompt.Id == promptId;
        await _dbContext.SaveChangesAsync();
    }
}
