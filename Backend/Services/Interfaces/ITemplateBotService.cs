using System.Threading.Tasks;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.TemplateBot;

namespace WhatsAppCampaignApi.Services.Interfaces;

public interface ITemplateBotService
{
    Task<PagedResponse<TemplateBotResponse>> GetPagedAsync(
        PagedRequest request, 
        string? relationType, 
        bool? isActive);
        
    Task<TemplateBotResponse> GetByIdAsync(int id);
    
    Task<TemplateBotResponse> CreateAsync(CreateTemplateBotRequest request);
    
    Task<TemplateBotResponse> UpdateAsync(int id, UpdateTemplateBotRequest request);
    
    Task<bool> DeleteAsync(int id);
    
    Task<TemplateBotResponse> CloneAsync(int id);
    
    Task<TemplateBotResponse> ToggleActiveAsync(int id);
}
