using System.Threading.Tasks;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.BotFlow;

namespace WhatsAppCampaignApi.Services.Interfaces;

public interface IBotFlowService
{
    Task<PagedResponse<BotFlowResponse>> GetPagedAsync(
        PagedRequest request, 
        bool? isActive);
        
    Task<BotFlowResponse> GetByIdAsync(int id);
    
    Task<BotFlowResponse> CreateAsync(CreateBotFlowRequest request);
    
    Task<BotFlowResponse> UpdateAsync(int id, UpdateBotFlowRequest request);
    
    Task<bool> DeleteAsync(int id);
    
    Task<BotFlowResponse> ToggleActiveAsync(int id);
}
