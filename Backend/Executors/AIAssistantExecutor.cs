using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Executors;

public class AIAssistantExecutor : INodeExecutor
{
    private readonly IWhatsAppService _whatsAppService;

    public AIAssistantExecutor(IWhatsAppService whatsAppService)
    {
        _whatsAppService = whatsAppService;
    }

    public string NodeType => "AI Personal Assistant";

    public async Task<NodeExecutionResult> ExecuteAsync(
        FlowNode node,
        ConversationState state,
        string incomingMessage,
        List<FlowEdge> outgoingEdges)
    {
        string aiModel = "Gemini 1.5 Flash";
        string instructions = "Help the customer as a friendly assistant.";

        try
        {
            using var doc = JsonDocument.Parse(node.DataJson);
            if (doc.RootElement.TryGetProperty("aiModel", out var modelProp)) aiModel = modelProp.GetString() ?? aiModel;
            if (doc.RootElement.TryGetProperty("instructions", out var instProp)) instructions = instProp.GetString() ?? instructions;
        }
        catch
        {
            // Ignore parse errors
        }

        // Simulate AI assistant text response generation
        string responseText = $"[AI Assistant - {aiModel}]: Thank you for your message. Running under instructions: \"{instructions}\". You asked: \"{incomingMessage}\". How can I help you further?";

        await _whatsAppService.SendTextMessageAsync(state.PhoneNumber, responseText);

        // Check if there is an outgoing connection. If so, advance.
        var nextEdge = outgoingEdges.FirstOrDefault(e => e.Source == node.NodeId);
        if (nextEdge == null)
        {
            // If no output connection, AI acts as an open-ended conversational bot and loops.
            return NodeExecutionResult.Pause(node.NodeId);
        }

        return NodeExecutionResult.Next(nextEdge.Target);
    }
}
