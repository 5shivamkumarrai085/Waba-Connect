using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// Publishes application-level email events to interested subscribers.
///
/// <para>
/// This is the decoupling layer between business logic and notification infrastructure.
/// SignalR is one subscriber; future ones (webhooks, audit log tailing, etc.) would be others.
/// Business logic calls Publish \u2014 it does not know or care whether SignalR, a message bus,
/// or nothing is listening.
/// </para>
///
/// <para>
/// The default implementation (<see cref="InProcessEventPublisher"/>) uses a
/// <see cref="System.Threading.Channels.Channel{T}"/> backed by a hosted consumer that
/// forwards to SignalR. Replacing the implementation with one that writes to RabbitMQ or
/// SQS does not require changing any business-logic code.
/// </para>
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Publishes an email event notification. Fire-and-forget: the publisher does not wait for
    /// subscribers to finish processing. Failures in subscribers are logged but do not bubble.
    /// </summary>
    ValueTask PublishEmailEventAsync(CampaignEmailEventNotification notification);
}

/// <summary>
/// The data carried by a campaign email event notification.
/// All properties are nullable so partial updates can be sent without requiring a full reload.
/// </summary>
public sealed record CampaignEmailEventNotification(
    int CampaignId,
    EmailEventKind Kind,
    int? CampaignContactId,
    string? RecipientAddress,
    DateTime OccurredAt,

    // Counter deltas to apply on the client (+1 increments the client counter without a reload)
    int SentDelta       = 0,
    int FailedDelta     = 0,
    int DeliveredDelta  = 0,
    int BouncedDelta    = 0,
    int OpenedDelta     = 0,
    int ClickedDelta    = 0,
    int RepliedDelta    = 0,
    int UnsubscribedDelta = 0,
    int ComplainedDelta = 0,

    /// <summary>WhatsApp "read" receipts — the WhatsApp counterpart of an email open.</summary>
    int ReadDelta = 0,

    /// <summary>New campaign status, if it changed as a result of this event.</summary>
    string? NewCampaignStatus = null
);
