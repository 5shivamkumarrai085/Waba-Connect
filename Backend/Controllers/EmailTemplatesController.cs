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

/// <summary>
/// Email template content and on/off state.
///
/// There is deliberately no send endpoint: this build ships template management only, with no
/// SMTP configured anywhere. Adding one would need a mail service and credentials.
/// </summary>
[ApiController]
[Route("api/setup/email-templates")]
[Authorize]
public class EmailTemplatesController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IAuditService _auditService;

    public EmailTemplatesController(AppDbContext dbContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _auditService = auditService;
    }

    [HttpGet]
    [RequiresPermission("EmailTemplate.View")]
    public async Task<IActionResult> GetAll()
    {
        var data = await _dbContext.EmailTemplates
            .AsNoTracking()
            .OrderBy(t => t.Id)
            .Select(t => new EmailTemplateResponse
            {
                Id = t.Id,
                Key = t.Key,
                Name = t.Name,
                Subject = t.Subject,
                BodyHtml = t.BodyHtml,
                IsEnabled = t.IsEnabled,
                IsSystem = t.IsSystem,
                AvailableVariables = t.AvailableVariables
            })
            .ToListAsync();

        return Ok(new ApiResponse<List<EmailTemplateResponse>> { Success = true, Data = data });
    }

    [HttpGet("{id:int}")]
    [RequiresPermission("EmailTemplate.View")]
    public async Task<IActionResult> GetById(int id)
    {
        var template = await _dbContext.EmailTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (template is null) return NotFound(new ApiResponse { Success = false, Message = "Email template not found." });

        return Ok(new ApiResponse<EmailTemplateResponse>
        {
            Success = true,
            Data = new EmailTemplateResponse
            {
                Id = template.Id,
                Key = template.Key,
                Name = template.Name,
                Subject = template.Subject,
                BodyHtml = template.BodyHtml,
                IsEnabled = template.IsEnabled,
                IsSystem = template.IsSystem,
                AvailableVariables = template.AvailableVariables
            }
        });
    }

    [HttpPut("{id:int}")]
    [RequiresPermission("EmailTemplate.Edit")]
    public async Task<IActionResult> Update(int id, [FromBody] SaveEmailTemplateRequest request)
    {
        var template = await _dbContext.EmailTemplates.FirstOrDefaultAsync(t => t.Id == id);
        if (template is null) return NotFound(new ApiResponse { Success = false, Message = "Email template not found." });

        if (string.IsNullOrWhiteSpace(request.Subject) || string.IsNullOrWhiteSpace(request.BodyHtml))
            return BadRequest(new ApiResponse { Success = false, Message = "Subject and body are required." });

        template.Name = request.Name.Trim();
        template.Subject = request.Subject.Trim();
        // Sanitized deliberately here rather than by the global filter: SanitizeHtml keeps a
        // safe tag allowlist, whereas the global SanitizeString would strip every tag and
        // leave the template as plain text.
        template.BodyHtml = SanitizationHelper.SanitizeHtml(request.BodyHtml) ?? string.Empty;
        template.IsEnabled = request.IsEnabled;
        template.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
        await _auditService.LogAsync("EmailTemplate.Updated", "Settings", $"Updated email template '{template.Name}'.", nameof(EmailTemplate), id.ToString());

        return Ok(new ApiResponse { Success = true, Message = "Email template saved." });
    }

    [HttpPatch("{id:int}/toggle")]
    [RequiresPermission("EmailTemplate.Toggle")]
    public async Task<IActionResult> Toggle(int id)
    {
        var template = await _dbContext.EmailTemplates.FirstOrDefaultAsync(t => t.Id == id);
        if (template is null) return NotFound(new ApiResponse { Success = false, Message = "Email template not found." });

        template.IsEnabled = !template.IsEnabled;
        template.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            template.IsEnabled ? "EmailTemplate.Enabled" : "EmailTemplate.Disabled", "Settings",
            $"{(template.IsEnabled ? "Enabled" : "Disabled")} email template '{template.Name}'.",
            nameof(EmailTemplate), id.ToString());

        return Ok(new ApiResponse
        {
            Success = true,
            Message = template.IsEnabled ? $"'{template.Name}' enabled." : $"'{template.Name}' disabled."
        });
    }
}
