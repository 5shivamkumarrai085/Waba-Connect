using System.Collections.Generic;
using System.Threading.Tasks;
using WhatsAppCampaignApi.Models.Entities;

namespace WhatsAppCampaignApi.Executors;

public interface INodeExecutor
{
    string NodeType { get; }
    Task<NodeExecutionResult> ExecuteAsync(
        FlowNode node,
        ConversationState state,
        string incomingMessage,
        List<FlowEdge> outgoingEdges);
}
