using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Services.WhatsApp.Flows;

namespace WhatsAppCampaignApi.Executors;

/// <summary>
/// Bot Flow node that hands the customer a native WhatsApp Flow (for now: Send Money).
/// </summary>
/// <remarks>
/// <para>
/// Node data (set in the Bot Flow designer): <c>flowPurpose</c> ("send_money"), <c>headerText</c>,
/// <c>bodyText</c>, <c>footerText</c>, <c>ctaText</c>.
/// </para>
/// <para>
/// Sending the Flow message ends the bot flow: from here the WhatsApp Flow and its data endpoint
/// own the conversation. The launcher files the message in the inbox itself, so on success this
/// executor returns an empty OutboundMessageText — anything else would make FlowExecutionService
/// send the body a second time as plain text. When WhatsApp refuses the Flow, the apology is
/// returned as OutboundMessageText so the engine sends and files it like any other bot reply.
/// </para>
/// </remarks>
public class WhatsAppFlowNodeExecutor : INodeExecutor
{
    private readonly IWhatsAppFlowLauncher _launcher;
    private readonly AppDbContext _db;
    private readonly ILogger<WhatsAppFlowNodeExecutor> _logger;

    public WhatsAppFlowNodeExecutor(IWhatsAppFlowLauncher launcher, AppDbContext db, ILogger<WhatsAppFlowNodeExecutor> logger)
    {
        _launcher = launcher;
        _db = db;
        _logger = logger;
    }

    public string NodeType => "WhatsApp Flow";

    public async Task<NodeExecutionResult> ExecuteAsync(FlowNode node, ConversationState state, string incomingMessage, List<FlowEdge> outgoingEdges)
    {
        string purpose = WhatsAppFlowRegistration.SendMoneyPurpose, header = "", body = "", footer = "", cta = "";
        try
        {
            using var doc = JsonDocument.Parse(node.DataJson);
            var root = doc.RootElement;
            purpose = Read(root, "flowPurpose") ?? purpose;
            header = Read(root, "headerText") ?? "";
            body = Read(root, "bodyText") ?? "";
            footer = Read(root, "footerText") ?? "";
            cta = Read(root, "ctaText") ?? "";
        }
        catch (JsonException)
        {
            // An unreadable node falls back to the defaults below.
        }

        if (string.IsNullOrWhiteSpace(body)) body = "Send money abroad in a few taps, with a guaranteed rate.";
        if (string.IsNullOrWhiteSpace(cta)) cta = "Send money";

        // Always Complete: this node ends the bot flow whatever happens, so a failure can never
        // leave the customer parked here, relaunching on every message they send.
        var result = NodeExecutionResult.Complete();
        result.OutboundMessageText = string.Empty;

        if (purpose != WhatsAppFlowRegistration.SendMoneyPurpose)
        {
            _logger.LogWarning("Bot node {NodeId} asks for unknown WhatsApp Flow purpose '{Purpose}'.", node.NodeId, purpose);
            result.OutboundMessageText = Unavailable;
            return result;
        }

        try
        {
            var plus = PhoneNumberHelper.NormalizePhoneNumber(state.PhoneNumber);
            var bare = PhoneNumberHelper.FormatForWhatsApp(state.PhoneNumber);
            var contact = await _db.Contacts.AsNoTracking()
                .Where(c => c.Phone == plus || c.Phone == bare)
                .Select(c => new { c.Id })
                .FirstOrDefaultAsync();
            if (contact is null)
            {
                _logger.LogWarning("Send Money Flow not launched: no contact for {Phone}.", Mask(state.PhoneNumber));
                result.OutboundMessageText = Unavailable;
                return result;
            }

            var launch = await _launcher.LaunchSendMoneyAsync(contact.Id, state.ConnectionId,
                new FlowInvitation(header, body, footer, cta), CancellationToken.None);

            if (!launch.Sent)
            {
                _logger.LogWarning("Send Money Flow not launched for {Phone}: {Error}", Mask(state.PhoneNumber), launch.Error);
                // Tell the customer rather than leaving them waiting on a message that never comes.
                // Handed back to FlowExecutionService, which sends it and files it in the inbox, so
                // WhatsApp's delivery statuses for it find their message. The launcher leaves the
                // text empty when sending anything would be refused too (outside the 24-hour window).
                result.OutboundMessageText = launch.CustomerMessage ?? Unavailable;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Send Money Flow launch failed for {Phone}.", Mask(state.PhoneNumber));
            result.OutboundMessageText = Unavailable;
        }

        return result;
    }

    private const string Unavailable = "Sorry, Send Money isn't available right now. Please try again later.";

    private static string Mask(string phone) => phone.Length <= 4 ? "****" : new string('*', phone.Length - 4) + phone[^4..];

    private static string? Read(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
