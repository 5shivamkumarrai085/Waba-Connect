using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Models.Options;
using WhatsAppCampaignApi.Services;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Services.Remittance;

namespace WhatsAppCampaignApi.Services.WhatsApp.Flows;

/// <summary>The opening message of a Flow: what the customer sees before tapping the button.</summary>
public sealed record FlowInvitation(string? Header, string Body, string? Footer, string Cta);

/// <param name="CustomerMessage">What to tell the customer when the launch failed: null for the standard apology, empty to send nothing (anything sent would be refused too).</param>
public sealed record FlowLaunchResult(bool Sent, string? Error, string? MessageId, string? CustomerMessage = null);

public interface IWhatsAppFlowLauncher
{
    /// <summary>
    /// Starts a Send Money session for the contact and sends the Flow message. The connection is the
    /// conversation's; when the caller has none, the one connection with a Send Money Flow is used.
    /// </summary>
    Task<FlowLaunchResult> LaunchSendMoneyAsync(int contactId, int? connectionId, FlowInvitation invitation, CancellationToken ct);
}

public sealed class WhatsAppFlowLauncher : IWhatsAppFlowLauncher
{
    private readonly AppDbContext _db;
    private readonly IRemittanceSessionService _sessions;
    private readonly IWhatsAppService _whatsApp;
    private readonly IOptionsMonitor<RemittanceOptions> _options;
    private readonly ILogger<WhatsAppFlowLauncher> _logger;
    private readonly HotPathCache _cache;

    public WhatsAppFlowLauncher(
        AppDbContext db,
        IRemittanceSessionService sessions,
        IWhatsAppService whatsApp,
        IOptionsMonitor<RemittanceOptions> options,
        ILogger<WhatsAppFlowLauncher> logger,
        HotPathCache cache)
    {
        _cache = cache;
        _db = db;
        _sessions = sessions;
        _whatsApp = whatsApp;
        _options = options;
        _logger = logger;
    }

    public async Task<FlowLaunchResult> LaunchSendMoneyAsync(int contactId, int? connectionId, FlowInvitation invitation, CancellationToken ct)
    {
        if (!_options.CurrentValue.Enabled) return new(false, "Send Money is switched off (Remittance:Enabled).", null);

        var contact = await _db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == contactId, ct);
        if (contact is null) return new(false, "Contact not found.", null);

        // Which Flow to send, and whether it is published: cached until the Flow is synced, linked
        // or published (those saves clear HotPathCache.SendMoney).
        var registration = await _cache.GetOrLoadAsync(HotPathCache.SendMoney, $"registration:{connectionId}", () =>
        {
            var registrations = _db.WhatsAppFlowRegistrations.AsNoTracking()
                .Where(r => r.Purpose == WhatsAppFlowRegistration.SendMoneyPurpose && r.MetaFlowId != null);
            return connectionId is { } id
                ? registrations.FirstOrDefaultAsync(r => r.ConnectionId == id, ct)
                : SingleOrNullAsync(registrations, ct);
        });
        if (registration is null)
        {
            return new(false, "There is no Send Money Flow on this WhatsApp connection. Create it under Connections › WhatsApp Flows.", null);
        }

        var published = registration.Status == "PUBLISHED";
        if (!published && !_options.CurrentValue.Flows.AllowDraftSend)
        {
            return new(false, "The Send Money Flow is not published yet (set Remittance:Flows:AllowDraftSend to test the draft).", null);
        }

        RemittanceSession session;
        string token;
        try
        {
            (session, token) = await _sessions.StartAsync(contact.Id, PhoneNumberHelper.FormatForWhatsApp(contact.Phone), registration.ConnectionId, ct);
        }
        catch (RemittanceLaunchLimitException ex)
        {
            _logger.LogWarning("Send Money not launched for contact {ContactId}: {Reason}", contact.Id, ex.Message);
            return new(false, ex.Message, null,
                "You've asked for the Send Money form several times in the last few minutes. Please use the latest one, or try again shortly.");
        }

        var parameters = new JsonObject
        {
            ["flow_message_version"] = "3",
            ["flow_token"] = token,
            ["flow_id"] = registration.MetaFlowId,
            ["flow_cta"] = Truncate(invitation.Cta, 30),
            // data_exchange: opening the Flow calls our endpoint (INIT), which issues the OTP.
            ["flow_action"] = "data_exchange"
        };
        if (!published) parameters["mode"] = "draft";

        var interactive = new JsonObject
        {
            ["type"] = "flow",
            ["body"] = new JsonObject { ["text"] = Truncate(invitation.Body, 1024) },
            ["action"] = new JsonObject { ["name"] = "flow", ["parameters"] = parameters }
        };
        if (!string.IsNullOrWhiteSpace(invitation.Header)) interactive["header"] = new JsonObject { ["type"] = "text", ["text"] = Truncate(invitation.Header, 60) };
        if (!string.IsNullOrWhiteSpace(invitation.Footer)) interactive["footer"] = new JsonObject { ["text"] = Truncate(invitation.Footer, 60) };

        var payload = new JsonObject
        {
            ["messaging_product"] = "whatsapp",
            ["recipient_type"] = "individual",
            ["to"] = PhoneNumberHelper.FormatForWhatsApp(contact.Phone),
            ["type"] = "interactive",
            ["interactive"] = interactive
        };

        var result = await _whatsApp.SendCustomPayloadAsync(contact.Phone, payload, null, registration.ConnectionId);
        await LogAsync(contact.Id, registration.ConnectionId, $"[FlowSent|Flow: Send Money|Text: {invitation.Body.Replace('|', '/')}|CTA: {invitation.Cta.Replace('|', '/')}]", result, ct);

        if (!result.Success)
        {
            session.Status = RemittanceSessionStatus.Cancelled;
            await _db.SaveChangesAsync(ct);

            // What the customer is told depends on why WhatsApp refused it.
            string? customerMessage = null;
            if (result.MetaErrorCode == 131047)
            {
                // Outside the 24-hour window: any reply would be refused the same way, so none is sent.
                customerMessage = string.Empty;
                _logger.LogWarning("Send Money Flow not sent to contact {ContactId}: outside the 24-hour window (131047).", contact.Id);
            }
            else if (result.MetaErrorCode is >= 139000 and < 140000)
            {
                // The Flow itself is blocked, throttled or not usable on this account: an
                // operator problem, not the customer's. Logged as an error so it is noticed.
                _logger.LogError("Send Money Flow {FlowId} refused by WhatsApp on connection {ConnectionId} ({Code}): {Error}",
                    registration.MetaFlowId, registration.ConnectionId, result.MetaErrorCode, result.ErrorMessage);
            }
            else
            {
                _logger.LogWarning("Send Money Flow could not be sent to contact {ContactId} ({Code}): {Error}",
                    contact.Id, result.MetaErrorCode, result.ErrorMessage);
            }
            return new(false, result.ErrorMessage, null, customerMessage);
        }

        return new(true, null, result.MessageId);
    }

    private static async Task<WhatsAppFlowRegistration?> SingleOrNullAsync(IQueryable<WhatsAppFlowRegistration> query, CancellationToken ct)
    {
        var two = await query.Take(2).ToListAsync(ct);
        return two.Count == 1 ? two[0] : null;
    }

    private async Task LogAsync(int contactId, int connectionId, string text, WhatsAppSendResult result, CancellationToken ct)
    {
        var conversation = await _db.ChatConversations.FirstOrDefaultAsync(c =>
            c.ContactId == contactId && c.Channel == MessageChannel.WhatsApp && (c.ConnectionId == connectionId || c.ConnectionId == null), ct);
        if (conversation is null)
        {
            conversation = new ChatConversation { ContactId = contactId, ConnectionId = connectionId };
            _db.ChatConversations.Add(conversation);
        }
        conversation.LastMessageText = text;
        conversation.LastMessageAt = DateTime.UtcNow;

        _db.ChatMessages.Add(new ChatMessage
        {
            Conversation = conversation,
            ContactId = contactId,
            ConnectionId = connectionId,
            WhatsAppMessageId = result.Success ? result.MessageId : null,
            Direction = ChatMessageDirection.Outgoing,
            Status = result.Success ? ChatMessageStatus.Sent : ChatMessageStatus.Failed,
            ErrorMessage = result.Success ? null : result.ErrorMessage,
            Text = text
        });
        await _db.SaveChangesAsync(ct);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}

/// <summary>Handles a Flow's completion message (interactive type "nfm_reply") from the messages webhook.</summary>
public interface IWhatsAppFlowCompletionHandler
{
    Task HandleAsync(string phoneNumber, int? connectionId, string? responseJson, CancellationToken ct);
}

public sealed class WhatsAppFlowCompletionHandler : IWhatsAppFlowCompletionHandler
{
    private readonly AppDbContext _db;
    private readonly IRemittanceSessionService _sessions;
    private readonly IOrderCreatedNotification _orderCreated;
    private readonly ILogger<WhatsAppFlowCompletionHandler> _logger;

    public WhatsAppFlowCompletionHandler(AppDbContext db, IRemittanceSessionService sessions, IOrderCreatedNotification orderCreated,
        ILogger<WhatsAppFlowCompletionHandler> logger)
    {
        _db = db;
        _sessions = sessions;
        _orderCreated = orderCreated;
        _logger = logger;
    }

    public async Task HandleAsync(string phoneNumber, int? connectionId, string? responseJson, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(responseJson)) return;

        string? token, reference;
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            token = doc.RootElement.TryGetProperty("flow_token", out var t) ? t.GetString() : null;
            reference = doc.RootElement.TryGetProperty("order_reference", out var r) ? r.GetString() : null;
        }
        catch (JsonException)
        {
            _logger.LogWarning("A Flow completion from {Phone} carried an unreadable response_json.", PhoneNumberHelper.Mask(phoneNumber));
            return;
        }

        if (token is null || !token.StartsWith(RemittanceSessionService.TokenPrefix + "_", StringComparison.Ordinal)) return;

        // The token proves which session; the reference must belong to that session's order.
        var session = await _sessions.FindByTokenAsync(token, ct);
        if (session is null) return;

        var orderId = await _db.TransferOrders.AsNoTracking()
            .Where(o => o.SessionId == session.Id && o.Reference == reference)
            .Select(o => (int?)o.Id).FirstOrDefaultAsync(ct);
        if (orderId is null)
        {
            _logger.LogWarning("Flow completion for session {SessionId} names order {Reference}, which is not its order.", session.Id, reference);
            return;
        }

        await _orderCreated.SendOnceAsync(orderId.Value, ct);
    }
}
