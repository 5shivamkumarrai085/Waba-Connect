namespace WhatsAppCampaignApi.Services.Queue;

/// <summary>
/// An in-process "work just arrived" nudge. The webhook controller pulses it after enqueueing,
/// so the inbound worker on the same instance starts at once instead of finishing its poll sleep
/// (up to a second of every reply). Other instances still find the job by polling; this only
/// shortens the common case.
/// </summary>
public sealed class QueueSignal
{
    private readonly SemaphoreSlim _pending = new(0, int.MaxValue);

    /// <summary>Wakes one waiting worker loop (or the next one to wait).</summary>
    public void Pulse()
    {
        // Bounded so a burst of webhooks cannot pile up thousands of wake-ups for idle loops.
        if (_pending.CurrentCount < 64) _pending.Release();
    }

    /// <summary>Waits for a pulse or the timeout, whichever comes first.</summary>
    public async Task WaitAsync(TimeSpan timeout, CancellationToken ct)
    {
        await _pending.WaitAsync(timeout, ct);
    }
}
