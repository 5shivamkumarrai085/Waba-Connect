using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// Persists normalized email events with idempotency guarantees.
///
/// <para>
/// This is the write side of the event store. All sources that produce email events — SMTP
/// results, IMAP replies, open-tracking pixels, click-tracking redirects, unsubscribe links —
/// go through this interface. It ensures each event is stored exactly once, even when the same
/// event arrives from multiple sources or when processing is retried.
/// </para>
/// </summary>
public interface IEmailEventStore
{
    /// <summary>
    /// Records an email event. Returns the persisted event, or null if the idempotency key
    /// was already present (duplicate — processing should be skipped).
    /// </summary>
    /// <param name="idempotencyKey">
    /// A stable, unique key for this specific occurrence. See <see cref="EmailEvent.IdempotencyKey"/>
    /// for the key convention.
    /// </param>
    Task<EmailEvent?> RecordAsync(
        EmailEventKind kind,
        string idempotencyKey,
        string source,
        int? campaignId,
        int? campaignContactId,
        string? messageId = null,
        string? providerMessageId = null,
        string? recipientAddress = null,
        DateTime? occurredAt = null,
        EmailBounceType? bounceType = null,
        string? bounceSubType = null,
        string? diagnosticCode = null,
        string? originalUrl = null,
        string? userAgent = null,
        string? ipAddress = null,
        string? metadataJson = null,
        CancellationToken ct = default);

    /// <summary>
    /// Returns true if an event with this idempotency key was already recorded.
    /// Can be used to short-circuit processing before expensive lookups.
    /// </summary>
    Task<bool> ExistsAsync(string idempotencyKey, CancellationToken ct = default);
}
