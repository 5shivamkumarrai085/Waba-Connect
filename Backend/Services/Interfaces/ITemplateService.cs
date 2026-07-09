using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.Templates;

namespace WhatsAppCampaignApi.Services.Interfaces;

/// <summary>
/// Service for managing WhatsApp message templates.
/// </summary>
public interface ITemplateService
{
    Task<PagedResponse<TemplateResponse>> GetAllAsync(PagedRequest request, string? status = null, string? category = null);
    Task<TemplateResponse> GetByIdAsync(int id);
    Task<TemplateResponse> CreateAsync(CreateTemplateRequest request);
    Task<TemplateResponse> UpdateAsync(int id, UpdateTemplateRequest request);
    Task DeleteAsync(int id);

    /// <summary>
    /// Syncs templates from the WhatsApp Cloud API into the local database.
    /// </summary>
    Task<int> SyncFromWhatsAppAsync();

    /// <summary>
    /// Generates a preview of the template body with sample variables replaced.
    /// </summary>
    Task<TemplatePreviewResponse> GetPreviewAsync(int id, Dictionary<string, string>? variableValues = null);
}
