using System.Threading.Channels;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// In-process implementation of <see cref="IEventPublisher"/> using a bounded
/// <see cref="Channel{T}"/>.
///
/// <para>
/// Events are written to the channel and consumed asynchronously by
/// <see cref="EventPublisherConsumer"/> (a hosted background service), which forwards them
/// to the SignalR hub. This keeps the critical path (the send worker recording a result)
/// non-blocking: writing to a channel is a microsecond operation.
/// </para>
///
/// <para>
/// The channel is bounded (capacity 4096) so a burst of events does not consume unbounded
/// memory. When it is full, <see cref="BoundedChannelFullMode.DropOldest"/> discards the
/// oldest notification rather than back-pressuring the sender. For real-time campaign progress,
/// a dropped notification means the client simply does not receive that particular increment
/// \u2014 the counters will converge on the next full reload, which still happens on page load.
/// </para>
///
/// <para>
/// Future scaling: replace this class with a RabbitMQ or SQS publisher without any changes
/// to the callers.
/// </para>
/// </summary>
public sealed class InProcessEventPublisher : IEventPublisher, IDisposable
{
    private readonly Channel<CampaignEmailEventNotification> _channel;

    public InProcessEventPublisher()
    {
        _channel = Channel.CreateBounded<CampaignEmailEventNotification>(
            new BoundedChannelOptions(4096)
            {
                // Drop oldest rather than blocking the writer: a missed real-time notification
                // is acceptable; blocking a dispatch worker is not.
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,  // Only the consumer reads
                SingleWriter = false  // Multiple workers write concurrently
            });
    }

    /// <inheritdoc />
    public ValueTask PublishEmailEventAsync(CampaignEmailEventNotification notification) =>
        _channel.Writer.TryWrite(notification)
            ? ValueTask.CompletedTask
            // Channel is full and DropOldest failed (shouldn't happen with that mode, but be safe)
            : ValueTask.CompletedTask;

    /// <summary>
    /// The consumer side. Only <see cref="EventPublisherConsumer"/> calls this.
    /// </summary>
    public ChannelReader<CampaignEmailEventNotification> Reader => _channel.Reader;

    public void Dispose() => _channel.Writer.TryComplete();
}
