using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// Campaign completion and counter reconciliation, shared by the event processor, the send
/// recorder and the maintenance sweep so the rules exist in exactly one place.
/// </summary>
public static class CampaignFinalizer
{
    /// <summary>
    /// Moves a Sending campaign to its terminal status once no recipient is still pending.
    /// Returns the new status, or null when the campaign is unfinished or already final.
    /// </summary>
    /// <remarks>
    /// The pending check is an index probe on (CampaignId, Status) that stops at the first hit,
    /// so calling this after every send is O(1) while recipients remain. The full per-status
    /// count runs only once nothing is pending — at most a handful of times per campaign — where
    /// the previous version counted every recipient after every single send (O(n²) overall).
    /// </remarks>
    public static async Task<string?> TryFinalizeAsync(AppDbContext db, int campaignId, CancellationToken ct)
    {
        var anyPending = await db.CampaignContacts
            .IgnoreQueryFilters()
            .AnyAsync(cc => cc.CampaignId == campaignId && cc.Status == MessageStatus.Pending, ct);

        if (anyPending) return null;

        // Bounced and complained recipients were accepted by the provider, so they count as sent
        // here; suppressed ones are neither. A campaign that sent nothing at all is Failed.
        var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Campaigns" c SET
                "Status" = CASE
                    WHEN s.sent = 0                  THEN 'Failed'
                    WHEN s.failed = 0                THEN 'Sent'
                    ELSE                                  'PartiallyFailed'
                END,
                "UpdatedAt" = now()
            FROM (
                SELECT
                    count(*) FILTER (WHERE "Status" = 'Pending')                                      AS pending,
                    count(*) FILTER (WHERE "Status" IN ('Sent','Delivered','Read','Bounced','Complained')) AS sent,
                    count(*) FILTER (WHERE "Status" = 'Failed')                                       AS failed
                FROM "CampaignContacts"
                WHERE "CampaignId" = {campaignId}
            ) s
            WHERE c."Id" = {campaignId}
              AND c."Status" = 'Sending'
              AND s.pending = 0
            """, ct);

        if (affected == 0) return null;

        var status = await db.Campaigns
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.Id == campaignId)
            .Select(c => c.Status)
            .FirstOrDefaultAsync(ct);

        return status.ToString();
    }

    /// <summary>
    /// Recomputes a WhatsApp campaign's counters from its recipient rows, using the same rules as
    /// the per-status deltas (a read message was also delivered and sent). Used after a retry,
    /// which puts failed recipients back to Pending.
    /// </summary>
    public static Task<int> ReconcileWhatsAppCountersAsync(AppDbContext db, int campaignId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Campaigns" c SET
                "SentCount"      = s.sent,
                "DeliveredCount" = s.delivered,
                "ReadCount"      = s.read,
                "FailedCount"    = s.failed,
                "SkippedCount"   = s.skipped
            FROM (
                SELECT
                    count(*) FILTER (WHERE "Status" IN ('Sent','Delivered','Read')) AS sent,
                    count(*) FILTER (WHERE "Status" IN ('Delivered','Read'))        AS delivered,
                    count(*) FILTER (WHERE "Status" = 'Read')                        AS read,
                    count(*) FILTER (WHERE "Status" = 'Failed')                      AS failed,
                    count(*) FILTER (WHERE "Status" = 'Skipped')                     AS skipped
                FROM "CampaignContacts"
                WHERE "CampaignId" = {campaignId}
            ) s
            WHERE c."Id" = {campaignId}
              AND c."Channel" = 'WhatsApp'
              AND (c."SentCount", c."DeliveredCount", c."ReadCount", c."FailedCount", c."SkippedCount")
                  IS DISTINCT FROM (s.sent, s.delivered, s.read, s.failed, s.skipped)
            """, ct);

    /// <summary>
    /// Recomputes an email campaign's counters from its recipient rows, which are the source of
    /// truth. The atomic per-event increments keep the figures live; this heals any drift they
    /// cannot see — a recipient failed by a path that raised no event, a worker killed between
    /// two writes, or counts carried over from before the counters existed.
    /// </summary>
    public static Task<int> ReconcileEmailCountersAsync(AppDbContext db, int campaignId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Campaigns" c SET
                "SentCount"         = s.sent,
                "FailedCount"       = s.failed,
                "BouncedCount"      = s.bounced,
                "SuppressedCount"   = s.suppressed,
                "SkippedCount"      = s.skipped,
                "ComplainedCount"   = s.complained,
                "DeliveredCount"    = s.delivered,
                "OpenedCount"       = s.opened,
                "ClickedCount"      = s.clicked,
                "RepliedCount"      = s.replied,
                "UnsubscribedCount" = u.unsubscribed
            FROM (
                SELECT
                    count(*) FILTER (WHERE "SentAt" IS NOT NULL
                                       OR "Status" IN ('Sent','Delivered','Read','Bounced','Complained')) AS sent,
                    count(*) FILTER (WHERE "Status" = 'Failed')      AS failed,
                    count(*) FILTER (WHERE "Status" = 'Bounced')     AS bounced,
                    count(*) FILTER (WHERE "Status" = 'Suppressed')  AS suppressed,
                    count(*) FILTER (WHERE "Status" = 'Skipped')     AS skipped,
                    count(*) FILTER (WHERE "Status" = 'Complained')  AS complained,
                    count(*) FILTER (WHERE "DeliveredAt" IS NOT NULL) AS delivered,
                    count(*) FILTER (WHERE "OpenedAt" IS NOT NULL)   AS opened,
                    count(*) FILTER (WHERE "ClickedAt" IS NOT NULL)  AS clicked,
                    count(*) FILTER (WHERE "RepliedAt" IS NOT NULL)  AS replied
                FROM "CampaignContacts"
                WHERE "CampaignId" = {campaignId}
            ) s,
            (
                SELECT count(DISTINCT "CampaignContactId") AS unsubscribed
                FROM "EmailEvents"
                WHERE "CampaignId" = {campaignId} AND "EventKind" = 'Unsubscribed'
            ) u
            WHERE c."Id" = {campaignId}
              AND c."Channel" = 'Email'
              AND (c."SentCount", c."FailedCount", c."BouncedCount", c."SuppressedCount", c."SkippedCount", c."ComplainedCount",
                   c."DeliveredCount", c."OpenedCount", c."ClickedCount", c."RepliedCount", c."UnsubscribedCount")
                  IS DISTINCT FROM
                  (s.sent, s.failed, s.bounced, s.suppressed, s.skipped, s.complained,
                   s.delivered, s.opened, s.clicked, s.replied, u.unsubscribed)
            """, ct);
}
