using System.Collections.Generic;
using System.Threading.Tasks;
using WhatsAppCampaignApi.Models.Entities;

namespace WhatsAppCampaignApi.Services.Interfaces;

public interface IConversationStateService
{
    Task<ConversationState?> GetActiveStateAsync(string phoneNumber, int? connectionId = null);
    Task<ConversationState> CreateOrUpdateStateAsync(string phoneNumber, int flowId, string currentNodeId, Dictionary<string, string> variables, int? connectionId = null);
    Task DeleteStateAsync(string phoneNumber, int? connectionId = null);
}
