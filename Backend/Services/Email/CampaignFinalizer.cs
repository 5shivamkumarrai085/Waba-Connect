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

        // The status above flips at most once per campaign (it is conditional on Sending), so this
        // records each completion exactly once. Written set-based rather than through
        // IAuditService because the finalizer runs from workers with no request or user — the
        // actor is the system itself.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AuditLogs" ("Event", "Category", "Module", "Action", "Status", "Description",
                                     "EntityType", "EntityId", "EntityName", "UserName", "CreatedAt")
            SELECT 'Campaign.' || a.action, 'Data', 'Campaign', a.action,
                   CASE WHEN c."Status" = 'Sent' THEN 'Success' ELSE 'Failed' END,
                   'Campaign "' || c."Name" || '" finished: ' || c."SentCount" || ' sent, ' || c."FailedCount" || ' failed, '
                       || c."BouncedCount" || ' bounced, ' || c."SkippedCount" || ' skipped.',
                   'Campaign', c."Id"::text, left(c."Name", 200), {Catalogs.RecentActivityCatalog.SystemActor}, now()
            FROM "Campaigns" c
            -- "Failed" alone is what the error middleware writes for a server error ("Campaign.Failed"),
            -- so a campaign that sent nothing is "SendFailed" to keep the two apart in filters.
            CROSS JOIN LATERAL (SELECT CASE c."Status" WHEN 'Sent' THEN 'Completed' WHEN 'Failed' THEN 'SendFailed' ELSE c."Status" END AS action) a
            WHERE c."Id" = {campaignId}
            """, ct);

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
    /// Recomputes an email campaign's counters from its recipient rows. The atomic per-event
    /// increments keep the figures live; this heals any drift they cannot see — a recipient failed
    /// by a path that raised no event, a worker killed between two writes, or counts carried over
    /// from before the counters existed.
    ///
    /// The recipient rows are first brought up to date from the event log. An event is recorded
    /// before the recipient is stamped, so a process that dies in between leaves an Opened (or
    /// Clicked, Replied, Delivered, Bounced) event with nothing to show for it — and Gmail caches
    /// the pixel, so the open never arrives again. Every stamp is "only if not set yet", so the
    /// repair never moves a recipient backwards or counts anyone twice.
    /// </summary>
    public static async Task<int> ReconcileEmailCountersAsync(AppDbContext db, int campaignId, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "CampaignContacts" cc SET
                "OpenedAt"    = COALESCE(cc."OpenedAt", e.opened),
                "ClickedAt"   = COALESCE(cc."ClickedAt", e.clicked),
                "RepliedAt"   = COALESCE(cc."RepliedAt", e.replied),
                "DeliveredAt" = COALESCE(cc."DeliveredAt", e.delivered),
                "Status"      = CASE WHEN e.bounced AND cc."Status" NOT IN ('Bounced','Complained') THEN 'Bounced' ELSE cc."Status" END
            FROM (
                SELECT "CampaignContactId" AS id,
                       min("OccurredAt") FILTER (WHERE "EventKind" = 'Opened')    AS opened,
                       min("OccurredAt") FILTER (WHERE "EventKind" = 'Clicked')   AS clicked,
                       min("OccurredAt") FILTER (WHERE "EventKind" = 'Replied')   AS replied,
                       min("OccurredAt") FILTER (WHERE "EventKind" = 'Delivered') AS delivered,
                       bool_or("EventKind" = 'Bounced' AND "BounceType" IS DISTINCT FROM 'Transient') AS bounced
                FROM "EmailEvents"
                WHERE "CampaignId" = {campaignId} AND "CampaignContactId" IS NOT NULL
                  AND "EventKind" IN ('Opened','Clicked','Replied','Delivered','Bounced')
                GROUP BY "CampaignContactId"
            ) e
            WHERE cc."Id" = e.id AND cc."CampaignId" = {campaignId}
              AND ((cc."OpenedAt" IS NULL AND e.opened IS NOT NULL)
                OR (cc."ClickedAt" IS NULL AND e.clicked IS NOT NULL)
                OR (cc."RepliedAt" IS NULL AND e.replied IS NOT NULL)
                OR (cc."DeliveredAt" IS NULL AND e.delivered IS NOT NULL)
                OR (e.bounced AND cc."Status" NOT IN ('Bounced','Complained')))
            """, ct);

        return await db.Database.ExecuteSqlInterpolatedAsync($"""
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
}
