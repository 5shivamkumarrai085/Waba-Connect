using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// Processes normalized email events: updates CampaignContact state, atomically increments
/// Campaign counters, triggers campaign finalization, and publishes real-time notifications.
///
/// <para>
/// This is the single entry point for all email events regardless of source (SMTP result, IMAP
/// reply, open pixel, click redirect, unsubscribe). Every source calls this interface instead
/// of writing to the DB directly, which is what keeps the business rules consistent.
/// </para>
///
/// <para>
/// The counter strategy is atomic SQL increments (UPDATE SET count = count + 1) rather than a
/// full GROUP BY recalculation. This makes each event O(1) at the DB instead of O(N) where N is
/// the number of campaign recipients. It is idempotency-safe because the
/// <see cref="IEmailEventStore"/> ensures each event is only recorded once.
/// </para>
/// </summary>
public interface ICampaignEmailEventProcessor
{
    /// <summary>
    /// Processes one normalized email event end-to-end:
    /// 1. Records the event (idempotency check)
    /// 2. Updates CampaignContact status/timestamps
    /// 3. Atomically increments Campaign counters
    /// 4. Checks for campaign completion
    /// 5. Publishes real-time notification
    /// </summary>
    Task ProcessAsync(
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
        CancellationToken ct = default);
}

/// <inheritdoc />
public class CampaignEmailEventProcessor : ICampaignEmailEventProcessor
{
    private readonly IEmailEventStore _eventStore;
    private readonly IEmailSuppressionService _suppression;
    private readonly IEventPublisher _publisher;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<CampaignEmailEventProcessor> _logger;

    public CampaignEmailEventProcessor(
        IEmailEventStore eventStore,
        IEmailSuppressionService suppression,
        IEventPublisher publisher,
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger<CampaignEmailEventProcessor> logger)
    {
        _eventStore   = eventStore;
        _suppression  = suppression;
        _publisher    = publisher;
        _contextFactory = contextFactory;
        _logger       = logger;
    }

    /// <inheritdoc />
    public async Task ProcessAsync(
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
        CancellationToken ct = default)
    {
        // 1. Record the event (idempotency enforced here)
        var recorded = await _eventStore.RecordAsync(
            kind, idempotencyKey, source,
            campaignId, campaignContactId,
            messageId, providerMessageId, recipientAddress,
            occurredAt, bounceType, bounceSubType, diagnosticCode,
            originalUrl, userAgent, ipAddress,
            ct: ct);

        if (recorded is null)
        {
            // Duplicate — already processed. Do nothing.
            _logger.LogDebug("Skipping duplicate event {Kind} / {Key}.", kind, idempotencyKey);
            return;
        }

        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var when = occurredAt ?? DateTime.UtcNow;

        // 2. Move the recipient forward. Each transition is one conditional UPDATE, so two workers
        //    handling events for the same recipient cannot both believe they made the change —
        //    which is what makes the counters below count recipients, not events. Opening an
        //    email ten times is one open, as every mailing platform reports it.
        var counts = true;
        if (campaignContactId.HasValue)
        {
            counts = await ApplyToRecipientAsync(db, campaignContactId.Value, recorded.Id, kind, when, bounceSubType, diagnosticCode, ct);
        }

        // 3. Atomically increment the campaign counter — only for a real state change.
        if (campaignId.HasValue && counts)
        {
            await IncrementCounterAsync(db, campaignId.Value, kind, ct);
        }

        // 4. Suppression for bounces, complaints and unsubscribes. Always global: a mailbox that
        //    does not exist, or a person who asked to stop, must not be mailed from any connection.
        var suppressionReason = kind switch
        {
            EmailEventKind.Bounced when bounceType != EmailBounceType.Transient => SuppressionReason.Bounce,
            EmailEventKind.Complained => SuppressionReason.Complaint,
            EmailEventKind.Unsubscribed => SuppressionReason.Unsubscribe,
            _ => (SuppressionReason?)null
        };

        if (suppressionReason is { } reason && !string.IsNullOrWhiteSpace(recipientAddress))
        {
            await _suppression.SuppressAsync(
                recipientAddress, reason,
                source: reason switch
                {
                    SuppressionReason.Bounce => $"Bounce/{bounceSubType ?? "Permanent"}",
                    SuppressionReason.Complaint => "Complaint",
                    _ => "UnsubscribeLink"
                },
                detail: reason == SuppressionReason.Bounce ? diagnosticCode : null,
                connectionId: null,
                createdBy: "EmailEventProcessor",
                ct: ct);
        }

        // 5. Campaign completion (terminal delivery events only).
        string? newCampaignStatus = null;
        if (campaignId.HasValue && IsTerminalDeliveryEvent(kind))
        {
            newCampaignStatus = await CampaignFinalizer.TryFinalizeAsync(db, campaignId.Value, ct);
        }

        // 6. Real-time notification (non-blocking, best-effort). Sent even when the counter did
        //    not move, so a status change still reaches the page; the deltas are what tell the
        //    client whether to increment.
        if (campaignId.HasValue && (counts || newCampaignStatus is not null))
        {
            var delta = counts ? 1 : 0;
            await _publisher.PublishEmailEventAsync(new CampaignEmailEventNotification(
                CampaignId: campaignId.Value,
                Kind: kind,
                CampaignContactId: campaignContactId,
                RecipientAddress: null,
                OccurredAt: when,
                SentDelta:          kind == EmailEventKind.Sent         ? delta : 0,
                FailedDelta:        kind == EmailEventKind.Failed       ? delta : 0,
                DeliveredDelta:     kind == EmailEventKind.Delivered    ? delta : 0,
                BouncedDelta:       kind == EmailEventKind.Bounced      ? delta : 0,
                OpenedDelta:        kind == EmailEventKind.Opened       ? delta : 0,
                ClickedDelta:       kind == EmailEventKind.Clicked      ? delta : 0,
                RepliedDelta:       kind == EmailEventKind.Replied      ? delta : 0,
                UnsubscribedDelta:  kind == EmailEventKind.Unsubscribed ? delta : 0,
                ComplainedDelta:    kind == EmailEventKind.Complained   ? delta : 0,
                NewCampaignStatus: newCampaignStatus));
        }
    }

    /// <summary>
    /// Moves recipient state forward, never backwards — events arrive out of order, and an Open
    /// must not overwrite Bounced. Returns true when this call changed the recipient, i.e. when
    /// the event is the first of its kind for them and should move the campaign counter.
    /// </summary>
    private static async Task<bool> ApplyToRecipientAsync(
        AppDbContext db,
        int recipientId,
        long eventId,
        EmailEventKind kind,
        DateTime occurredAt,
        string? bounceSubType,
        string? diagnosticCode,
        CancellationToken ct)
    {
        var recipients = db.CampaignContacts.IgnoreQueryFilters().Where(cc => cc.Id == recipientId);

        switch (kind)
        {
            // The send recorder has already written the Sent/Failed row in the same unit of work
            // as the chat message, and the idempotency key is per recipient, so reaching here
            // means this is the one and only Sent (or Failed) for them.
            case EmailEventKind.Sent:
                await recipients
                    .Where(cc => cc.Status == MessageStatus.Pending)
                    .ExecuteUpdateAsync(u => u
                        .SetProperty(cc => cc.Status, MessageStatus.Sent)
                        .SetProperty(cc => cc.SentAt, cc => cc.SentAt ?? occurredAt), ct);
                return true;

            case EmailEventKind.Failed:
                await recipients
                    .Where(cc => cc.Status == MessageStatus.Pending || cc.Status == MessageStatus.Sent)
                    .ExecuteUpdateAsync(u => u.SetProperty(cc => cc.Status, MessageStatus.Failed), ct);
                return true;

            case EmailEventKind.Delivered:
                return await recipients
                    .Where(cc => cc.DeliveredAt == null)
                    .ExecuteUpdateAsync(u => u
                        .SetProperty(cc => cc.Status, cc => cc.Status == MessageStatus.Pending || cc.Status == MessageStatus.Sent
                            ? MessageStatus.Delivered : cc.Status)
                        .SetProperty(cc => cc.SentAt, cc => cc.SentAt ?? occurredAt)
                        .SetProperty(cc => cc.DeliveredAt, occurredAt), ct) > 0;

            case EmailEventKind.Bounced:
            {
                var message = BuildBounceMessage(bounceSubType, diagnosticCode);
                return await recipients
                    .Where(cc => cc.Status != MessageStatus.Bounced)
                    .ExecuteUpdateAsync(u => u
                        .SetProperty(cc => cc.Status, MessageStatus.Bounced)
                        .SetProperty(cc => cc.ErrorMessage, message), ct) > 0;
            }

            case EmailEventKind.Complained:
                return await recipients
                    .Where(cc => cc.Status != MessageStatus.Complained)
                    .ExecuteUpdateAsync(u => u
                        .SetProperty(cc => cc.Status, MessageStatus.Complained)
                        .SetProperty(cc => cc.ErrorMessage, "Recipient marked this message as spam."), ct) > 0;

            // Engagement — first occurrence only; delivery status untouched.
            case EmailEventKind.Opened:
                return await recipients
                    .Where(cc => cc.OpenedAt == null)
                    .ExecuteUpdateAsync(u => u.SetProperty(cc => cc.OpenedAt, occurredAt), ct) > 0;

            case EmailEventKind.Clicked:
                return await recipients
                    .Where(cc => cc.ClickedAt == null)
                    .ExecuteUpdateAsync(u => u.SetProperty(cc => cc.ClickedAt, occurredAt), ct) > 0;

            case EmailEventKind.Replied:
                return await recipients
                    .Where(cc => cc.RepliedAt == null)
                    .ExecuteUpdateAsync(u => u.SetProperty(cc => cc.RepliedAt, occurredAt), ct) > 0;

            case EmailEventKind.Unsubscribed:
                // No per-recipient column, so the recipient's earliest Unsubscribed event is the
                // one that counts. Callers use a per-recipient key today, but a second source (a
                // List-Unsubscribe POST as well as the link) must not count the same person twice
                // — the reconciler counts distinct recipients and would pull the figure back down.
                return !await db.EmailEvents.AsNoTracking().AnyAsync(e =>
                    e.CampaignContactId == recipientId
                    && e.EventKind == EmailEventKind.Unsubscribed
                    && e.Id < eventId, ct);

            default:
                return false;
        }
    }

    /// <summary>
    /// Atomically increments the relevant Campaign counter by 1.
    /// One SQL UPDATE per event — O(1) regardless of recipient count.
    /// </summary>
    private static async Task IncrementCounterAsync(
        AppDbContext db, int campaignId, EmailEventKind kind, CancellationToken ct)
    {
        var campaigns = db.Campaigns.IgnoreQueryFilters().Where(c => c.Id == campaignId && !c.IsDeleted);
        var now = DateTime.UtcNow;

        _ = kind switch
        {
            EmailEventKind.Sent => await campaigns.ExecuteUpdateAsync(u => u.SetProperty(c => c.SentCount, c => c.SentCount + 1).SetProperty(c => c.UpdatedAt, now), ct),
            EmailEventKind.Failed => await campaigns.ExecuteUpdateAsync(u => u.SetProperty(c => c.FailedCount, c => c.FailedCount + 1).SetProperty(c => c.UpdatedAt, now), ct),
            EmailEventKind.Delivered => await campaigns.ExecuteUpdateAsync(u => u.SetProperty(c => c.DeliveredCount, c => c.DeliveredCount + 1).SetProperty(c => c.UpdatedAt, now), ct),
            // A bounce is its own figure. It used to add to FailedCount, which made
            // sent + failed exceed the recipient total.
            EmailEventKind.Bounced => await campaigns.ExecuteUpdateAsync(u => u.SetProperty(c => c.BouncedCount, c => c.BouncedCount + 1).SetProperty(c => c.UpdatedAt, now), ct),
            EmailEventKind.Opened => await campaigns.ExecuteUpdateAsync(u => u.SetProperty(c => c.OpenedCount, c => c.OpenedCount + 1).SetProperty(c => c.UpdatedAt, now), ct),
            EmailEventKind.Clicked => await campaigns.ExecuteUpdateAsync(u => u.SetProperty(c => c.ClickedCount, c => c.ClickedCount + 1).SetProperty(c => c.UpdatedAt, now), ct),
            EmailEventKind.Replied => await campaigns.ExecuteUpdateAsync(u => u.SetProperty(c => c.RepliedCount, c => c.RepliedCount + 1).SetProperty(c => c.UpdatedAt, now), ct),
            EmailEventKind.Unsubscribed => await campaigns.ExecuteUpdateAsync(u => u.SetProperty(c => c.UnsubscribedCount, c => c.UnsubscribedCount + 1).SetProperty(c => c.UpdatedAt, now), ct),
            EmailEventKind.Complained => await campaigns.ExecuteUpdateAsync(u => u.SetProperty(c => c.ComplainedCount, c => c.ComplainedCount + 1).SetProperty(c => c.UpdatedAt, now), ct),
            _ => 0
        };
    }

    private static bool IsTerminalDeliveryEvent(EmailEventKind kind) => kind is
        EmailEventKind.Sent or EmailEventKind.Failed or
        EmailEventKind.Delivered or EmailEventKind.Bounced or EmailEventKind.Complained;

    private static string BuildBounceMessage(string? subType, string? diagnostic)
    {
        var msg = $"Permanently bounced{(subType is null ? "" : $" ({subType})")}.";
        var full = diagnostic is null ? msg : $"{msg} {diagnostic}";
        return full.Length > 500 ? full[..500] : full;
    }
}
