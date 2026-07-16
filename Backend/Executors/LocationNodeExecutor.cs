using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Executors;

public class LocationNodeExecutor : INodeExecutor
{
    private readonly IWhatsAppService _whatsAppService;

    public LocationNodeExecutor(IWhatsAppService whatsAppService)
    {
        _whatsAppService = whatsAppService;
    }

    public string NodeType => "Location";

    public async Task<NodeExecutionResult> ExecuteAsync(
        FlowNode node,
        ConversationState state,
        string incomingMessage,
        List<FlowEdge> outgoingEdges)
    {
        double latitude = 0;
        double longitude = 0;
        string name = string.Empty;
        string address = string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(node.DataJson);
            if (doc.RootElement.TryGetProperty("latitude", out var latProp))
            {
                if (latProp.ValueKind == JsonValueKind.String)
                    double.TryParse(latProp.GetString(), out latitude);
                else
                    latitude = latProp.GetDouble();
            }
            if (doc.RootElement.TryGetProperty("longitude", out var lonProp))
            {
                if (lonProp.ValueKind == JsonValueKind.String)
                    double.TryParse(lonProp.GetString(), out longitude);
                else
                    longitude = lonProp.GetDouble();
            }
            if (doc.RootElement.TryGetProperty("locationName", out var nameProp)) name = nameProp.GetString() ?? "";
            if (doc.RootElement.TryGetProperty("address", out var addrProp)) address = addrProp.GetString() ?? "";
        }
        catch
        {
            // Ignore parse errors
        }

        var payload = new
        {
            messaging_product = "whatsapp",
            recipient_type = "individual",
            to = state.PhoneNumber,
            type = "location",
            location = new
            {
                latitude = latitude,
                longitude = longitude,
                name = string.IsNullOrEmpty(name) ? "Location" : name,
                address = address
            }
        };

        await _whatsAppService.SendCustomPayloadAsync(state.PhoneNumber, payload);

        var nextEdge = outgoingEdges.FirstOrDefault(e => e.Source == node.NodeId);
        if (nextEdge == null)
        {
            return NodeExecutionResult.Complete();
        }

        return NodeExecutionResult.Next(nextEdge.Target);
    }
}
