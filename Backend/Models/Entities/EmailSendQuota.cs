namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// Cross-instance token bucket for one connection's send rate.
///
/// <para>
/// The state lives in the database rather than in process memory on purpose. An in-process
/// limiter is correct only while exactly one instance is running: scale to three and the mail
/// account quietly receives three times the configured rate, which gets it throttled and then
/// reputation-damaged. Reserving tokens is a single conditional <c>UPDATE … RETURNING</c> that
/// refills by elapsed time and decrements in the same statement, so concurrent workers cannot
/// both see the same spare capacity.
/// </para>
/// <para>
/// The cost is one round trip per send batch. That is cheap next to the provider call it gates,
/// and it buys a limit that is actually true.
/// </para>
/// </summary>
public class EmailSendQuota
{
    /// <summary>Also the primary key — one bucket per connection.</summary>
    public int ConnectionId { get; set; }
    public virtual Connection? Connection { get; set; }

    /// <summary>Burst ceiling, in messages. Tokens never refill above this.</summary>
    public double Capacity { get; set; }

    /// <summary>Refill rate, in messages per second.</summary>
    public double RefillPerSecond { get; set; }

    /// <summary>Currently available tokens. Fractional, because refill is time-proportional.</summary>
    public double Tokens { get; set; }

    /// <summary>
    /// When <see cref="Tokens"/> was last recomputed. The refill is derived from the gap between
    /// this and <c>now()</c> inside the reservation statement, so no timer or background tick is
    /// needed to keep the bucket current.
    /// </summary>
    public DateTime LastRefillAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
