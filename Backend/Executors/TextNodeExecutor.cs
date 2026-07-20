using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Executors;

public class TextNodeExecutor : INodeExecutor
{
    private readonly IWhatsAppService _whatsAppService;

    public TextNodeExecutor(IWhatsAppService whatsAppService)
    {
        _whatsAppService = whatsAppService;
    }

    public string NodeType => "Text Message";

    public async Task<NodeExecutionResult> ExecuteAsync(
        FlowNode node,
        ConversationState state,
        string incomingMessage,
        List<FlowEdge> outgoingEdges)
    {
        string messageText = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(node.DataJson);
            if (doc.RootElement.TryGetProperty("messageText", out var textProp))
            {
                messageText = textProp.GetString() ?? string.Empty;
            }
            else if (doc.RootElement.TryGetProperty("message", out var msgProp))
            {
                messageText = msgProp.GetString() ?? string.Empty;
            }
        }
        catch
        {
            // Fallback if JSON parsing fails
        }

        if (string.IsNullOrWhiteSpace(messageText))
        {
            messageText = "Text Message configured incorrectly.";
        }

        // Variable interpolation support: e.g. {{name}} or {{phone}}
        if (state.VariablesJson != "{}" && !string.IsNullOrWhiteSpace(state.VariablesJson))
        {
            try
            {
                var variables = JsonSerializer.Deserialize<Dictionary<string, string>>(state.VariablesJson);
                if (variables != null)
                {
                    foreach (var kv in variables)
                    {
                        messageText = messageText.Replace($"{{{{{kv.Key}}}}}", kv.Value, StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            catch
            {
                // Ignore interpolation errors
            }
        }

        var nextEdge = outgoingEdges.FirstOrDefault(e => e.Source == node.NodeId);
        var result = nextEdge == null ? NodeExecutionResult.Complete() : NodeExecutionResult.Next(nextEdge.Target);
        result.OutboundMessageText = messageText;
        return result;
    }
}
