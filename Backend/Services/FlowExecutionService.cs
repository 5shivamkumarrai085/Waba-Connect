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
    private readonly IConfiguration _configuration;
    private readonly ILogger<FlowExecutionService> _logger;
    private readonly HotPathCache _cache;

    public FlowExecutionService(
        AppDbContext dbContext,
        IFlowLoaderService flowLoader,
        IConversationStateService stateService,
        IEnumerable<INodeExecutor> executors,
        IWhatsAppService whatsAppService,
        IConfiguration configuration,
        ILogger<FlowExecutionService> logger,
        HotPathCache cache)
    {
        _cache = cache;
        _dbContext = dbContext;
        _flowLoader = flowLoader;
        _stateService = stateService;
        _executors = executors;
        _whatsAppService = whatsAppService;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// How long a half-finished flow keeps hold of a conversation.
    ///
    /// <para>
    /// A flow used to stay Active until it reached an end node, and a customer who abandoned it
    /// mid-menu never reached one. From then on <em>every</em> message they sent — for weeks, in
    /// practice — was fed back into the menu they had walked away from, which answered "Good
    /// morning" with "Please choose an option" and never let the trigger keywords be evaluated
    /// again. Sessions have to end on their own, and a customer returning tomorrow has to be
    /// starting a new conversation, not resuming yesterday's.
    /// </para>
    /// <para>
    /// Thirty minutes is the working default: long enough that a customer reading options, or
    /// stepping away for a coffee, comes back to the same menu; short enough that the next
    /// unrelated message is treated as a fresh conversation. Override with
    /// <c>BotFlow:SessionTimeoutMinutes</c>.
    /// </para>
    /// </summary>
    private TimeSpan SessionTimeout =>
        TimeSpan.FromMinutes(_configuration.GetValue("BotFlow:SessionTimeoutMinutes", 30));

    /// <summary>
    /// Hard cap on one session however active it is (<c>BotFlow:MaxSessionMinutes</c>). Each
    /// unrecognised reply refreshes the idle clock, so without a cap a chatty customer could stay
    /// inside one menu all day.
    /// </summary>
    private TimeSpan MaxSessionLength =>
        TimeSpan.FromMinutes(_configuration.GetValue("BotFlow:MaxSessionMinutes", 120));

    /// <summary>
    /// Sent when a menu stops waiting and nothing else matched (<c>BotFlow:MenuGiveUpReply</c>;
    /// empty to stay silent). Without it the customer's second message simply vanished.
    /// </summary>
    private string MenuGiveUpReply =>
        _configuration["BotFlow:MenuGiveUpReply"]
        ?? "No problem. Send us a message any time and we'll start again.";

    /// <summary>Session variable holding when the flow started, for <see cref="MaxSessionLength"/>.</summary>
    private const string StartedAtVariable = "__startedAt";

    public async Task<bool> ExecuteFlowStepAsync(string phoneNumber, string incomingMessage, int? connectionId = null)
    {
        var handled = await ExecuteFlowStepCoreAsync(phoneNumber, incomingMessage, connectionId);

        // Bot log rows are added as the turn goes and written here in one go (state changes have
        // already saved most of them along the way) instead of one round trip per line.
        if (_dbContext.ChangeTracker.HasChanges()) await _dbContext.SaveChangesAsync();
        return handled;
    }

    private async Task<bool> ExecuteFlowStepCoreAsync(string phoneNumber, string incomingMessage, int? connectionId)
    {
        // ── Stop means stop ─────────────────────────────────────────────────────────────────────
        // The bot router already refuses to route a suppressed customer, so in the normal path this
        // never fires. It is here anyway because this engine is what actually sends the menus, and
        // "a customer who said stop is never answered" is too important to rest on a single check
        // at one entry point. Any future caller of the flow engine inherits the rule for free.
        if (await IsSuppressedAsync(phoneNumber, connectionId))
        {
            _logger.LogInformation(
                "Bot flow refused for {Phone}: the customer has stopped automated replies.", phoneNumber);

            // Close anything still open, so the moment the suppression lapses they are not dropped
            // back into a half-finished menu from before they opted out.
            await CloseActiveStateAsync(phoneNumber, connectionId);
            return false;
        }

        var activeState = await _stateService.GetActiveStateAsync(phoneNumber, connectionId);

        // An abandoned, over-long or orphaned session must not keep intercepting messages. Closed
        // here rather than by a sweeper so the very next message is already free to match a trigger.
        if (activeState != null && EndReason(activeState) is { } reason)
        {
            _logger.LogInformation(
                "Bot flow session for {Phone} on flow {FlowId} ended ({Reason}); re-evaluating triggers.",
                MaskPhone(phoneNumber), activeState.FlowId, reason);

            await _stateService.DeleteStateAsync(phoneNumber, connectionId);
            activeState = null;
        }

        if (activeState == null)
        {
            return await EvaluateTriggersAsync(phoneNumber, incomingMessage, connectionId);
        }

        if (connectionId.HasValue && activeState.ConnectionId == null)
        {
            activeState.ConnectionId = connectionId.Value;
            _dbContext.Entry(activeState).State = EntityState.Modified;
            await _dbContext.SaveChangesAsync();
        }
        else if (!connectionId.HasValue && activeState.ConnectionId.HasValue)
        {
            connectionId = activeState.ConnectionId;
        }

        // A flow's exact keyword wins over a menu that is waiting for an answer: a customer who
        // types "send money" while the "hi" menu is open wants Send Money, not that menu again.
        // A genuine option of the waiting menu is never taken as a keyword.
        var interrupting = await FindTriggeredFlowAsync(incomingMessage, connectionId, exactOnly: true);
        if (interrupting is not null && !await IsAnswerToCurrentNodeAsync(activeState, incomingMessage))
        {
            _logger.LogInformation(
                "Keyword for flow {NewFlowId} interrupted flow {OldFlowId} at node {NodeId} for {Phone}.",
                interrupting.Flow.Id, activeState.FlowId, activeState.CurrentNodeId, MaskPhone(phoneNumber));

            await _stateService.DeleteStateAsync(phoneNumber, connectionId);
            await StartFlowAsync(interrupting, phoneNumber, incomingMessage, connectionId);
            return true;
        }

        var gaveUp = await ResumeFlowAsync(activeState, incomingMessage, connectionId);
        if (!gaveUp) return true;

        // The menu stopped waiting because this message was not one of its options. It was meant
        // for something else, so offer it to the triggers instead of swallowing it.
        if (await EvaluateTriggersAsync(phoneNumber, incomingMessage, connectionId)) return true;

        var closing = MenuGiveUpReply;
        if (!string.IsNullOrWhiteSpace(closing))
        {
            await LogBotMessageAsync(phoneNumber, "Outgoing", closing, activeState.FlowId, activeState.CurrentNodeId);
            await SyncToChatMessagesAsync(phoneNumber, closing, connectionId: connectionId);
        }
        return true;
    }

    /// <summary>Why this session should end before handling the message, or null to keep it.</summary>
    private string? EndReason(ConversationState state)
    {
        var now = DateTime.UtcNow;
        if (now - state.UpdatedAt > SessionTimeout)
            return $"idle for {(now - state.UpdatedAt).TotalMinutes:0} minutes";

        if (ReadVariable(state, StartedAtVariable) is { } started
            && DateTime.TryParse(started, null, System.Globalization.DateTimeStyles.RoundtripKind, out var startedAt)
            && now - startedAt > MaxSessionLength)
            return $"open for {(now - startedAt).TotalMinutes:0} minutes";

        // A flow switched off or deleted mid-conversation stops answering at once.
        if (state.Flow is null || !state.Flow.IsActive) return "flow inactive or deleted";
        return null;
    }

    private static string? ReadVariable(ConversationState state, string key)
    {
        try
        {
            var variables = JsonSerializer.Deserialize<Dictionary<string, string>>(state.VariablesJson);
            return variables is not null && variables.TryGetValue(key, out var value) ? value : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Whether the message is one of the options the waiting node offered.</summary>
    private async Task<bool> IsAnswerToCurrentNodeAsync(ConversationState state, string message)
    {
        var node = (await _flowLoader.GetNodesForFlowAsync(state.FlowId)).FirstOrDefault(n => n.NodeId == state.CurrentNodeId);
        return node is not null && BotFlowTriggers.IsMenuOption(node, message);
    }

    private static string MaskPhone(string phone) =>
        phone.Length <= 4 ? "****" : new string('*', phone.Length - 4) + phone[^4..];

    /// <summary>
    /// Whether this customer has told the bots to stop and the window has not yet lapsed.
    ///
    /// Matches the router's rule exactly: a suppression recorded without a connection applies
    /// everywhere, one recorded against a connection applies to that number only.
    /// </summary>
    private async Task<bool> IsSuppressedAsync(string phoneNumber, int? connectionId)
    {
        try
        {
            var normalized = phoneNumber.Replace("+", string.Empty).Trim();
            var now = DateTime.UtcNow;

            return await _dbContext.BotSuppressions
                .AsNoTracking()
                .AnyAsync(b => b.PhoneNumber == normalized
                               && (b.ConnectionId == null || b.ConnectionId == connectionId)
                               && (b.ResumeAt == null || b.ResumeAt > now));
        }
        catch (Exception ex)
        {
            // Failing open: a database blip must not silence every bot in the product. The router's
            // own check is the primary guard, and it runs first.
            _logger.LogError(ex, "Could not check bot suppression for {Phone}; continuing.", phoneNumber);
            return false;
        }
    }

    /// <summary>Ends any flow this customer is part-way through. Safe to call when there is none.</summary>
    private async Task CloseActiveStateAsync(string phoneNumber, int? connectionId)
    {
        try
        {
            var normalized = phoneNumber.Replace("+", string.Empty).Trim();

            var open = await _dbContext.ConversationStates
                .Where(c => c.PhoneNumber == normalized && c.Status == "Active")
                .ToListAsync();

            if (open.Count == 0) return;

            foreach (var state in open)
            {
                state.Status = "Completed";
                state.UpdatedAt = DateTime.UtcNow;
            }

            await _dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not close the active bot flow for {Phone}.", phoneNumber);
        }
    }

    /// <summary>Feeds the reply to the waiting node. Returns true when a menu gave up on it.</summary>
    private async Task<bool> ResumeFlowAsync(ConversationState state, string incomingMessage, int? connectionId = null)
    {
        await LogBotMessageAsync(state.PhoneNumber, "Incoming", incomingMessage, state.FlowId, state.CurrentNodeId);

        var nodes = await _flowLoader.GetNodesForFlowAsync(state.FlowId);
        var edges = await _flowLoader.GetEdgesForFlowAsync(state.FlowId);

        // The node the customer was left on no longer exists (the flow was edited and its nodes
        // recreated) or this build cannot run it: end the session and treat the message as new.
        var currentNode = nodes.FirstOrDefault(n => n.NodeId == state.CurrentNodeId);
        var executor = currentNode is null ? null : _executors.FirstOrDefault(e => e.NodeType == currentNode.NodeType);
        if (currentNode == null || executor == null)
        {
            _logger.LogWarning(
                "Bot flow {FlowId} has no runnable node {NodeId} ({NodeType}); ending the session for {Phone}.",
                state.FlowId, state.CurrentNodeId, currentNode?.NodeType ?? "missing", MaskPhone(state.PhoneNumber));
            await _stateService.DeleteStateAsync(state.PhoneNumber, connectionId);
            return true;
        }

        var result = await executor.ExecuteAsync(currentNode, state, incomingMessage, edges);
        if (!string.IsNullOrEmpty(result.OutboundMessageText))
        {
            await LogBotMessageAsync(state.PhoneNumber, "Outgoing", result.OutboundMessageText, state.FlowId, state.CurrentNodeId);
            await SyncToChatMessagesAsync(state.PhoneNumber, result.OutboundMessageText, connectionId: connectionId);
        }
        else if (result.SentByExecutor is { } resent)
        {
            // A menu re-sent after an unrecognised reply: already delivered, so only file it.
            var resentText = GetOutboundTextForNode(currentNode);
            if (!string.IsNullOrEmpty(resentText))
            {
                await LogBotMessageAsync(state.PhoneNumber, "Outgoing", resentText, state.FlowId, currentNode.NodeId);
                await SyncToChatMessagesAsync(state.PhoneNumber, resentText, connectionId: connectionId, alreadySent: resent);
            }
        }
        await ProcessResultAsync(state, result, nodes, edges, incomingMessage, connectionId);
        return result.GaveUp;
    }

    private async Task<bool> EvaluateTriggersAsync(string phoneNumber, string incomingMessage, int? connectionId = null)
    {
        var match = await FindTriggeredFlowAsync(incomingMessage, connectionId, exactOnly: false);
        if (match is null) return false;

        await StartFlowAsync(match, phoneNumber, incomingMessage, connectionId);
        return true;
    }

    private sealed record TriggeredFlow(BotFlow Flow, FlowNode TriggerNode, TriggerMatch Match);

    /// <summary>
    /// The flow this message starts, decided the same way every time: the strongest match wins
    /// (an exact keyword beats "contains" and "starts with"), then a flow bound to this connection
    /// beats a global one, then the oldest flow. With <paramref name="exactOnly"/> only an exact
    /// keyword counts, which is what may interrupt a waiting menu.
    /// </summary>
    private async Task<TriggeredFlow?> FindTriggeredFlowAsync(string message, int? connectionId, bool exactOnly)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;

        // Every active flow's Start Trigger for this connection, parsed once and cached until a flow
        // is saved (HotPathCache): this runs for every message a customer sends.
        var triggers = await _cache.GetOrLoadAsync(HotPathCache.BotFlows, $"triggers:{connectionId}", async () =>
        {
            var nodes = await _dbContext.FlowNodes.AsNoTracking()
                .Where(n => n.NodeType == "Start Trigger"
                            && n.Flow.IsActive
                            && (n.Flow.ConnectionId == null || n.Flow.ConnectionId == connectionId))
                .Include(n => n.Flow)
                .OrderBy(n => n.FlowId).ThenBy(n => n.Id)
                .ToListAsync();
            return nodes
                .Select(n => (Node: n, Trigger: BotFlowTriggers.Parse(n)))
                .Where(t => t.Trigger is not null)
                .Select(t => (t.Node, Trigger: t.Trigger!))
                .ToList();
        });

        TriggeredFlow? best = null;
        foreach (var (node, trigger) in triggers)
        {
            var match = BotFlowTriggers.Match(trigger, message);
            if (match == TriggerMatch.None || (exactOnly && match != TriggerMatch.Exact)) continue;

            var candidate = new TriggeredFlow(node.Flow, node, match);
            if (best is null || Ranks(candidate, connectionId) > Ranks(best, connectionId)) best = candidate;
        }

        if (best is not null)
        {
            _logger.LogInformation("Message matched flow {FlowId} \"{FlowName}\" ({Match}).", best.Flow.Id, best.Flow.Name, best.Match);
        }
        return best;

        // Higher wins; ties keep the earlier (lower id) flow because candidates arrive in id order.
        static int Ranks(TriggeredFlow f, int? connectionId) =>
            (int)f.Match * 10 + (connectionId.HasValue && f.Flow.ConnectionId == connectionId ? 1 : 0);
    }

    /// <summary>
    /// Opens a new session on the matched flow and runs it from its Start Trigger. If a step
    /// throws, the session is closed so the customer is not left parked on the trigger, where the
    /// next message of any kind would relaunch the flow.
    /// </summary>
    private async Task StartFlowAsync(TriggeredFlow match, string phoneNumber, string incomingMessage, int? connectionId)
    {
        var flow = match.Flow;
        var triggerNode = match.TriggerNode;
        var nodes = await _flowLoader.GetNodesForFlowAsync(flow.Id);
        var edges = await _flowLoader.GetEdgesForFlowAsync(flow.Id);

        var initialVariables = new Dictionary<string, string>
        {
            [StartedAtVariable] = DateTime.UtcNow.ToString("O")
        };
        if (connectionId.HasValue)
        {
            initialVariables["ConnectionId"] = connectionId.Value.ToString();
        }

        var state = await _stateService.CreateOrUpdateStateAsync(phoneNumber, flow.Id, triggerNode.NodeId, initialVariables, connectionId);

        try
        {
            await LogBotMessageAsync(phoneNumber, "Incoming", incomingMessage, flow.Id, triggerNode.NodeId);

            var executor = _executors.First(e => e.NodeType == "Start Trigger");
            var result = await executor.ExecuteAsync(triggerNode, state, incomingMessage, edges);
            if (!string.IsNullOrEmpty(result.OutboundMessageText))
            {
                await LogBotMessageAsync(phoneNumber, "Outgoing", result.OutboundMessageText, flow.Id, triggerNode.NodeId);
                await SyncToChatMessagesAsync(phoneNumber, result.OutboundMessageText, connectionId: connectionId);
            }

            await ProcessResultAsync(state, result, nodes, edges, incomingMessage, connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bot flow {FlowId} failed for {Phone}; closing the session.", flow.Id, MaskPhone(phoneNumber));
            await _stateService.DeleteStateAsync(phoneNumber, connectionId);
            throw;
        }
    }

    private async Task ProcessResultAsync(
        ConversationState state,
        NodeExecutionResult result,
        List<FlowNode> nodes,
        List<FlowEdge> edges,
        string incomingMessage,
        int? connectionId = null)
    {
        if (result.CollectVariables != null && result.CollectVariables.Any())
        {
            await _stateService.CreateOrUpdateStateAsync(state.PhoneNumber, state.FlowId, state.CurrentNodeId, result.CollectVariables, connectionId);
        }

        if (result.IsCompleted || string.IsNullOrEmpty(result.NextNodeId))
        {
            await _stateService.DeleteStateAsync(state.PhoneNumber, connectionId);
            return;
        }

        if (result.IsWaitingForReply)
        {
            await _stateService.CreateOrUpdateStateAsync(state.PhoneNumber, state.FlowId, result.NextNodeId, new Dictionary<string, string>(), connectionId);
            return;
        }

        var nextNode = nodes.FirstOrDefault(n => n.NodeId == result.NextNodeId);
        if (nextNode == null)
        {
            await _stateService.DeleteStateAsync(state.PhoneNumber, connectionId);
            return;
        }

        var executor = _executors.FirstOrDefault(e => e.NodeType == nextNode.NodeType);
        if (executor == null)
        {
            await _stateService.DeleteStateAsync(state.PhoneNumber, connectionId);
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

            // Buttons, lists, locations and contact cards were already sent by their executor as an
            // interactive message; sending outboundText as well is what made "Please choose an
            // option:" arrive twice. File the executor's message instead, under its WhatsApp id.
            await SyncToChatMessagesAsync(state.PhoneNumber, outboundText, mediaUrl, mediaType, mediaFileName, connectionId, nextResult.SentByExecutor);
        }

        await ProcessResultAsync(state, nextResult, nodes, edges, incomingMessage, connectionId);
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

    private Task LogBotMessageAsync(string phone, string direction, string text, int flowId, string nodeId)
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
        // Saved with the turn's next write (or the final flush in ExecuteFlowStepAsync).
        _dbContext.Messages.Add(msg);
        return Task.CompletedTask;
    }

    private async Task SyncToChatMessagesAsync(
        string phone, 
        string text, 
        string? mediaUrl = null, 
        string? mediaType = null, 
        string? mediaFileName = null,
        int? connectionId = null,
        WhatsAppSendResult? alreadySent = null)
    {
        try
        {
            var normalizedPhone = Helpers.PhoneNumberHelper.NormalizePhoneNumber(phone);
            var target = await ResolveChatTargetAsync(normalizedPhone, connectionId);
            if (target is null) return;
            var (contact, conversation, resolvedPhoneNumberId) = target.Value;

            conversation.LastMessageText = string.IsNullOrEmpty(mediaUrl) ? text : $"[Sent {mediaType ?? "media"}]";
            conversation.LastMessageAt = DateTime.UtcNow;

            WhatsAppSendResult sendResult;
            if (alreadySent is not null)
            {
                // The node's executor already sent its own (interactive) message: record that one.
                sendResult = alreadySent;
            }
            else if (!string.IsNullOrEmpty(mediaUrl) && !string.IsNullOrEmpty(mediaType))
            {
                sendResult = await _whatsAppService.SendMediaMessageAsync(
                    normalizedPhone,
                    mediaUrl,
                    mediaType,
                    mediaFileName,
                    text,
                    resolvedPhoneNumberId,
                    connectionId);
            }
            else
            {
                sendResult = await _whatsAppService.SendTextMessageAsync(normalizedPhone, text, resolvedPhoneNumberId, connectionId);
            }

            _dbContext.ChatMessages.Add(new ChatMessage
            {
                ConversationId = conversation.Id,
                ContactId = contact.Id,
                ConnectionId = connectionId,
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

    /// <summary>
    /// Who a bot line goes to and where it is filed: the contact, their conversation on this
    /// connection, and the number to send from. Resolved once per turn (this service lives for one
    /// message) — a menu turn sends several lines, and each used to repeat these lookups.
    /// </summary>
    private async Task<(Contact Contact, ChatConversation Conversation, string? PhoneNumberId)?> ResolveChatTargetAsync(string normalizedPhone, int? connectionId)
    {
        if (_chatTarget is { } known && known.Phone == normalizedPhone && known.ConnectionId == connectionId)
        {
            return (known.Contact, known.Conversation, known.PhoneNumberId);
        }

        var contact = await _dbContext.Contacts.FirstOrDefaultAsync(c => c.Phone == normalizedPhone);
        if (contact == null) return null;

        // The sending number of a connection rarely changes: cached (cleared when numbers are saved).
        var account = await _cache.GetOrLoadAsync(HotPathCache.Connections, $"first-phone:{connectionId}", () =>
            connectionId.HasValue
                ? _dbContext.WabaPhoneNumbers.AsNoTracking().Where(p => p.ConnectionId == connectionId.Value)
                    .OrderBy(p => p.Id).Select(p => new { p.Id, p.PhoneNumberId }).FirstOrDefaultAsync()
                : _dbContext.WabaPhoneNumbers.AsNoTracking()
                    .OrderBy(p => p.Id).Select(p => new { p.Id, p.PhoneNumberId }).FirstOrDefaultAsync());

        var conversation = await _dbContext.ChatConversations
            .FirstOrDefaultAsync(c => c.ContactId == contact.Id && (connectionId == null || c.ConnectionId == connectionId));

        if (conversation == null)
        {
            conversation = new ChatConversation
            {
                ContactId = contact.Id,
                ConnectionId = connectionId,
                WabaPhoneNumberId = account?.Id
            };
            _dbContext.ChatConversations.Add(conversation);
            await _dbContext.SaveChangesAsync();
        }
        else if (conversation.ConnectionId == null && connectionId.HasValue)
        {
            conversation.ConnectionId = connectionId;
        }

        var phoneNumberId = connectionId.HasValue ? account?.PhoneNumberId : null;
        _chatTarget = (normalizedPhone, connectionId, contact, conversation, phoneNumberId);
        return (contact, conversation, phoneNumberId);
    }

    private (string Phone, int? ConnectionId, Contact Contact, ChatConversation Conversation, string? PhoneNumberId)? _chatTarget;

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
