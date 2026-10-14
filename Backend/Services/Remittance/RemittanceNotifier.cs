using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services.Remittance;

public interface IRemittanceNotifier
{
    /// <summary>"Order created — pay here". Returns false when WhatsApp refused the message.</summary>
    Task<bool> SendOrderCreatedAsync(int orderId, CancellationToken ct);

    /// <summary>A later status change (payment received, transfer completed, failed, expired).</summary>
    Task SendStatusAsync(int orderId, string headline, string? detail, CancellationToken ct);
}

/// <summary>
/// Tells the customer about their transfer on WhatsApp and files each message in the inbox, so an
/// agent opening the conversation sees the same thing the customer saw.
/// </summary>
/// <remarks>
/// These are free-form messages, which WhatsApp only allows within 24 hours of the customer's last
/// message. That holds for the order and payment steps (the customer has just used the Flow); a
/// payout that completes days later needs an approved utility template — see the walkthrough's
/// production gaps.
/// </remarks>
public sealed class RemittanceNotifier : IRemittanceNotifier
{
    private readonly AppDbContext _db;
    private readonly IWhatsAppService _whatsApp;
    private readonly ILogger<RemittanceNotifier> _logger;

    public RemittanceNotifier(AppDbContext db, IWhatsAppService whatsApp, ILogger<RemittanceNotifier> logger)
    {
        _db = db;
        _whatsApp = whatsApp;
        _logger = logger;
    }

    public async Task<bool> SendOrderCreatedAsync(int orderId, CancellationToken ct)
    {
        var order = await _db.TransferOrders.AsNoTracking().Include(o => o.Contact).FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null) return false;

        var corridor = await _db.RemittanceCorridors.AsNoTracking().FirstOrDefaultAsync(c => c.Id == order.CorridorId, ct);
        var method = corridor is null ? null : await _db.RemittancePaymentMethods.AsNoTracking()
            .FirstOrDefaultAsync(m => m.SourceCountryCode == corridor.SourceCountryCode && m.Code == order.PaymentMethodCode, ct);

        var receive = RemittancePricing.Format(order.ReceiveCurrency, order.ReceiveAmount, corridor?.DestinationDecimals ?? 2);
        var total = RemittancePricing.Format(order.SendCurrency, order.TotalPayable);
        var minutes = order.PaymentExpiresAt is { } expires ? Math.Max(1, (int)Math.Round((expires - DateTime.UtcNow).TotalMinutes)) : (int?)null;

        var lines = new List<string>
        {
            "Your transfer request has been recorded.",
            "",
            $"Beneficiary: {order.BeneficiaryName}",
            $"Receiving: {receive}",
        };
        if (!string.IsNullOrWhiteSpace(order.InstitutionName)) lines.Add($"Bank: {order.InstitutionName}");
        lines.Add($"Payment: {method?.Label ?? order.PaymentMethodCode}");
        lines.Add($"Total: {total}");
        lines.Add("");

        object payload;
        string logText;
        if (!string.IsNullOrWhiteSpace(order.PaymentUrl))
        {
            lines.Add(minutes is { } m
                ? $"Your order is held for {m} minutes. Tap below to pay securely on our payment partner's page."
                : "Tap below to pay securely on our payment partner's page.");

            payload = new JsonObject
            {
                ["messaging_product"] = "whatsapp",
                ["recipient_type"] = "individual",
                ["to"] = WhatsAppCampaignApi.Helpers.PhoneNumberHelper.FormatForWhatsApp(order.Contact.Phone),
                ["type"] = "interactive",
                ["interactive"] = new JsonObject
                {
                    ["type"] = "cta_url",
                    ["header"] = new JsonObject { ["type"] = "text", ["text"] = $"Order created · {order.Reference}" },
                    ["body"] = new JsonObject { ["text"] = string.Join("\n", lines) },
                    ["footer"] = new JsonObject { ["text"] = "Never share your banking password or OTP in chat." },
                    ["action"] = new JsonObject
                    {
                        ["name"] = "cta_url",
                        ["parameters"] = new JsonObject
                        {
                            // Meta caps the button at 20 characters.
                            ["display_text"] = Truncate($"Pay {total}", 20),
                            ["url"] = order.PaymentUrl
                        }
                    }
                }
            };
            logText = $"[Transfer|Ref: {order.Reference}|Status: Awaiting payment|Amount: {total}|Receive: {receive}|Link: {order.PaymentUrl}]";
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(order.PaymentReference)) lines.Add($"Payment reference: {order.PaymentReference}");
            lines.Add(minutes is { } m ? $"Please pay within {m} minutes to keep this rate." : "Please complete your payment to keep this rate.");

            payload = new JsonObject
            {
                ["messaging_product"] = "whatsapp",
                ["recipient_type"] = "individual",
                ["to"] = WhatsAppCampaignApi.Helpers.PhoneNumberHelper.FormatForWhatsApp(order.Contact.Phone),
                ["type"] = "text",
                ["text"] = new JsonObject { ["body"] = $"*Order created · {order.Reference}*\n\n" + string.Join("\n", lines) }
            };
            logText = $"[Transfer|Ref: {order.Reference}|Status: Awaiting payment|Amount: {total}|Receive: {receive}|Detail: Reference {order.PaymentReference}]";
        }

        var result = await _whatsApp.SendCustomPayloadAsync(order.Contact.Phone, payload, null, order.ConnectionId);
        await LogAsync(order, logText, result, ct);
        return result.Success;
    }

    public async Task SendStatusAsync(int orderId, string headline, string? detail, CancellationToken ct)
    {
        var order = await _db.TransferOrders.AsNoTracking().Include(o => o.Contact).FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null) return;

        var text = $"*{headline}* · {order.Reference}" + (string.IsNullOrWhiteSpace(detail) ? string.Empty : $"\n\n{detail}");
        var result = await _whatsApp.SendTextMessageAsync(order.Contact.Phone, text, null, order.ConnectionId);
        await LogAsync(order, $"[Transfer|Ref: {order.Reference}|Status: {headline}|Amount: {RemittancePricing.Format(order.SendCurrency, order.TotalPayable)}|Detail: {detail}]", result, ct);
    }

    private async Task LogAsync(TransferOrder order, string text, WhatsAppSendResult result, CancellationToken ct)
    {
        try
        {
            var conversation = await _db.ChatConversations
                .FirstOrDefaultAsync(c => c.ContactId == order.ContactId
                                       && c.Channel == MessageChannel.WhatsApp
                                       && (order.ConnectionId == null || c.ConnectionId == order.ConnectionId), ct);
            if (conversation is null)
            {
                conversation = new ChatConversation { ContactId = order.ContactId, ConnectionId = order.ConnectionId };
                _db.ChatConversations.Add(conversation);
            }

            conversation.LastMessageText = text;
            conversation.LastMessageAt = DateTime.UtcNow;

            _db.ChatMessages.Add(new ChatMessage
            {
                Conversation = conversation,
                ContactId = order.ContactId,
                ConnectionId = order.ConnectionId,
                WhatsAppMessageId = result.Success ? result.MessageId : null,
                Direction = ChatMessageDirection.Outgoing,
                Status = result.Success ? ChatMessageStatus.Sent : ChatMessageStatus.Failed,
                ErrorMessage = result.Success ? null : result.ErrorMessage,
                Text = text
            });

            _db.TransferOrderEvents.Add(new TransferOrderEvent
            {
                TransferOrderId = order.Id,
                Kind = "Notification",
                Source = "System",
                Detail = Truncate(result.Success ? $"WhatsApp message sent: {text}" : $"WhatsApp message failed: {result.ErrorMessage}", 500)
            });

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The message went (or did not) either way; a lost log row must not fail the caller.
            _logger.LogWarning(ex, "Could not file the transfer message for order {OrderId} in the inbox.", order.Id);
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
