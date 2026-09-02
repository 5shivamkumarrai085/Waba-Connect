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

public class ButtonNodeExecutor : INodeExecutor
{
    private readonly IWhatsAppService _whatsAppService;
    private readonly AppDbContext _dbContext;

    public ButtonNodeExecutor(IWhatsAppService whatsAppService, AppDbContext dbContext)
    {
        _whatsAppService = whatsAppService;
        _dbContext = dbContext;
    }

    public string NodeType => "Button Message";

    public async Task<NodeExecutionResult> ExecuteAsync(
        FlowNode node,
        ConversationState state,
        string incomingMessage,
        List<FlowEdge> outgoingEdges)
    {
        // Parse node configuration
        string bodyText = "Please select an option:";
        var buttons = new List<ButtonItem>();

        try
        {
            using var doc = JsonDocument.Parse(node.DataJson);
            if (doc.RootElement.TryGetProperty("messageText", out var textProp))
            {
                bodyText = textProp.GetString() ?? bodyText;
            }

            if (doc.RootElement.TryGetProperty("buttons", out var buttonsProp))
            {
                var parsedButtons = JsonSerializer.Deserialize<List<ButtonItem>>(buttonsProp.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (parsedButtons != null)
                {
                    buttons = parsedButtons;
                }
            }
        }
        catch
        {
            // Ignore parse errors
        }

        // Unrecognised replies at this node so far. Zero on the first send.
        var retryCount = 0;

        // Check if we are resuming (processing reply) or arriving (first send)
        if (state.CurrentNodeId == node.NodeId)
        {
            // Resuming: Process user input
            var matchedButton = buttons.FirstOrDefault(b =>
                string.Equals(b.Text, incomingMessage, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(b.Value, incomingMessage, StringComparison.OrdinalIgnoreCase));

            int matchedIndex = matchedButton != null ? buttons.IndexOf(matchedButton) : -1;

            if (matchedIndex != -1)
            {
                // Try to find edge matching the index handle (button-0, button-1) or the value/text
                var nextEdge = outgoingEdges.FirstOrDefault(e =>
                    e.Source == node.NodeId &&
                    (e.SourceHandle == $"button-{matchedIndex}" || 
                     e.SourceHandle == matchedButton!.Value ||
                     e.SourceHandle == matchedButton!.Text));

                // Fallback: If no handle matches specifically, get any outgoing edge from this node
                nextEdge ??= outgoingEdges.FirstOrDefault(e => e.Source == node.NodeId);

                if (nextEdge != null)
                {
                    var collectVars = new Dictionary<string, string>
                    {
                        { node.NodeId, matchedButton!.Value ?? matchedButton.Text },
                        // A good answer wipes the slate: the next menu gets its own full allowance.
                        { MenuRetryPolicy.KeyFor(node.NodeId), "0" }
                    };
                    return new NodeExecutionResult
                    {
                        NextNodeId = nextEdge.Target,
                        CollectVariables = collectVars
                    };
                }
            }

            // Invalid input. One re-prompt is helpful; a second unrecognised reply means the
            // customer is not answering the menu, so the flow ends rather than asking forever.
            var giveUp = MenuRetryPolicy.OnUnrecognised(state, node.NodeId);
            if (giveUp != null)
            {
                return giveUp;
            }

            retryCount = MenuRetryPolicy.AttemptsSoFar(state, node.NodeId) + 1;

            // Falls through to re-send the options below.
        }

        // Send the interactive buttons message
        var replyButtons = buttons.Select((b, idx) => new
        {
            type = "reply",
            reply = new
            {
                id = string.IsNullOrEmpty(b.Value) ? $"button-{idx}" : b.Value,
                title = b.Text.Length > 20 ? b.Text.Substring(0, 20) : b.Text
            }
        }).ToList();

        var payload = new
        {
            messaging_product = "whatsapp",
            recipient_type = "individual",
            to = state.PhoneNumber,
            type = "interactive",
            interactive = new
            {
                type = "button",
                body = new { text = bodyText },
                action = new { buttons = replyButtons }
            }
        };

        // Resolve the correct phone number for this connection
        string? resolvedPhoneId = null;
        if (state.ConnectionId.HasValue)
        {
            var phoneAcc = await _dbContext.WabaPhoneNumbers.FirstOrDefaultAsync(p => p.ConnectionId == state.ConnectionId.Value);
            resolvedPhoneId = phoneAcc?.PhoneNumberId;
        }
        await _whatsAppService.SendCustomPayloadAsync(state.PhoneNumber, payload, resolvedPhoneId, state.ConnectionId);

        var paused = NodeExecutionResult.Pause(node.NodeId);
        paused.CollectVariables = MenuRetryPolicy.Counter(node.NodeId, retryCount);
        return paused;
    }

    private class ButtonItem
    {
        public string Text { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}
