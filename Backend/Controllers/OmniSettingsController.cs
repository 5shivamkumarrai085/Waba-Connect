using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.Setup;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Controllers;

/// <summary>
/// The OmniConnect settings surface.
///
/// Two endpoints only: read the whole schema, or save one section of it. There is deliberately no
/// per-field write — validation rules like "required once the toggle is on" span a section, and a
/// field-at-a-time API could not enforce them.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OmniSettingsController : ControllerBase
{
    private readonly IOmniSettingsService _settingsService;

    public OmniSettingsController(IOmniSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <summary>
    /// Every section, field, option list and stored value.
    ///
    /// The page is drawn from this, so a field added to the catalogue appears with no frontend
    /// change — and cannot appear without the server knowing how to validate it.
    /// </summary>
    [HttpGet("schema")]
    [RequiresPermission("OmniSettings.View")]
    public async Task<IActionResult> GetSchema()
    {
        var data = await _settingsService.GetSchemaAsync();
        return Ok(new ApiResponse<OmniSettingsSchemaDto> { Success = true, Data = data });
    }

    /// <summary>
    /// Saves one section. Keys outside that section are ignored rather than written.
    /// </summary>
    [HttpPut("{sectionKey}")]
    [RequiresPermission("OmniSettings.Edit")]
    public async Task<IActionResult> SaveSection(string sectionKey, [FromBody] SaveOmniSettingsRequest request)
    {
        var data = await _settingsService.SaveSectionAsync(sectionKey, request ?? new SaveOmniSettingsRequest());

        return Ok(new ApiResponse<OmniSettingsSchemaDto>
        {
            Success = true,
            Message = "Settings saved.",
            Data = data
        });
    }
}
