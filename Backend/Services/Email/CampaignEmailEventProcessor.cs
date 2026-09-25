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

        // 2. Update CampaignContact state
        CampaignContact? recipient = null;
        if (campaignContactId.HasValue)
        {
            recipient = await db.CampaignContacts
                .IgnoreQueryFilters()
                .Include(cc => cc.Contact)
                .FirstOrDefaultAsync(cc => cc.Id == campaignContactId.Value, ct);
        }

        string? newCampaignStatus = null;

        if (recipient is not null)
        {
            ApplyToRecipient(recipient, kind, occurredAt ?? DateTime.UtcNow,
                bounceType, bounceSubType, diagnosticCode);
        }

        // 3. Atomically increment Campaign counters
        if (campaignId.HasValue)
        {
            await IncrementCounterAsync(db, campaignId.Value, kind, ct);
        }

        // 4. Save recipient state changes
        if (recipient is not null)
        {
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save recipient state for CampaignContact {Id}.", recipient.Id);
            }
        }

        // 5. Handle suppression for bounces/complaints
        if (kind is EmailEventKind.Bounced && !string.IsNullOrWhiteSpace(recipientAddress))
        {
            var connectionId = campaignId.HasValue
                ? await GetConnectionIdAsync(db, campaignId.Value, ct)
                : null;
            await _suppression.SuppressAsync(
                recipientAddress, SuppressionReason.Bounce,
                source: $"Bounce/{bounceSubType ?? "Permanent"}",
                detail: diagnosticCode,
                connectionId: null,  // Global — a non-existent mailbox stays non-existent
                createdBy: "EmailEventProcessor",
                ct: ct);
        }
        else if (kind is EmailEventKind.Complained && !string.IsNullOrWhiteSpace(recipientAddress))
        {
            await _suppression.SuppressAsync(
                recipientAddress, SuppressionReason.Complaint,
                source: "Complaint",
                detail: null, connectionId: null,
                createdBy: "EmailEventProcessor",
                ct: ct);
        }
        else if (kind is EmailEventKind.Unsubscribed && !string.IsNullOrWhiteSpace(recipientAddress))
        {
            await _suppression.SuppressAsync(
                recipientAddress, SuppressionReason.Unsubscribe,
                source: "UnsubscribeLink",
                detail: null, connectionId: null,
                createdBy: "EmailEventProcessor",
                ct: ct);
        }

        // 6. Check campaign finalization (terminal delivery events only)
        if (campaignId.HasValue && IsTerminalDeliveryEvent(kind))
        {
            newCampaignStatus = await TryFinalizeCampaignAsync(db, campaignId.Value, ct);
        }

        // 7. Publish real-time notification (non-blocking, best-effort)
        if (campaignId.HasValue)
        {
            var (delta, _) = GetCounterDelta(kind);
            await _publisher.PublishEmailEventAsync(new CampaignEmailEventNotification(
                CampaignId: campaignId.Value,
                Kind: kind,
                CampaignContactId: campaignContactId,
                RecipientAddress: recipientAddress,
                OccurredAt: occurredAt ?? DateTime.UtcNow,
                SentDelta:          kind == EmailEventKind.Sent         ? 1 : 0,
                FailedDelta:        kind == EmailEventKind.Failed        ? 1 : 0,
                DeliveredDelta:     kind == EmailEventKind.Delivered     ? 1 : 0,
                BouncedDelta:       kind == EmailEventKind.Bounced       ? 1 : 0,
                OpenedDelta:        kind == EmailEventKind.Opened        ? 1 : 0,
                ClickedDelta:       kind == EmailEventKind.Clicked       ? 1 : 0,
                RepliedDelta:       kind == EmailEventKind.Replied       ? 1 : 0,
                UnsubscribedDelta:  kind == EmailEventKind.Unsubscribed  ? 1 : 0,
                ComplainedDelta:    kind == EmailEventKind.Complained    ? 1 : 0,
                NewCampaignStatus: newCampaignStatus));
        }
    }

    /// <summary>
    /// Moves recipient state forward. Never backwards — events arrive out of order.
    /// </summary>
    private static void ApplyToRecipient(
        CampaignContact recipient,
        EmailEventKind kind,
        DateTime occurredAt,
        EmailBounceType? bounceType,
        string? bounceSubType,
        string? diagnosticCode)
    {
        switch (kind)
        {
            case EmailEventKind.Sent:
                if (recipient.Status == MessageStatus.Pending)
                {
                    recipient.Status = MessageStatus.Sent;
                    recipient.SentAt ??= occurredAt;
                }
                break;

            case EmailEventKind.Delivered:
                if (recipient.Status is MessageStatus.Pending or MessageStatus.Sent)
                    recipient.Status = MessageStatus.Delivered;
                recipient.SentAt    ??= occurredAt;
                recipient.DeliveredAt ??= occurredAt;
                break;

            case EmailEventKind.Bounced:
                // Always terminal, regardless of current status
                recipient.Status = MessageStatus.Bounced;
                recipient.ErrorMessage = BuildBounceMessage(bounceSubType, diagnosticCode);
                break;

            case EmailEventKind.Complained:
                recipient.Status = MessageStatus.Complained;
                recipient.ErrorMessage = "Recipient marked this message as spam.";
                break;

            case EmailEventKind.Failed:
                if (recipient.Status is MessageStatus.Pending or MessageStatus.Sent)
                {
                    recipient.Status = MessageStatus.Failed;
                }
                break;

            // Engagement events — set timestamps, but do NOT change delivery status.
            // An Open must not overwrite Bounced; a Reply must not overwrite Delivered.
            case EmailEventKind.Opened:
                recipient.OpenedAt ??= occurredAt;
                break;

            case EmailEventKind.Clicked:
                recipient.ClickedAt ??= occurredAt;
                break;

            case EmailEventKind.Replied:
                recipient.RepliedAt ??= occurredAt;
                break;

            case EmailEventKind.Unsubscribed:
                // Suppression is handled separately. Status unchanged — still Sent/Delivered.
                break;
        }
    }

    /// <summary>
    /// Atomically increments the relevant Campaign counter by 1.
    /// One SQL UPDATE per event — O(1) regardless of recipient count.
    /// </summary>
    private static async Task IncrementCounterAsync(
        AppDbContext db, int campaignId, EmailEventKind kind, CancellationToken ct)
    {
        var (column, _) = GetCounterDelta(kind);
        if (column is null) return;

        // Raw SQL for atomic increment. EF's change tracker cannot express
        // "increment this specific column" without a read-modify-write that races.
        var sql = $"""
            UPDATE "Campaigns"
               SET "{column}" = "{column}" + 1,
                   "UpdatedAt" = now()
             WHERE "Id" = {campaignId}
               AND "IsDeleted" = false
            """;

        await db.Database.ExecuteSqlRawAsync(sql, ct);
    }

    private static (string? Column, int Delta) GetCounterDelta(EmailEventKind kind) => kind switch
    {
        EmailEventKind.Sent         => ("SentCount",           1),
        EmailEventKind.Failed       => ("FailedCount",         1),
        EmailEventKind.Delivered    => ("DeliveredCount",      1),
        EmailEventKind.Bounced      => ("FailedCount",         1),  // Bounced counts as failed
        EmailEventKind.Opened       => ("OpenedCount",         1),
        EmailEventKind.Clicked      => ("ClickedCount",        1),
        EmailEventKind.Replied      => ("RepliedCount",        1),
        EmailEventKind.Unsubscribed => ("UnsubscribedCount",   1),
        EmailEventKind.Complained   => ("ComplainedCount",     1),
        _                           => (null,                  0)
    };

    private static bool IsTerminalDeliveryEvent(EmailEventKind kind) => kind is
        EmailEventKind.Sent or EmailEventKind.Failed or
        EmailEventKind.Delivered or EmailEventKind.Bounced or EmailEventKind.Complained;

    /// <summary>
    /// Checks whether the campaign is complete and updates its status if so.
    /// One conditional UPDATE — does nothing if recipients are still pending.
    /// Returns the new status string, or null if nothing changed.
    /// </summary>
    private static async Task<string?> TryFinalizeCampaignAsync(
        AppDbContext db, int campaignId, CancellationToken ct)
    {
        // This query is intentionally minimal: it only runs after terminal delivery events,
        // not after every send. The WHERE clause filters to campaigns that are still Sending,
        // so it is a no-op once the campaign has already been finalized.
        const string sql = """
            UPDATE "Campaigns" c SET
                "Status" = CASE
                    WHEN s.sent = 0       THEN 'Failed'
                    WHEN s.failed = 0     THEN 'Sent'
                    ELSE                       'PartiallyFailed'
                END,
                "UpdatedAt" = now()
            FROM (
                SELECT
                    count(*) FILTER (WHERE "Status" = 'Pending')                          AS pending,
                    count(*) FILTER (WHERE "Status" IN ('Sent','Delivered','Read'))        AS sent,
                    count(*) FILTER (WHERE "Status" IN ('Failed','Bounced','Complained'))  AS failed
                FROM "CampaignContacts"
                WHERE "CampaignId" = {0}
            ) s
            WHERE c."Id" = {0}
              AND c."Status" = 'Sending'
              AND s.pending = 0
            RETURNING c."Status"
            """;

        // ExecuteSqlRaw doesn't return rows; we check affected count and re-read if needed
        var affected = await db.Database.ExecuteSqlRawAsync(
            sql.Replace("{0}", campaignId.ToString()), ct);

        if (affected > 0)
        {
            var campaign = await db.Campaigns.AsNoTracking()
                .IgnoreQueryFilters()
                .Where(c => c.Id == campaignId)
                .Select(c => c.Status)
                .FirstOrDefaultAsync(ct);

            var statusStr = campaign.ToString();
            return statusStr;
        }

        return null;
    }

    private static string BuildBounceMessage(string? subType, string? diagnostic)
    {
        var msg = $"Permanently bounced{(subType is null ? "" : $" ({subType})")}.";
        return diagnostic is null ? msg : $"{msg} {diagnostic}";
    }

    private static async Task<int?> GetConnectionIdAsync(AppDbContext db, int campaignId, CancellationToken ct)
    {
        return await db.Campaigns.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(c => c.Id == campaignId)
            .Select(c => c.ConnectionId)
            .FirstOrDefaultAsync(ct);
    }
}
