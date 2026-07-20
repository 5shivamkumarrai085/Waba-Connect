using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Executors;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services;

public class FlowExecutionService : IFlowExecutionService
{
    private readonly AppDbContext _dbContext;
    private readonly IFlowLoaderService _flowLoader;
    private readonly IConversationStateService _stateService;
    private readonly IEnumerable<INodeExecutor> _executors;
    private readonly IWhatsAppService _whatsAppService;

    public FlowExecutionService(
        AppDbContext dbContext,
        IFlowLoaderService flowLoader,
        IConversationStateService stateService,
        IEnumerable<INodeExecutor> executors,
        IWhatsAppService whatsAppService)
    {
        _dbContext = dbContext;
        _flowLoader = flowLoader;
        _stateService = stateService;
        _executors = executors;
        _whatsAppService = whatsAppService;
    }

    public async Task<bool> ExecuteFlowStepAsync(string phoneNumber, string incomingMessage)
    {
        var activeState = await _stateService.GetActiveStateAsync(phoneNumber);

        if (activeState != null)
        {
            await ResumeFlowAsync(activeState, incomingMessage);
            return true;
        }
        else
        {
            return await EvaluateTriggersAsync(phoneNumber, incomingMessage);
        }
    }

    private async Task ResumeFlowAsync(ConversationState state, string incomingMessage)
    {
        await LogBotMessageAsync(state.PhoneNumber, "Incoming", incomingMessage, state.FlowId, state.CurrentNodeId);

        var nodes = await _flowLoader.GetNodesForFlowAsync(state.FlowId);
        var edges = await _flowLoader.GetEdgesForFlowAsync(state.FlowId);

        var currentNode = nodes.FirstOrDefault(n => n.NodeId == state.CurrentNodeId);
        if (currentNode == null)
        {
            await _stateService.DeleteStateAsync(state.PhoneNumber);
            return;
        }

        var executor = _executors.FirstOrDefault(e => e.NodeType == currentNode.NodeType);
        if (executor == null)
        {
            await _stateService.DeleteStateAsync(state.PhoneNumber);
            return;
        }

        var result = await executor.ExecuteAsync(currentNode, state, incomingMessage, edges);
        if (!string.IsNullOrEmpty(result.OutboundMessageText))
        {
            await LogBotMessageAsync(state.PhoneNumber, "Outgoing", result.OutboundMessageText, state.FlowId, state.CurrentNodeId);
            await SyncToChatMessagesAsync(state.PhoneNumber, result.OutboundMessageText);
        }
        await ProcessResultAsync(state, result, nodes, edges, incomingMessage);
    }

    private async Task<bool> EvaluateTriggersAsync(string phoneNumber, string incomingMessage)
    {
        var activeFlows = await _dbContext.BotFlows.Where(f => f.IsActive).ToListAsync();

        foreach (var flow in activeFlows)
        {
            var nodes = await _flowLoader.GetNodesForFlowAsync(flow.Id);
            var triggerNode = nodes.FirstOrDefault(n => n.NodeType == "Start Trigger");
            if (triggerNode == null) continue;

            if (IsTriggerMatch(triggerNode, incomingMessage))
            {
                var edges = await _flowLoader.GetEdgesForFlowAsync(flow.Id);

                var state = await _stateService.CreateOrUpdateStateAsync(
                    phoneNumber,
                    flow.Id,
                    triggerNode.NodeId,
                    new Dictionary<string, string>());

                await LogBotMessageAsync(phoneNumber, "Incoming", incomingMessage, flow.Id, triggerNode.NodeId);

                var executor = _executors.First(e => e.NodeType == "Start Trigger");
                var result = await executor.ExecuteAsync(triggerNode, state, incomingMessage, edges);
                if (!string.IsNullOrEmpty(result.OutboundMessageText))
                {
                    await LogBotMessageAsync(phoneNumber, "Outgoing", result.OutboundMessageText, flow.Id, triggerNode.NodeId);
                    await SyncToChatMessagesAsync(phoneNumber, result.OutboundMessageText);
                }

                await ProcessResultAsync(state, result, nodes, edges, incomingMessage);
                return true; 
            }
        }
        return false;
    }

    private bool IsTriggerMatch(FlowNode triggerNode, string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(triggerNode.DataJson);
            if (!doc.RootElement.TryGetProperty("keywords", out var kwProp)) return false;
            var keywords = JsonSerializer.Deserialize<List<string>>(kwProp.GetRawText()) ?? new List<string>();

            string triggerType = "on exact match";
            if (doc.RootElement.TryGetProperty("triggerType", out var typeProp))
            {
                triggerType = typeProp.GetString() ?? triggerType;
            }

            var cleanMsg = message.Trim();
            foreach (var kw in keywords)
            {
                if (string.IsNullOrWhiteSpace(kw)) continue;

                if (triggerType.Equals("on exact match", StringComparison.OrdinalIgnoreCase))
                {
                    if (cleanMsg.Equals(kw.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
                }
                else if (triggerType.Equals("contains", StringComparison.OrdinalIgnoreCase))
                {
                    if (cleanMsg.Contains(kw.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
                }
                else if (triggerType.Equals("starts with", StringComparison.OrdinalIgnoreCase))
                {
                    if (cleanMsg.StartsWith(kw.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
        }
        catch
        {
            // Ignore parsing errors
        }

        return false;
    }

    private async Task ProcessResultAsync(
        ConversationState state,
        NodeExecutionResult result,
        List<FlowNode> nodes,
        List<FlowEdge> edges,
        string incomingMessage)
    {
        if (result.CollectVariables != null && result.CollectVariables.Any())
        {
            await _stateService.CreateOrUpdateStateAsync(state.PhoneNumber, state.FlowId, state.CurrentNodeId, result.CollectVariables);
        }

        if (result.IsCompleted || string.IsNullOrEmpty(result.NextNodeId))
        {
            await _stateService.DeleteStateAsync(state.PhoneNumber);
            return;
        }

        if (result.IsWaitingForReply)
        {
            await _stateService.CreateOrUpdateStateAsync(state.PhoneNumber, state.FlowId, result.NextNodeId, new Dictionary<string, string>());
            return;
        }

        var nextNode = nodes.FirstOrDefault(n => n.NodeId == result.NextNodeId);
        if (nextNode == null)
        {
            await _stateService.DeleteStateAsync(state.PhoneNumber);
            return;
        }

        var executor = _executors.FirstOrDefault(e => e.NodeType == nextNode.NodeType);
        if (executor == null)
        {
            await _stateService.DeleteStateAsync(state.PhoneNumber);
            return;
        }

        var nextResult = await executor.ExecuteAsync(nextNode, state, incomingMessage, edges);

        string outboundText = nextResult.OutboundMessageText ?? GetOutboundTextForNode(nextNode);
        if (!string.IsNullOrEmpty(outboundText))
        {
            await LogBotMessageAsync(state.PhoneNumber, "Outgoing", outboundText, state.FlowId, nextNode.NodeId);
            
            string? mediaUrl = null;
            string? mediaType = null;
            string? mediaFileName = null;

            if (nextNode.NodeType == "Media Message")
            {
                try
                {
                    using var doc = JsonDocument.Parse(nextNode.DataJson);
                    string rawUrl = doc.RootElement.TryGetProperty("mediaUrl", out var urlProp) ? urlProp.GetString() ?? "" : "";
                    mediaType = doc.RootElement.TryGetProperty("mediaType", out var typeProp) ? typeProp.GetString() ?? "image" : "image";
                    
                    if (!string.IsNullOrEmpty(rawUrl))
                    {
                        mediaUrl = SaveBase64ToLocalFile(rawUrl, nextNode.NodeId);
                        mediaFileName = string.Equals(mediaType, "image", StringComparison.OrdinalIgnoreCase) ? "image.jpg" : "file";
                    }
                }
                catch
                {
                    // Ignore parsing issues
                }
            }

            await SyncToChatMessagesAsync(state.PhoneNumber, outboundText, mediaUrl, mediaType, mediaFileName);
        }

        await ProcessResultAsync(state, nextResult, nodes, edges, incomingMessage);
    }

    private string GetOutboundTextForNode(FlowNode node)
    {
        try
        {
            using var doc = JsonDocument.Parse(node.DataJson);
            if (node.NodeType == "Text Message")
            {
                return doc.RootElement.TryGetProperty("messageText", out var p1) ? p1.GetString() ?? "" :
                       doc.RootElement.TryGetProperty("message", out var p2) ? p2.GetString() ?? "" : "";
            }
            if (node.NodeType == "Button Message")
            {
                return doc.RootElement.TryGetProperty("messageText", out var p) ? p.GetString() ?? "" : "";
            }
            if (node.NodeType == "List Message")
            {
                return doc.RootElement.TryGetProperty("bodyText", out var p) ? p.GetString() ?? "" : "";
            }
            if (node.NodeType == "Media Message")
            {
                string url = doc.RootElement.TryGetProperty("mediaUrl", out var p) ? p.GetString() ?? "" : "";
                string type = doc.RootElement.TryGetProperty("mediaType", out var t) ? t.GetString() ?? "file" : "file";
                if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    return $"[Sent {type}: base64 data]";
                }
                return $"[Sent {type}: {url}]";
            }
            if (node.NodeType == "Location")
            {
                string name = doc.RootElement.TryGetProperty("locationName", out var p) ? p.GetString() ?? "" : "";
                string address = doc.RootElement.TryGetProperty("locationAddress", out var addr) ? addr.GetString() ?? "" : "";
                string latitude = "";
                if (doc.RootElement.TryGetProperty("latitude", out var lat))
                {
                    latitude = lat.ValueKind == JsonValueKind.Number ? lat.GetDouble().ToString() : lat.GetString() ?? "";
                }
                string longitude = "";
                if (doc.RootElement.TryGetProperty("longitude", out var lng))
                {
                    longitude = lng.ValueKind == JsonValueKind.Number ? lng.GetDouble().ToString() : lng.GetString() ?? "";
                }
                return $"[Location|Name: {name}|Addr: {address}|Lat: {latitude}|Lng: {longitude}]";
            }
            if (node.NodeType == "Contact Card")
            {
                var contacts = new List<LocalContactItem>();
                if (doc.RootElement.TryGetProperty("contacts", out var contactsProp))
                {
                    var parsed = JsonSerializer.Deserialize<List<LocalContactItem>>(contactsProp.GetRawText(), new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });
                    if (parsed != null) contacts = parsed;
                }

                if (contacts.Any())
                {
                    var c = contacts.First();
                    return $"[ContactCard|Name: {c.FirstName} {c.LastName}|Phone: {c.Phone}|Email: {c.Email}|Org: {c.Company} - {c.Title}]";
                }
                return "[Sent Contact Card]";
            }
            if (node.NodeType == "Call to Action")
            {
                return doc.RootElement.TryGetProperty("valueText", out var p) ? p.GetString() ?? "" : "";
            }
        }
        catch
        {
            // Ignore parse errors
        }

        return string.Empty;
    }

    private async Task LogBotMessageAsync(string phone, string direction, string text, int flowId, string nodeId)
    {
        var msg = new BotMessage
        {
            PhoneNumber = phone,
            Direction = direction,
            Text = text,
            Status = "Sent",
            FlowId = flowId,
            NodeId = nodeId,
            Timestamp = DateTime.UtcNow
        };
        _dbContext.Messages.Add(msg);
        await _dbContext.SaveChangesAsync();
    }

    private async Task SyncToChatMessagesAsync(
        string phone, 
        string text, 
        string? mediaUrl = null, 
        string? mediaType = null, 
        string? mediaFileName = null)
    {
        try
        {
            var normalizedPhone = Helpers.PhoneNumberHelper.NormalizePhoneNumber(phone);
            var contact = await _dbContext.Contacts.FirstOrDefaultAsync(c => c.Phone == normalizedPhone);
            if (contact == null) return;

            var conversation = await _dbContext.ChatConversations.FirstOrDefaultAsync(c => c.ContactId == contact.Id);
            if (conversation == null)
            {
                var account = await _dbContext.WabaPhoneNumbers.FirstOrDefaultAsync();
                conversation = new ChatConversation
                {
                    ContactId = contact.Id,
                    WabaPhoneNumberId = account?.Id
                };
                _dbContext.ChatConversations.Add(conversation);
                await _dbContext.SaveChangesAsync();
            }

            conversation.LastMessageText = string.IsNullOrEmpty(mediaUrl) ? text : $"[Sent {mediaType ?? "media"}]";
            conversation.LastMessageAt = DateTime.UtcNow;

            WhatsAppSendResult sendResult;
            if (!string.IsNullOrEmpty(mediaUrl) && !string.IsNullOrEmpty(mediaType))
            {
                sendResult = await _whatsAppService.SendMediaMessageAsync(
                    normalizedPhone,
                    mediaUrl,
                    mediaType,
                    mediaFileName,
                    text);
            }
            else
            {
                sendResult = await _whatsAppService.SendTextMessageAsync(normalizedPhone, text);
            }

            _dbContext.ChatMessages.Add(new ChatMessage
            {
                ConversationId = conversation.Id,
                ContactId = contact.Id,
                WhatsAppMessageId = sendResult.Success ? sendResult.MessageId : null,
                Direction = ChatMessageDirection.Outgoing,
                Status = sendResult.Success ? ChatMessageStatus.Sent : ChatMessageStatus.Failed,
                ErrorMessage = sendResult.Success ? null : sendResult.ErrorMessage,
                Text = text,
                MediaUrl = mediaUrl,
                MediaType = mediaType,
                MediaFileName = mediaFileName,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await _dbContext.SaveChangesAsync();
        }
        catch
        {
            // Ignore sync errors
        }
    }

    private string? SaveBase64ToLocalFile(string base64DataUrl, string nodeId)
    {
        try
        {
            if (!base64DataUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || !base64DataUrl.Contains(";base64,"))
            {
                return base64DataUrl; // Already a URL or relative path
            }

            var prefix = base64DataUrl.Substring(0, base64DataUrl.IndexOf(";base64,"));
            var mime = prefix.Substring(5); // e.g. "image/jpeg"
            var ext = mime.Split('/').LastOrDefault() ?? "jpg";
            if (ext == "jpeg") ext = "jpg";

            var base64Part = base64DataUrl.Substring(base64DataUrl.IndexOf(";base64,") + 8);
            var fileBytes = Convert.FromBase64String(base64Part);

            var dir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "media");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var filename = $"media_{nodeId}_{DateTime.UtcNow.Ticks}.{ext}";
            var absolutePath = Path.Combine(dir, filename);
            File.WriteAllBytes(absolutePath, fileBytes);

            return $"/uploads/media/{filename}";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save base64 media locally: {ex.Message}");
            return null;
        }
    }

    private class LocalContactItem
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Company { get; set; }
        public string? Title { get; set; }
    }
}
