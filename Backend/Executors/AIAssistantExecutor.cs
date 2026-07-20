using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Executors;

public class AIAssistantExecutor : INodeExecutor
{
    private readonly IWhatsAppService _whatsAppService;
    private readonly AppDbContext _dbContext;

    public AIAssistantExecutor(IWhatsAppService whatsAppService, AppDbContext dbContext)
    {
        _whatsAppService = whatsAppService;
        _dbContext = dbContext;
    }

    public string NodeType => "AI Personal Assistant";

    public async Task<NodeExecutionResult> ExecuteAsync(
        FlowNode node,
        ConversationState state,
        string incomingMessage,
        List<FlowEdge> outgoingEdges)
    {
        string normalizedPhone = state.PhoneNumber.Replace("+", "").Trim();

        // Check if AI is stopped for this phone number
        var lastSession = await _dbContext.AiSessions
            .Where(s => s.PhoneNumber == normalizedPhone)
            .OrderByDescending(s => s.UpdatedAt)
            .FirstOrDefaultAsync();

        bool isAiStopped = lastSession != null && !lastSession.IsActive;

        if (isAiStopped)
        {
            // AI is stopped - bypass this node and transition immediately
            var nextEdge = outgoingEdges.FirstOrDefault(e => e.Source == node.NodeId);
            if (nextEdge == null)
            {
                return NodeExecutionResult.Complete();
            }
            return NodeExecutionResult.Next(nextEdge.Target);
        }

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

        // Check if there is an outgoing connection. If so, advance.
        var nextEdge2 = outgoingEdges.FirstOrDefault(e => e.Source == node.NodeId);
        var result = nextEdge2 == null ? NodeExecutionResult.Pause(node.NodeId) : NodeExecutionResult.Next(nextEdge2.Target);
        result.OutboundMessageText = responseText;
        return result;
    }
}
