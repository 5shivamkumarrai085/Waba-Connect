using System.Threading.Tasks;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.MessageBot;

namespace WhatsAppCampaignApi.Services.Interfaces;

public interface IMessageBotService
{
    Task<PagedResponse<MessageBotResponse>> GetPagedAsync(
        PagedRequest request, 
        string? relationType, 
        bool? isActive);
        
    Task<MessageBotResponse> GetByIdAsync(int id);
    
    Task<MessageBotResponse> CreateAsync(CreateMessageBotRequest request);
    
    Task<MessageBotResponse> UpdateAsync(int id, UpdateMessageBotRequest request);
    
    Task<bool> DeleteAsync(int id);
    
    Task<MessageBotResponse> CloneAsync(int id);
    
    Task<MessageBotResponse> ToggleActiveAsync(int id);
}
