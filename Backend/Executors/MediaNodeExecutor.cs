using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Executors;

public class MediaNodeExecutor : INodeExecutor
{
    private readonly IWhatsAppService _whatsAppService;
    private readonly AppDbContext _dbContext;

    public MediaNodeExecutor(IWhatsAppService whatsAppService, AppDbContext dbContext)
    {
        _whatsAppService = whatsAppService;
        _dbContext = dbContext;
    }

    public string NodeType => "Media Message";

    public async Task<NodeExecutionResult> ExecuteAsync(
        FlowNode node,
        ConversationState state,
        string incomingMessage,
        List<FlowEdge> outgoingEdges)
    {
        string mediaType = "image";
        string mediaUrl = string.Empty;
        string? caption = null;

        try
        {
            using var doc = JsonDocument.Parse(node.DataJson);
            if (doc.RootElement.TryGetProperty("mediaType", out var typeProp)) mediaType = typeProp.GetString() ?? mediaType;
            if (doc.RootElement.TryGetProperty("mediaUrl", out var urlProp)) mediaUrl = urlProp.GetString() ?? "";
            if (doc.RootElement.TryGetProperty("caption", out var capProp)) caption = capProp.GetString();
        }
        catch
        {
            // Ignore parse errors
        }

        if (!string.IsNullOrWhiteSpace(mediaUrl))
        {
            string filename = Path.GetFileName(mediaUrl);

            // Resolve the correct phone number for this connection
            string? resolvedPhoneId = null;
            if (state.ConnectionId.HasValue)
            {
                var phoneAcc = await _dbContext.WabaPhoneNumbers.FirstOrDefaultAsync(p => p.ConnectionId == state.ConnectionId.Value);
                resolvedPhoneId = phoneAcc?.PhoneNumberId;
            }

            await _whatsAppService.SendMediaMessageAsync(
                state.PhoneNumber,
                mediaUrl,
                mediaType,
                filename,
                caption,
                resolvedPhoneId,
                state.ConnectionId);
        }

        var nextEdge = outgoingEdges.FirstOrDefault(e => e.Source == node.NodeId);
        if (nextEdge == null)
        {
            return NodeExecutionResult.Complete();
        }

        return NodeExecutionResult.Next(nextEdge.Target);
    }
}
