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
        int? aiPromptId = null;

        try
        {
            using var doc = JsonDocument.Parse(node.DataJson);
            if (doc.RootElement.TryGetProperty("aiModel", out var modelProp)) aiModel = modelProp.GetString() ?? aiModel;
            if (doc.RootElement.TryGetProperty("instructions", out var instProp)) instructions = instProp.GetString() ?? instructions;

            // Optional, and read defensively: flows saved before AI prompts existed simply
            // don't carry this property and keep using their inline instructions.
            if (doc.RootElement.TryGetProperty("aiPromptId", out var promptProp) &&
                promptProp.ValueKind == JsonValueKind.Number &&
                promptProp.TryGetInt32(out var parsedPromptId))
            {
                aiPromptId = parsedPromptId;
            }
        }
        catch
        {
            // Ignore parse errors
        }

        // A selected prompt takes precedence over the node's inline instructions. If the prompt
        // was deleted or deactivated the node falls back to its own text rather than failing —
        // a flow going quiet is far worse than one running a slightly stale instruction.
        if (aiPromptId.HasValue)
        {
            var promptText = await _dbContext.AiPrompts
                .AsNoTracking()
                .Where(p => p.Id == aiPromptId.Value && p.IsActive)
                .Select(p => p.PromptText)
                .FirstOrDefaultAsync();

            if (!string.IsNullOrWhiteSpace(promptText)) instructions = promptText;
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
