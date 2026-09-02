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

public class ListNodeExecutor : INodeExecutor
{
    private readonly IWhatsAppService _whatsAppService;
    private readonly AppDbContext _dbContext;

    public ListNodeExecutor(IWhatsAppService whatsAppService, AppDbContext dbContext)
    {
        _whatsAppService = whatsAppService;
        _dbContext = dbContext;
    }

    public string NodeType => "List Message";

    public async Task<NodeExecutionResult> ExecuteAsync(
        FlowNode node,
        ConversationState state,
        string incomingMessage,
        List<FlowEdge> outgoingEdges)
    {
        string headerText = string.Empty;
        string bodyText = "Please select from the options list:";
        string footerText = string.Empty;
        string buttonText = "View Options";
        var sections = new List<ListSection>();

        try
        {
            using var doc = JsonDocument.Parse(node.DataJson);
            if (doc.RootElement.TryGetProperty("headerText", out var hProp)) headerText = hProp.GetString() ?? "";
            if (doc.RootElement.TryGetProperty("bodyText", out var bProp)) bodyText = bProp.GetString() ?? bodyText;
            if (doc.RootElement.TryGetProperty("footerText", out var fProp)) footerText = fProp.GetString() ?? "";
            if (doc.RootElement.TryGetProperty("buttonText", out var btnProp)) buttonText = btnProp.GetString() ?? buttonText;
            if (doc.RootElement.TryGetProperty("sections", out var secProp))
            {
                var parsed = JsonSerializer.Deserialize<List<ListSection>>(secProp.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (parsed != null) sections = parsed;
            }
        }
        catch
        {
            // Ignore parse errors
        }

        // Gather all rows for validation and checking responses
        var allRows = new List<ListRow>();
        foreach (var sec in sections)
        {
            if (sec.Items != null) allRows.AddRange(sec.Items);
            if (sec.Rows != null) allRows.AddRange(sec.Rows);
        }

        // Resuming: user selected a list item
        // Unrecognised replies at this node so far. Zero on the first send.
        var retryCount = 0;

        if (state.CurrentNodeId == node.NodeId)
        {
            var matchedRow = allRows.FirstOrDefault(r =>
                string.Equals(r.Title, incomingMessage, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(r.Id, incomingMessage, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(r.Value, incomingMessage, StringComparison.OrdinalIgnoreCase));

            if (matchedRow != null)
            {
                string rowId = string.IsNullOrEmpty(matchedRow.Id) ? matchedRow.Value : matchedRow.Id;
                
                var nextEdge = outgoingEdges.FirstOrDefault(e =>
                    e.Source == node.NodeId &&
                    (e.SourceHandle == rowId ||
                     e.SourceHandle == matchedRow.Title ||
                     e.SourceHandle == $"item-{rowId}" ||
                     e.SourceHandle == matchedRow.Value ||
                     e.SourceHandle == $"item-{matchedRow.Value}"));

                // Fallback
                nextEdge ??= outgoingEdges.FirstOrDefault(e => e.Source == node.NodeId);

                if (nextEdge != null)
                {
                    var collectVars = new Dictionary<string, string>
                    {
                        { node.NodeId, rowId },
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

            // Invalid input. Same rule as the button menu: one re-prompt, then the flow ends
            // rather than answering every unrelated message with the same list.
            var giveUp = MenuRetryPolicy.OnUnrecognised(state, node.NodeId);
            if (giveUp != null)
            {
                return giveUp;
            }

            retryCount = MenuRetryPolicy.AttemptsSoFar(state, node.NodeId) + 1;
        }

        // Convert list sections to Meta structure
        var metaSections = sections.Select(sec => new
        {
            title = string.IsNullOrEmpty(sec.Title) ? "Options" : sec.Title,
            rows = (sec.Items ?? sec.Rows ?? new List<ListRow>()).Select(r => new
            {
                id = string.IsNullOrEmpty(r.Id) ? r.Value : r.Id,
                title = r.Title.Length > 24 ? r.Title.Substring(0, 24) : r.Title,
                description = r.Description?.Length > 72 ? r.Description.Substring(0, 72) : r.Description
            }).ToList()
        }).ToList();

        var payload = new
        {
            messaging_product = "whatsapp",
            recipient_type = "individual",
            to = state.PhoneNumber,
            type = "interactive",
            interactive = new
            {
                type = "list",
                header = !string.IsNullOrEmpty(headerText) ? new { type = "text", text = headerText } : null,
                body = new { text = bodyText },
                footer = !string.IsNullOrEmpty(footerText) ? new { text = footerText } : null,
                action = new
                {
                    button = buttonText,
                    sections = metaSections
                }
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

    private class ListSection
    {
        public string Title { get; set; } = string.Empty;
        public List<ListRow>? Items { get; set; }
        public List<ListRow>? Rows { get; set; }
    }

    private class ListRow
    {
        public string Id { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
    }
}
