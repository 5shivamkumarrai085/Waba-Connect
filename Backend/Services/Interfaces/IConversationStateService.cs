using System.Collections.Generic;
using System.Threading.Tasks;
using WhatsAppCampaignApi.Models.Entities;

namespace WhatsAppCampaignApi.Services.Interfaces;

public interface IConversationStateService
{
    Task<ConversationState?> GetActiveStateAsync(string phoneNumber, int? connectionId = null);
    Task<ConversationState> CreateOrUpdateStateAsync(string phoneNumber, int flowId, string currentNodeId, Dictionary<string, string> variables, int? connectionId = null);
    Task DeleteStateAsync(string phoneNumber, int? connectionId = null);

    /// <summary>Ends every open flow for this phone on every connection (opt-out, opt-in). Returns how many were open.</summary>
    Task<int> CloseAllForPhoneAsync(string phoneNumber);
}
