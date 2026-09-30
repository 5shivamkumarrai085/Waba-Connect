using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Email;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services.Campaigns;

public sealed record AbVariantResult(
    int VariantId, string Label, string? TemplateName, string? SubjectOverride,
    int Recipients, int Sent, int Opened, int Clicked, int Replied, int Read, double Rate, bool IsWinner);

public sealed record AbTestResult(
    string Metric, int TestPercent, DateTime? DecideAt, DateTime? DecidedAt, int? WinnerVariantId, int HeldRecipients,
    IReadOnlyList<AbVariantResult> Variants);

/// <summary>
/// A/B tests: splitting the test share across variants, measuring them, and releasing the
/// winner to the recipients who were held back.
/// </summary>
public interface IAbTestService
{
    Task<AbTestResult?> GetResultAsync(int campaignId, CancellationToken ct = default);

    /// <summary>Picks the winner (or uses <paramref name="forcedVariantId"/>) and sends it to the held recipients.</summary>
    Task<int?> DecideAsync(int campaignId, int? forcedVariantId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class AbTestService : IAbTestService
{
    private readonly AppDbContext _db;
    private readonly IEmailCampaignDispatcher _emailDispatcher;
    private readonly WhatsApp.IWhatsAppCampaignDispatcher _whatsAppDispatcher;
    private readonly IAuditService _audit;
    private readonly ILogger<AbTestService> _logger;

    public AbTestService(
        AppDbContext db, IEmailCampaignDispatcher emailDispatcher, WhatsApp.IWhatsAppCampaignDispatcher whatsAppDispatcher,
        IAuditService audit, ILogger<AbTestService> logger)
    {
        _db = db;
        _emailDispatcher = emailDispatcher;
        _whatsAppDispatcher = whatsAppDispatcher;
        _audit = audit;
        _logger = logger;
    }

    /// <summary>
    /// Deterministic split, entirely in SQL. Unassigned pending recipients are ordered by a
    /// multiplicative hash of their id (a stable shuffle), the first <paramref name="percent"/>%
    /// (rounded up, and at least one per variant) form the test group, and the test group is dealt
    /// round-robin across the variants, so every variant gets an equal share whatever the number
    /// of variants. The rest are held for the winner. Re-running assigns only rows that have no
    /// variant yet (people who joined a segment later), so an assignment already made never changes.
    /// </summary>
    /// <remarks>
    /// The previous version picked the variant with <c>(id * 40503) % n</c>; 40503 is divisible by
    /// 3, so a three-variant test sent every test recipient variant A.
    /// </remarks>
    public static Task<int> AssignAsync(AppDbContext db, int campaignId, int percent, int[] variantIds, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH pool AS (
                SELECT "Id",
                       row_number() OVER (ORDER BY ("Id"::bigint * 2654435761) % 4294967296, "Id") AS rn,
                       count(*) OVER () AS total
                FROM "CampaignContacts"
                WHERE "CampaignId" = {campaignId}
                  AND "Status" = 'Pending'
                  AND "VariantId" IS NULL
                  AND NOT "HeldForWinner"
            ), sized AS (
                SELECT "Id", rn,
                       LEAST(total, GREATEST(LEAST(total, {variantIds.Length}), CEIL(total * {percent} / 100.0)))::bigint AS test_size
                FROM pool
            )
            UPDATE "CampaignContacts" AS cc SET
                "HeldForWinner" = s.rn > s.test_size,
                "VariantId" = CASE
                    WHEN s.rn <= s.test_size THEN ({variantIds})[(((s.rn - 1) % {variantIds.Length}) + 1)::int]
                    ELSE NULL END
            FROM sized AS s
            WHERE cc."Id" = s."Id"
            """, ct);

    /// <summary>After a winner is known, newcomers (segment joiners) simply get the winner.</summary>
    public static Task<int> AssignLateJoinersAsync(AppDbContext db, Campaign campaign, CancellationToken ct)
    {
        if (campaign.AbTestPercent is not { } percent) return Task.FromResult(0);

        if (campaign.AbWinnerVariantId is { } winner)
        {
            return db.CampaignContacts.IgnoreQueryFilters()
                .Where(cc => cc.CampaignId == campaign.Id && cc.Status == MessageStatus.Pending && cc.VariantId == null && !cc.HeldForWinner)
                .ExecuteUpdateAsync(u => u.SetProperty(cc => cc.VariantId, (int?)winner), ct);
        }

        var ids = db.CampaignVariants.AsNoTracking().Where(v => v.CampaignId == campaign.Id).OrderBy(v => v.SortOrder).Select(v => v.Id).ToArray();
        return ids.Length == 0 ? Task.FromResult(0) : AssignAsync(db, campaign.Id, percent, ids, ct);
    }

    public async Task<AbTestResult?> GetResultAsync(int campaignId, CancellationToken ct = default)
    {
        var campaign = await _db.Campaigns.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.Id == campaignId)
            .Select(c => new { c.Channel, c.AbTestPercent, c.AbWinnerMetric, c.AbDecideAt, c.AbDecidedAt, c.AbWinnerVariantId })
            .FirstOrDefaultAsync(ct);
        if (campaign?.AbTestPercent is null) return null;

        var variants = await _db.CampaignVariants.AsNoTracking()
            .Where(v => v.CampaignId == campaignId)
            .OrderBy(v => v.SortOrder)
            .Select(v => new
            {
                v.Id, v.Label, v.SubjectOverride,
                TemplateName = v.TemplateId != null ? v.Template!.Name : v.EmailTemplate != null ? v.EmailTemplate.Name : null
            })
            .ToListAsync(ct);

        var stats = await _db.CampaignContacts.IgnoreQueryFilters().AsNoTracking()
            .Where(cc => cc.CampaignId == campaignId && cc.VariantId != null)
            .GroupBy(cc => cc.VariantId!.Value)
            .Select(g => new
            {
                VariantId = g.Key,
                Recipients = g.Count(),
                Sent = g.Count(cc => cc.SentAt != null),
                Opened = g.Count(cc => cc.OpenedAt != null),
                Clicked = g.Count(cc => cc.ClickedAt != null),
                Replied = g.Count(cc => cc.RepliedAt != null),
                Read = g.Count(cc => cc.ReadAt != null || cc.Status == MessageStatus.Read)
            })
            .ToDictionaryAsync(x => x.VariantId, ct);

        var held = await _db.CampaignContacts.IgnoreQueryFilters().CountAsync(cc => cc.CampaignId == campaignId && cc.HeldForWinner, ct);
        var metric = campaign.AbWinnerMetric ?? DefaultMetric(campaign.Channel);

        var results = variants.Select(v =>
        {
            var s = stats.GetValueOrDefault(v.Id);
            var sent = s?.Sent ?? 0;
            var hits = metric switch { "click" => s?.Clicked ?? 0, "reply" => s?.Replied ?? 0, "read" => s?.Read ?? 0, _ => s?.Opened ?? 0 };
            return new AbVariantResult(
                v.Id, v.Label, v.TemplateName, v.SubjectOverride,
                s?.Recipients ?? 0, sent, s?.Opened ?? 0, s?.Clicked ?? 0, s?.Replied ?? 0, s?.Read ?? 0,
                sent == 0 ? 0 : Math.Round(hits * 100.0 / sent, 1),
                v.Id == campaign.AbWinnerVariantId);
        }).ToList();

        return new AbTestResult(metric, campaign.AbTestPercent.Value, campaign.AbDecideAt, campaign.AbDecidedAt, campaign.AbWinnerVariantId, held, results);
    }

    public async Task<int?> DecideAsync(int campaignId, int? forcedVariantId, CancellationToken ct = default)
    {
        var campaign = await _db.Campaigns.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == campaignId, ct)
            ?? throw new KeyNotFoundException("Campaign not found.");
        if (campaign.AbTestPercent is null) throw new InvalidOperationException("This campaign is not an A/B test.");
        if (campaign.AbDecidedAt is not null) throw new InvalidOperationException("The winner of this test has already been chosen.");
        if (campaign.Status is not (CampaignStatus.Sending or CampaignStatus.Paused))
            throw new InvalidOperationException("The winner can be chosen only while the campaign is sending.");

        var result = await GetResultAsync(campaignId, ct) ?? throw new InvalidOperationException("This campaign is not an A/B test.");

        int winner;
        if (forcedVariantId is { } forced)
        {
            if (result.Variants.All(v => v.VariantId != forced)) throw new ArgumentException("That variant is not part of this test.");
            winner = forced;
        }
        else
        {
            // No data, no decision: if a variant has had nothing accepted yet (the sends are still
            // queued, or were held by a connection problem), picking a "winner" would just pick A.
            // Wait and look again instead.
            var starved = result.Variants.Where(v => v.Recipients > 0 && v.Sent < Catalogs.CampaignFeatureCatalog.AbMinimumSentPerVariant).ToList();
            if (starved.Count > 0)
            {
                campaign.AbDecideAt = DateTime.UtcNow.AddMinutes(Catalogs.CampaignFeatureCatalog.AbRecheckMinutes);
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "A/B test of campaign {CampaignId} postponed: variant(s) {Labels} have no accepted sends yet.",
                    campaignId, string.Join(", ", starved.Select(v => v.Label)));
                return null;
            }

            // Highest rate wins; a tie goes to the earlier variant (A before B), which keeps the
            // original unless a challenger is actually better.
            winner = result.Variants.OrderByDescending(v => v.Rate).ThenBy(v => result.Variants.ToList().IndexOf(v)).First().VariantId;
        }

        var now = DateTime.UtcNow;
        var released = await _db.CampaignContacts.IgnoreQueryFilters()
            .Where(cc => cc.CampaignId == campaignId && cc.HeldForWinner)
            .ExecuteUpdateAsync(u => u
                .SetProperty(cc => cc.VariantId, (int?)winner)
                .SetProperty(cc => cc.HeldForWinner, false), ct);

        campaign.AbWinnerVariantId = winner;
        campaign.AbDecidedAt = now;
        await _db.SaveChangesAsync(ct);

        var label = result.Variants.First(v => v.VariantId == winner).Label;
        await _audit.LogAsync("Campaign.AbTestDecided", "Data",
            $"Variant {label} won the A/B test of campaign \"{campaign.Name}\" ({(forcedVariantId is null ? $"highest {result.Metric} rate" : "chosen manually")}); released to {released} held recipient(s).",
            "Campaign", campaignId.ToString());

        if (released > 0 && campaign.Status == CampaignStatus.Sending)
        {
            if (campaign.Channel == MessageChannel.Email)
                await _emailDispatcher.SubmitAsync(campaignId, null, ct, runId: $"ab-{Guid.NewGuid():N}");
            else
                await _whatsAppDispatcher.StartAsync(campaignId, notBefore: null, ct);
        }

        _logger.LogInformation("A/B test of campaign {CampaignId} decided: variant {Label}, {Released} released.", campaignId, label, released);
        return winner;
    }

    public static string DefaultMetric(MessageChannel channel) => channel == MessageChannel.Email ? "open" : "read";
}

/// <summary>Decides A/B tests whose decision time has come.</summary>
public sealed class AbTestWinnerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AbTestWinnerWorker> _logger;

    public AbTestWinnerWorker(IServiceScopeFactory scopeFactory, ILogger<AbTestWinnerWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Moves each undecided test's decision time to "first test send + window" when that is later
    /// than the stored time. One set-based statement; campaigns that have not sent anything yet
    /// are pushed out by the recheck interval so they are not decided on no data.
    /// </summary>
    internal static Task<int> ReanchorDecisionTimesAsync(AppDbContext db, DateTime now, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Campaigns" AS c SET "AbDecideAt" = GREATEST(c."AbDecideAt", first_send.at + make_interval(hours => c."AbDecideAfterHours"))
            FROM (
                SELECT cc."CampaignId", MIN(cc."SentAt") AS at
                FROM "CampaignContacts" cc
                JOIN "Campaigns" c2 ON c2."Id" = cc."CampaignId"
                WHERE c2."AbTestPercent" IS NOT NULL AND c2."AbDecidedAt" IS NULL AND c2."AbDecideAfterHours" IS NOT NULL
                  AND cc."VariantId" IS NOT NULL AND cc."SentAt" IS NOT NULL
                GROUP BY cc."CampaignId"
            ) AS first_send
            WHERE c."Id" = first_send."CampaignId"
              AND c."AbDecidedAt" IS NULL
              AND first_send.at + make_interval(hours => c."AbDecideAfterHours") > c."AbDecideAt"
            """, ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var now = DateTime.UtcNow;

                var due = await db.Campaigns.IgnoreQueryFilters().AsNoTracking()
                    .Where(c => c.AbTestPercent != null && c.AbDecidedAt == null && c.AbDecideAt <= now
                             && c.Status == CampaignStatus.Sending && !c.IsDeleted)
                    .OrderBy(c => c.AbDecideAt)
                    .Select(c => c.Id)
                    .Take(20)
                    .ToListAsync(stoppingToken);

                // The test window runs from the first test message actually sent, not from when the
                // campaign was saved: approval waits and local-time scheduling used to use it up.
                await ReanchorDecisionTimesAsync(db, now, stoppingToken);
                due = await db.Campaigns.IgnoreQueryFilters().AsNoTracking()
                    .Where(c => due.Contains(c.Id) && c.AbDecideAt <= now)
                    .Select(c => c.Id)
                    .ToListAsync(stoppingToken);

                foreach (var id in due)
                {
                    try
                    {
                        using var decideScope = _scopeFactory.CreateScope();
                        await decideScope.ServiceProvider.GetRequiredService<IAbTestService>().DecideAsync(id, null, stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex, "Could not decide the A/B test of campaign {CampaignId}.", id);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "A/B test sweep failed; retrying.");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
