using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WhatsAppCampaignApi.Models.Entities;

namespace WhatsAppCampaignApi.Executors;

public class StartTriggerExecutor : INodeExecutor
{
    public string NodeType => "Start Trigger";

    public Task<NodeExecutionResult> ExecuteAsync(
        FlowNode node,
        ConversationState state,
        string incomingMessage,
        List<FlowEdge> outgoingEdges)
    {
        // Start trigger simply forwards to the next connected node
        var nextEdge = outgoingEdges.FirstOrDefault(e => e.Source == node.NodeId);
        if (nextEdge == null)
        {
            return Task.FromResult(NodeExecutionResult.Complete());
        }

        return Task.FromResult(NodeExecutionResult.Next(nextEdge.Target));
    }
}
