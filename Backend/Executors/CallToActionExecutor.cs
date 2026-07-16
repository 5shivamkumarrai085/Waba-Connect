using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Executors;

public class CallToActionExecutor : INodeExecutor
{
    private readonly IWhatsAppService _whatsAppService;

    public CallToActionExecutor(IWhatsAppService whatsAppService)
    {
        _whatsAppService = whatsAppService;
    }

    public string NodeType => "Call to Action";

    public async Task<NodeExecutionResult> ExecuteAsync(
        FlowNode node,
        ConversationState state,
        string incomingMessage,
        List<FlowEdge> outgoingEdges)
    {
        string header = string.Empty;
        string valueText = string.Empty;
        string buttonText = "Click Here";
        string buttonLink = string.Empty;
        string footer = string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(node.DataJson);
            if (doc.RootElement.TryGetProperty("header", out var hProp)) header = hProp.GetString() ?? "";
            if (doc.RootElement.TryGetProperty("valueText", out var valProp)) valueText = valProp.GetString() ?? "";
            if (doc.RootElement.TryGetProperty("buttonText", out var btnProp)) buttonText = btnProp.GetString() ?? buttonText;
            if (doc.RootElement.TryGetProperty("buttonLink", out var linkProp)) buttonLink = linkProp.GetString() ?? "";
            if (doc.RootElement.TryGetProperty("footer", out var fProp)) footer = fProp.GetString() ?? "";
        }
        catch
        {
            // Ignore parse errors
        }

        // Build a beautiful structured CTA text representation
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(header))
        {
            lines.Add($"*{header}*");
        }
        if (!string.IsNullOrWhiteSpace(valueText))
        {
            lines.Add(valueText);
        }
        if (!string.IsNullOrWhiteSpace(buttonLink))
        {
            lines.Add($"🔗 *{buttonText}*\n👉 {buttonLink}");
        }
        else
        {
            lines.Add($"🔗 *{buttonText}*");
        }
        if (!string.IsNullOrWhiteSpace(footer))
        {
            lines.Add($"_{footer}_");
        }

        string formattedText = string.Join("\n\n", lines);

        await _whatsAppService.SendTextMessageAsync(state.PhoneNumber, formattedText);

        var nextEdge = outgoingEdges.FirstOrDefault(e => e.Source == node.NodeId);
        if (nextEdge == null)
        {
            return NodeExecutionResult.Complete();
        }

        return NodeExecutionResult.Next(nextEdge.Target);
    }
}
