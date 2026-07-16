using System.Collections.Generic;
using System.Threading.Tasks;
using WhatsAppCampaignApi.Models.Entities;

namespace WhatsAppCampaignApi.Services.Interfaces;

public interface IFlowLoaderService
{
    Task<BotFlow?> LoadFlowAsync(int flowId);
    Task<List<FlowNode>> GetNodesForFlowAsync(int flowId);
    Task<List<FlowEdge>> GetEdgesForFlowAsync(int flowId);
}
