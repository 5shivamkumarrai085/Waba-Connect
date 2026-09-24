using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.Setup;
using WhatsAppCampaignApi.Services.Email;

namespace WhatsAppCampaignApi.Controllers;

/// <summary>
/// Email template content and on/off state.
///
/// <para>
/// These templates are the single source of truth for every outgoing email — both the seeded
/// system notifications and the campaigns authored in the marketing section render from them.
/// Create, delete and preview were added with the email channel; the existing view, edit and
/// toggle endpoints are unchanged, so anything already calling them keeps working.
/// </para>
/// </summary>
[ApiController]
[Route("api/setup/email-templates")]
[Authorize]
public class EmailTemplatesController : ControllerBase
{
    private readonly IEmailTemplateService _service;

    public EmailTemplatesController(IEmailTemplateService service)
    {
        _service = service;
    }

    /// <param name="enabledOnly">
    /// True for the campaign template picker, which must not offer a disabled template.
    /// </param>
    [HttpGet]
    [RequiresPermission("EmailTemplate.View")]
    public async Task<IActionResult> GetAll([FromQuery] bool enabledOnly = false, CancellationToken ct = default)
    {
        var data = await _service.GetAllAsync(enabledOnly, ct);
        return Ok(new ApiResponse<List<EmailTemplateResponse>> { Success = true, Data = data });
    }

    [HttpGet("{id:int}")]
    [RequiresPermission("EmailTemplate.View")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var data = await _service.GetByIdAsync(id, ct);
        return Ok(new ApiResponse<EmailTemplateResponse> { Success = true, Data = data });
    }

    [HttpPost]
    [RequiresPermission("EmailTemplate.Create")]
    public async Task<IActionResult> Create([FromBody] SaveEmailTemplateRequest request, CancellationToken ct)
    {
        var data = await _service.CreateAsync(request, ct);
        return Ok(new ApiResponse<EmailTemplateResponse>
        {
            Success = true,
            Message = "Email template created.",
            Data = data
        });
    }

    [HttpPut("{id:int}")]
    [RequiresPermission("EmailTemplate.Edit")]
    public async Task<IActionResult> Update(int id, [FromBody] SaveEmailTemplateRequest request, CancellationToken ct)
    {
        var data = await _service.UpdateAsync(id, request, ct);
        return Ok(new ApiResponse<EmailTemplateResponse>
        {
            Success = true,
            Message = "Email template saved.",
            Data = data
        });
    }

    [HttpPatch("{id:int}/toggle")]
    [RequiresPermission("EmailTemplate.Toggle")]
    public async Task<IActionResult> Toggle(int id, CancellationToken ct)
    {
        var data = await _service.ToggleAsync(id, ct);
        return Ok(new ApiResponse<EmailTemplateResponse>
        {
            Success = true,
            Message = data.IsEnabled ? $"'{data.Name}' enabled." : $"'{data.Name}' disabled.",
            Data = data
        });
    }

    [HttpDelete("{id:int}")]
    [RequiresPermission("EmailTemplate.Delete")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _service.DeleteAsync(id, ct);
        return Ok(new ApiResponse { Success = true, Message = "Email template deleted." });
    }

    /// <summary>Renders the template with sample or supplied values, for the editor's preview.</summary>
    [HttpPost("{id:int}/preview")]
    [RequiresPermission("EmailTemplate.View")]
    public async Task<IActionResult> Preview(
        int id,
        [FromBody] EmailTemplatePreviewRequest? request,
        CancellationToken ct)
    {
        var data = await _service.PreviewAsync(id, request?.Values, request?.UseSampleData ?? false, ct);
        return Ok(new ApiResponse<EmailTemplatePreviewResponse> { Success = true, Data = data });
    }
}
