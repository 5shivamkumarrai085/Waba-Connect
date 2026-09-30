using System.Collections.Concurrent;

namespace WhatsAppCampaignApi.Services;

/// <summary>
/// Serialises bot processing per customer, so two messages from the same person are never handled
/// at the same time.
///
/// <para>
/// Every inbound WhatsApp message is routed on its own detached task. That is right — Meta must
/// get its acknowledgement immediately, and one slow bot must not hold up the next message. What
/// was missing was any ordering <em>within</em> a conversation, and a bot conversation is a state
/// machine: read the current node, decide, write the next node. Two of those interleaving on one
/// customer produced exactly the failures reported.
/// </para>
/// <para>
/// Observed on a three-message test: the flow greeted the customer twice for a single "hi", a
/// message that arrived mid-flow was answered by a menu that had not finished being sent, and a
/// "stop" was overtaken by a task from the previous message which re-created the conversation
/// state <em>after</em> the stop had been recorded — so the bot carried on as though nothing had
/// been said. Widening the gaps between messages made it look fixed, which is the signature of a
/// race rather than of broken logic.
/// </para>
/// <para>
/// The lock is per phone number, so different customers are still processed fully in parallel; only
/// one person's own messages queue behind each other, which is the order they were sent in anyway.
/// </para>
/// <para>
/// In-process, because the bot pipeline runs in one process. A multi-instance deployment would need
/// this moved to a shared lock (the database or a distributed cache) — the call site would not
/// change, only what <see cref="EnterAsync"/> waits on.
/// </para>
/// </summary>
public static class ConversationGate
{
    /// <summary>
    /// A fixed set of lock stripes, chosen by hashing the phone number. The previous per-number
    /// dictionary was never pruned, so it grew by one semaphore for every customer who ever wrote
    /// in. Striping keeps memory constant; two numbers that share a stripe merely take turns,
    /// which is harmless because a bot turn is short.
    /// </summary>
    private const int StripeCount = 4096;
    private static readonly SemaphoreSlim[] Stripes = Enumerable.Range(0, StripeCount).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    /// <summary>
    /// How long a message waits for the one ahead of it before giving up.
    ///
    /// A bot turn is normally under a second; anything approaching this means the previous turn is
    /// stuck on an outbound call. Waiting forever would pile up tasks behind it, so the wait is
    /// bounded and a timeout is reported to the caller rather than silently ignored.
    /// </summary>
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Takes the conversation's turn. Dispose the result to release it.
    ///
    /// <para>
    /// Returns null when the wait timed out — the caller should skip processing rather than run
    /// concurrently, because running anyway is the behaviour this class exists to prevent.
    /// </para>
    /// </summary>
    public static async Task<IDisposable?> EnterAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var gate = Stripes[(int)((uint)StringComparer.Ordinal.GetHashCode(phoneNumber) % StripeCount)];

        if (!await gate.WaitAsync(MaxWait, cancellationToken))
        {
            return null;
        }

        return new Release(gate);
    }

    /// <summary>How many lock stripes are currently held. For diagnostics.</summary>
    public static int HeldStripes => Stripes.Count(s => s.CurrentCount == 0);

    private sealed class Release : IDisposable
    {
        private readonly SemaphoreSlim _gate;
        private bool _released;

        public Release(SemaphoreSlim gate) => _gate = gate;

        public void Dispose()
        {
            if (_released) return;
            _released = true;
            _gate.Release();
        }
    }
}
