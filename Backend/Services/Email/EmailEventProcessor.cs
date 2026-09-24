using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// Applies an SES delivery event to the recipient it concerns.
/// </summary>
public interface IEmailEventProcessor
{
    /// <summary>
    /// Records the event and updates delivery state. Returns false when the notification was a
    /// duplicate or could not be attributed — neither of which is an error worth reporting back
    /// to SNS, because retrying would produce the same outcome.
    /// </summary>
    Task<bool> ProcessAsync(string snsMessageId, string rawMessage, CancellationToken ct = default);
}

/// <inheritdoc />
public class EmailEventProcessor : IEmailEventProcessor
{
    private readonly AppDbContext _dbContext;
    private readonly IEmailSuppressionService _suppression;
    private readonly ILogger<EmailEventProcessor> _logger;

    public EmailEventProcessor(
        AppDbContext dbContext,
        IEmailSuppressionService suppression,
        ILogger<EmailEventProcessor> logger)
    {
        _dbContext = dbContext;
        _suppression = suppression;
        _logger = logger;
    }

    public async Task<bool> ProcessAsync(string snsMessageId, string rawMessage, CancellationToken ct = default)
    {
        // Idempotency, before anything else. SNS guarantees at-least-once delivery and redelivers
        // freely; without this a replayed bounce would suppress an address twice and double-count
        // the campaign's failures.
        if (await _dbContext.EmailDeliveryEvents.AnyAsync(e => e.SnsMessageId == snsMessageId, ct))
        {
            _logger.LogDebug("Ignoring SNS notification {SnsMessageId}, already processed.", snsMessageId);
            return false;
        }

        SesEventNotification? notification;
        try
        {
            notification = JsonSerializer.Deserialize<SesEventNotification>(rawMessage);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Could not parse an SES event notification.");
            return false;
        }

        var providerMessageId = notification?.Mail?.MessageId;
        if (notification is null || string.IsNullOrWhiteSpace(providerMessageId))
        {
            _logger.LogWarning("An SES event arrived with no message id; cannot attribute it.");
            return false;
        }

        var eventType = MapEventType(notification.ResolvedEventType);
        if (eventType is null)
        {
            // An event type this build does not handle. Recorded rather than dropped, so a future
            // version can see it happened.
            _logger.LogInformation(
                "Recording an unhandled SES event type '{EventType}'.", notification.ResolvedEventType);
        }

        // Correlated by provider message id. Nullable: events regularly arrive before the send
        // row has finished committing, and the event log is still worth keeping.
        var recipient = await _dbContext.CampaignContacts
            .IgnoreQueryFilters()
            .Include(cc => cc.Contact)
            .FirstOrDefaultAsync(cc => cc.ProviderMessageId == providerMessageId, ct);

        var chatMessage = await _dbContext.ChatMessages
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.ProviderMessageId == providerMessageId, ct);

        var campaign = recipient is not null
            ? await _dbContext.Campaigns.IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Id == recipient.CampaignId, ct)
            : null;

        var occurredAt = ResolveTimestamp(notification);
        var recipientAddress = ResolveRecipientAddress(notification, recipient);

        var deliveryEvent = new EmailDeliveryEvent
        {
            SnsMessageId = snsMessageId,
            ProviderMessageId = providerMessageId,
            EventType = eventType ?? EmailEventType.Send,
            OccurredAt = occurredAt,
            CampaignContactId = recipient?.Id,
            ChatMessageId = chatMessage?.Id,
            RecipientAddress = Truncate(recipientAddress, 255),

            // Redacted through the same helper the WhatsApp activity log uses, so a payload
            // carrying anything sensitive is treated identically on both channels.
            PayloadJson = PayloadRedactor.Redact(rawMessage)
        };

        switch (eventType)
        {
            case EmailEventType.Bounce:
                ApplyBounce(notification, deliveryEvent);
                break;

            case EmailEventType.Click:
                deliveryEvent.LinkUrl = Truncate(notification.Click?.Link, 2000);
                deliveryEvent.UserAgent = Truncate(notification.Click?.UserAgent, 500);
                deliveryEvent.IpAddress = Truncate(notification.Click?.IpAddress, 64);
                break;

            case EmailEventType.Open:
                deliveryEvent.UserAgent = Truncate(notification.Open?.UserAgent, 500);
                deliveryEvent.IpAddress = Truncate(notification.Open?.IpAddress, 64);
                break;

            case EmailEventType.Reject:
                deliveryEvent.DiagnosticCode = Truncate(notification.Reject?.Reason, 2000);
                break;

            case EmailEventType.DeliveryDelay:
                deliveryEvent.DiagnosticCode = Truncate(notification.DeliveryDelay?.DelayType, 2000);
                break;
        }

        _dbContext.EmailDeliveryEvents.Add(deliveryEvent);

        if (recipient is not null && eventType is not null)
        {
            ApplyToRecipient(recipient, chatMessage, eventType.Value, notification, occurredAt);
        }

        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Lost a race with a concurrent redelivery of the same notification. The unique index
            // on SnsMessageId did its job, and the event is already recorded.
            _dbContext.ChangeTracker.Clear();
            _logger.LogDebug(ex, "SNS notification {SnsMessageId} was processed concurrently.", snsMessageId);
            return false;
        }

        // Suppression after the state change commits. Doing it first would risk suppressing an
        // address for an event that then failed to record.
        await ApplySuppressionAsync(eventType, notification, campaign?.ConnectionId, ct);

        if (recipient is not null)
        {
            await RecalculateCampaignCountsAsync(recipient.CampaignId, ct);
        }

        return true;
    }

    /// <summary>
    /// Moves the recipient's delivery state forward, never backwards.
    ///
    /// <para>
    /// The monotonic guard matters because events do not arrive in order — a Delivery can land
    /// after an Open, and without this the recipient would regress from Delivered to Sent. This
    /// mirrors the guard the WhatsApp status handler already applies, deliberately in a separate
    /// service so that one is untouched.
    /// </para>
    /// </summary>
    private void ApplyToRecipient(
        CampaignContact recipient,
        ChatMessage? chatMessage,
        EmailEventType eventType,
        SesEventNotification notification,
        DateTime occurredAt)
    {
        switch (eventType)
        {
            case EmailEventType.Send:
                if (recipient.Status == MessageStatus.Pending)
                {
                    recipient.Status = MessageStatus.Sent;
                    recipient.SentAt ??= occurredAt;
                }
                break;

            case EmailEventType.Delivery:
                if (recipient.Status is MessageStatus.Pending or MessageStatus.Sent)
                {
                    recipient.Status = MessageStatus.Delivered;
                }
                recipient.SentAt ??= occurredAt;
                recipient.DeliveredAt ??= occurredAt;

                if (chatMessage is not null
                    && chatMessage.Status is ChatMessageStatus.Pending or ChatMessageStatus.Sent)
                {
                    chatMessage.Status = ChatMessageStatus.Delivered;
                    chatMessage.DeliveredAt ??= occurredAt;
                }
                break;

            case EmailEventType.Bounce:
                // Only a permanent bounce is terminal. A transient one is a deferral — the remote
                // server was busy or the mailbox full — and marking it Bounced would write off a
                // recipient SES is still trying to reach.
                if (IsPermanentBounce(notification))
                {
                    recipient.Status = MessageStatus.Bounced;
                    recipient.ErrorMessage = Truncate(BuildBounceMessage(notification), 500);

                    if (chatMessage is not null)
                    {
                        chatMessage.Status = ChatMessageStatus.Failed;
                        chatMessage.ErrorMessage = Truncate(BuildBounceMessage(notification), 1000);
                    }
                }
                break;

            case EmailEventType.Complaint:
                // Terminal regardless of what came before. Someone pressed "this is spam", and
                // continuing to count the message as delivered would misrepresent the campaign.
                recipient.Status = MessageStatus.Complained;
                recipient.ErrorMessage = Truncate(
                    $"The recipient reported this message as spam"
                  + $"{(notification.Complaint?.ComplaintFeedbackType is { } type ? $" ({type})" : "")}.", 500);
                break;

            case EmailEventType.Reject:
            case EmailEventType.RenderingFailure:
                recipient.Status = MessageStatus.Failed;
                recipient.ErrorMessage = Truncate(
                    notification.Reject?.Reason ?? "The provider rejected the message.", 500);

                if (chatMessage is not null) chatMessage.Status = ChatMessageStatus.Failed;
                break;

            // Open and Click deliberately change no delivery state. Engagement is not delivery,
            // and treating an open as a status would let it overwrite Delivered — or, worse,
            // resurrect a bounced recipient.
            case EmailEventType.Open:
            case EmailEventType.Click:
            case EmailEventType.DeliveryDelay:
            case EmailEventType.Subscription:
                break;
        }
    }

    private void ApplyBounce(SesEventNotification notification, EmailDeliveryEvent deliveryEvent)
    {
        deliveryEvent.BounceType = notification.Bounce?.BounceType switch
        {
            "Permanent" => EmailBounceType.Permanent,
            "Transient" => EmailBounceType.Transient,
            _ => EmailBounceType.Undetermined
        };

        deliveryEvent.BounceSubType = Truncate(notification.Bounce?.BounceSubType, 100);
        deliveryEvent.DiagnosticCode = Truncate(
            notification.Bounce?.BouncedRecipients?.FirstOrDefault()?.DiagnosticCode, 2000);
    }

    /// <summary>
    /// Adds addresses to the suppression list when an event says never to mail them again.
    ///
    /// <para>
    /// Only permanent bounces and complaints. Suppressing on a transient bounce would
    /// permanently lose a recipient because their mailbox was briefly full, and suppressing on an
    /// undetermined one would do the same on a guess.
    /// </para>
    /// </summary>
    private async Task ApplySuppressionAsync(
        EmailEventType? eventType,
        SesEventNotification notification,
        int? connectionId,
        CancellationToken ct)
    {
        if (eventType == EmailEventType.Bounce && IsPermanentBounce(notification))
        {
            foreach (var bounced in notification.Bounce?.BouncedRecipients ?? [])
            {
                if (string.IsNullOrWhiteSpace(bounced.EmailAddress)) continue;

                await _suppression.SuppressAsync(
                    bounced.EmailAddress,
                    SuppressionReason.Bounce,
                    source: $"SES Bounce/{notification.Bounce?.BounceSubType ?? "Permanent"}",
                    detail: bounced.DiagnosticCode,

                    // Global, not connection-scoped. A permanently non-existent mailbox is
                    // non-existent whichever identity writes to it, and scoping it would keep
                    // mailing a dead address from every other connection.
                    connectionId: null,
                    createdBy: "SES",
                    ct: ct);
            }
        }
        else if (eventType == EmailEventType.Complaint)
        {
            foreach (var complained in notification.Complaint?.ComplainedRecipients ?? [])
            {
                if (string.IsNullOrWhiteSpace(complained.EmailAddress)) continue;

                await _suppression.SuppressAsync(
                    complained.EmailAddress,
                    SuppressionReason.Complaint,
                    source: $"SES Complaint/{notification.Complaint?.ComplaintFeedbackType ?? "unspecified"}",
                    detail: null,
                    connectionId: null,
                    createdBy: "SES",
                    ct: ct);
            }
        }

        _ = connectionId;
    }

    /// <summary>
    /// Recomputes the campaign's counters from its recipient rows.
    ///
    /// <para>
    /// Derived, not incremented. Events arrive concurrently from SNS, and incrementing a counter
    /// from several handlers at once is how a campaign header ends up disagreeing with its own
    /// recipient list.
    /// </para>
    /// </summary>
    private async Task RecalculateCampaignCountsAsync(int campaignId, CancellationToken ct)
    {
        var counts = await _dbContext.CampaignContacts
            .IgnoreQueryFilters()
            .Where(cc => cc.CampaignId == campaignId)
            .GroupBy(cc => cc.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var campaign = await _dbContext.Campaigns
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == campaignId, ct);

        if (campaign is null) return;

        int CountOf(MessageStatus status) => counts.FirstOrDefault(c => c.Status == status)?.Count ?? 0;

        campaign.TotalRecipients = counts.Sum(c => c.Count);
        campaign.DeliveredCount = CountOf(MessageStatus.Delivered) + CountOf(MessageStatus.Read);
        campaign.FailedCount = CountOf(MessageStatus.Failed)
                             + CountOf(MessageStatus.Bounced)
                             + CountOf(MessageStatus.Complained);

        // Finalised only once nothing is still pending, so a campaign mid-flight is not reported
        // as finished the moment its first event lands.
        var pending = CountOf(MessageStatus.Pending);
        if (pending == 0 && campaign.Status == CampaignStatus.Sending)
        {
            var succeeded = CountOf(MessageStatus.Sent)
                          + CountOf(MessageStatus.Delivered)
                          + CountOf(MessageStatus.Read);

            campaign.Status = campaign.FailedCount == 0
                ? CampaignStatus.Sent
                : succeeded == 0
                    ? CampaignStatus.Failed
                    : CampaignStatus.PartiallyFailed;
        }

        await _dbContext.SaveChangesAsync(ct);
    }

    private static bool IsPermanentBounce(SesEventNotification notification) =>
        notification.Bounce?.BounceType == "Permanent";

    private static string BuildBounceMessage(SesEventNotification notification)
    {
        var subType = notification.Bounce?.BounceSubType;
        var diagnostic = notification.Bounce?.BouncedRecipients?.FirstOrDefault()?.DiagnosticCode;

        var message = $"Permanently bounced{(subType is null ? "" : $" ({subType})")}.";
        return diagnostic is null ? message : $"{message} {diagnostic}";
    }

    /// <summary>
    /// The event's own timestamp where SES supplies one, falling back to the mail's and then to
    /// now. Using the event's time rather than receipt time keeps the ordering meaningful when
    /// SNS delivers late.
    /// </summary>
    private static DateTime ResolveTimestamp(SesEventNotification notification)
    {
        var timestamp = notification.ResolvedEventType switch
        {
            "Bounce" => notification.Bounce?.Timestamp,
            "Complaint" => notification.Complaint?.Timestamp,
            "Delivery" => notification.Delivery?.Timestamp,
            "Open" => notification.Open?.Timestamp,
            "Click" => notification.Click?.Timestamp,
            "DeliveryDelay" => notification.DeliveryDelay?.Timestamp,
            _ => null
        } ?? notification.Mail?.Timestamp ?? DateTime.UtcNow;

        return timestamp.Kind == DateTimeKind.Utc
            ? timestamp
            : DateTime.SpecifyKind(timestamp.ToUniversalTime(), DateTimeKind.Utc);
    }

    private static string? ResolveRecipientAddress(SesEventNotification notification, CampaignContact? recipient) =>
        notification.Bounce?.BouncedRecipients?.FirstOrDefault()?.EmailAddress
        ?? notification.Complaint?.ComplainedRecipients?.FirstOrDefault()?.EmailAddress
        ?? notification.Delivery?.Recipients?.FirstOrDefault()
        ?? notification.Mail?.Destination?.FirstOrDefault()
        ?? recipient?.Contact?.Email;

    private static EmailEventType? MapEventType(string? eventType) => eventType switch
    {
        "Send" => EmailEventType.Send,
        "Delivery" => EmailEventType.Delivery,
        "Bounce" => EmailEventType.Bounce,
        "Complaint" => EmailEventType.Complaint,
        "Reject" => EmailEventType.Reject,
        "Open" => EmailEventType.Open,
        "Click" => EmailEventType.Click,
        "DeliveryDelay" => EmailEventType.DeliveryDelay,
        // SES spells this one with a space.
        "Rendering Failure" or "RenderingFailure" => EmailEventType.RenderingFailure,
        "Subscription" => EmailEventType.Subscription,
        _ => null
    };

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) ? value
        : value.Length <= maxLength ? value
        : value[..maxLength];
}
