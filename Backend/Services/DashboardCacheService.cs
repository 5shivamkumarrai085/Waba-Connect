using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services;

public class DashboardCacheService : IDashboardCacheService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _configuration;

    private static readonly string[] CacheKeys =
    {
        "Dashboard_Summary_today",
        "Dashboard_Summary_week",
        "Dashboard_Summary_month",
        "Dashboard_Summary_all"
    };

    // Fixed UTC+5:30 offset for India — no DST to account for, so a constant offset is exact
    // and avoids any TimeZoneInfo lookup / cross-platform timezone-database dependency.
    private static readonly TimeSpan IstOffset = TimeSpan.FromHours(5.5);

    private readonly Security.IAccessScope _accessScope;

    /// <summary>
    /// Cancelled on invalidation. Every cached summary — one per time filter and per connection
    /// scope — listens to it, so one call clears them all.
    /// </summary>
    private static CancellationTokenSource _invalidation = new();

    public DashboardCacheService(IDbContextFactory<AppDbContext> dbContextFactory, IMemoryCache cache, IConfiguration configuration, Security.IAccessScope accessScope)
    {
        _accessScope = accessScope;
        _dbContextFactory = dbContextFactory;
        _cache = cache;
        _configuration = configuration;
    }

    public async Task<object> GetSummaryAsync(string timeFilter)
    {
        string normalizedFilter = (timeFilter ?? "all").ToLower().Trim();
        var validFilters = new HashSet<string> { "today", "week", "month", "all" };
        if (!validFilters.Contains(normalizedFilter))
            normalizedFilter = "all";
        // Connection scoping: a restricted user's figures cover only their connections, and are
        // cached separately — keyed by the scope — so one user's numbers never serve another's.
        var scope = (await _accessScope.GetAllowedConnectionIdsAsync())?.OrderBy(id => id).ToArray();
        string cacheKey = scope is null
            ? $"Dashboard_Summary_{normalizedFilter}"
            : $"Dashboard_Summary_{normalizedFilter}_scope_{string.Join('-', scope)}";

        int ttlSeconds = normalizedFilter switch
        {
            "today" => _configuration.GetValue<int>("DashboardCacheSettings:TodayTtlSeconds", 30),
            "week" => _configuration.GetValue<int>("DashboardCacheSettings:WeekTtlSeconds", 60),
            "month" => _configuration.GetValue<int>("DashboardCacheSettings:MonthTtlSeconds", 180),
            _ => _configuration.GetValue<int>("DashboardCacheSettings:AllTtlSeconds", 300)
        };

        var data = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(ttlSeconds);
            entry.AddExpirationToken(new Microsoft.Extensions.Primitives.CancellationChangeToken(_invalidation.Token));

            var now = DateTime.UtcNow;

            // Current-period cutoff, and the immediately preceding period of equal length (null for "all").
            DateTime? currentCutoff = null;
            DateTime? previousStart = null;
            DateTime? previousEnd = null;

            if (normalizedFilter == "today")
            {
                // "Today" means the user's calendar day, not UTC's. The app targets IST users
                // (see the greeting logic in Frontend/src/pages/Dashboard.tsx, which already
                // hardcodes Asia/Kolkata) — bucketing by raw UTC midnight misclassifies anything
                // sent between IST midnight and 5:30 AM IST as "yesterday". India has no DST, so
                // a fixed offset is exact (no TimeZoneInfo lookup / no timezone-database risk).
                var todayIstDate = now.Add(IstOffset).Date;
                currentCutoff = todayIstDate.Subtract(IstOffset); // UTC instant of IST midnight today
                previousStart = currentCutoff.Value.AddDays(-1);
                previousEnd = currentCutoff.Value;
            }
            else if (normalizedFilter == "week")
            {
                currentCutoff = now.AddDays(-7);
                previousStart = now.AddDays(-14);
                previousEnd = now.AddDays(-7);
            }
            else if (normalizedFilter == "month")
            {
                currentCutoff = now.AddDays(-30);
                previousStart = now.AddDays(-60);
                previousEnd = now.AddDays(-30);
            }

            var sparklineStart = now.Date.AddDays(-6);

            // Each of these opens its own short-lived DbContext (via the factory) so they can run
            // concurrently — AppDbContext itself is scoped/not thread-safe, so a shared context can't
            // be awaited from multiple in-flight tasks at once.
            async Task<PreviousPeriodCounts?> GetPreviousPeriodOrNullAsync()
            {
                if (!previousStart.HasValue || !previousEnd.HasValue) return null;
                return await GetPreviousPeriodCountsAsync(previousStart.Value, previousEnd.Value, scope);
            }

            var coreCountsTask = GetCoreCountsAsync(currentCutoff, scope);
            var previousPeriodTask = GetPreviousPeriodOrNullAsync();
            var sparklinesTask = GetSparklinesAsync(sparklineStart, scope);
            var hourlyTask = GetHourlyChartAsync(currentCutoff, scope);
            var topAndRecentCampaignsTask = GetTopAndRecentCampaignsAsync(currentCutoff, scope);
            var recentActivityTask = GetRecentActivityAndBusinessNameAsync(scope);
        var channelBreakdownTask = GetChannelBreakdownAsync(currentCutoff, scope);

            await Task.WhenAll(
                coreCountsTask,
                previousPeriodTask,
                sparklinesTask,
                hourlyTask,
                topAndRecentCampaignsTask,
                recentActivityTask,
                channelBreakdownTask);

            var core = coreCountsTask.Result;
            var prev = previousPeriodTask.Result;
            var sparklines = sparklinesTask.Result;
            var hourly = hourlyTask.Result;
            var topAndRecent = topAndRecentCampaignsTask.Result;
            var recentActivityResult = recentActivityTask.Result;
            var channelBreakdown = channelBreakdownTask.Result;

            // Previous-period comparison (null for "all", where there is no meaningful prior window).
            object? previousPeriod = null;
            if (prev != null)
            {
                previousPeriod = new
                {
                    messagesChangePercent = PercentChange(core.MessagesSent, prev.MessagesCount),
                    contactsChangePercent = PercentChange(core.TotalContacts, prev.ContactsCount),
                    campaignsChangePercent = PercentChange(core.TotalCampaigns, prev.CampaignsCount),
                    templatesChangePercent = PercentChange(core.TemplatesTotal, prev.TemplatesCount)
                };
            }

            // Overall Rates
            double overallDeliveryRate = core.MessagesSent > 0 ? ((double)core.MessagesDelivered / core.MessagesSent) * 100 : 0;
            double overallReadRate = core.MessagesDelivered > 0 ? ((double)core.MessagesRead / core.MessagesDelivered) * 100 : 0;

            // Delivery / Read breakdown for the donut widget (both always returned; frontend toggles client-side).
            object deliveryBreakdown = new
            {
                delivered = core.MessagesDelivered,
                failed = core.MessagesFailed,
                pending = core.MessagesPending,
                deliveredPercent = core.MessagesSent > 0 ? Math.Round((double)core.MessagesDelivered / core.MessagesSent * 100, 1) : 0,
                failedPercent = core.MessagesSent > 0 ? Math.Round((double)core.MessagesFailed / core.MessagesSent * 100, 1) : 0,
                pendingPercent = core.MessagesSent > 0 ? Math.Round((double)core.MessagesPending / core.MessagesSent * 100, 1) : 0
            };

            var messagesUnread = Math.Max(core.MessagesDelivered - core.MessagesRead, 0);
            var messagesNotDelivered = core.MessagesFailed + core.MessagesPending;
            object readBreakdown = new
            {
                read = core.MessagesRead,
                unread = messagesUnread,
                notDelivered = messagesNotDelivered,
                readPercent = core.MessagesSent > 0 ? Math.Round((double)core.MessagesRead / core.MessagesSent * 100, 1) : 0,
                unreadPercent = core.MessagesSent > 0 ? Math.Round((double)messagesUnread / core.MessagesSent * 100, 1) : 0,
                notDeliveredPercent = core.MessagesSent > 0 ? Math.Round((double)messagesNotDelivered / core.MessagesSent * 100, 1) : 0
            };

            return new
            {
                totalContacts = core.TotalContacts,
                contactsActive = core.ContactsActive,
                totalCampaigns = core.TotalCampaigns,
                campaignsActive = core.CampaignsActive,
                templatesTotal = core.TemplatesTotal,
                templatesApproved = core.TemplatesApproved,
                messagesSent = core.MessagesSent,
                messagesDelivered = core.MessagesDelivered,
                messagesRead = core.MessagesRead,
                messagesFailed = core.MessagesFailed,
                messagesReplies = core.MessagesReplies,
                messagesPending = core.MessagesPending,
                previousPeriod,
                messagesSparkline = sparklines.MessagesSparkline,
                contactsSparkline = sparklines.ContactsSparkline,
                campaignsSparkline = sparklines.CampaignsSparkline,
                templatesSparkline = sparklines.TemplatesSparkline,
                recentCampaigns = topAndRecent.RecentCampaigns,
                hourlyChartData = hourly.HourlyChartData,
                dailyChartData = hourly.DailyChartData,
                deliveryTrend = hourly.DeliveryTrend,
                readTrend = hourly.ReadTrend,
                topCampaigns = topAndRecent.TopCampaigns,
                deliveryBreakdown,
                readBreakdown,
                recentActivity = recentActivityResult.RecentActivity,
                channelBreakdown,
                businessName = recentActivityResult.BusinessName,
                overallDeliveryRate = Math.Round(overallDeliveryRate, 2),
                overallReadRate = Math.Round(overallReadRate, 2)
            } as object;
        });

        return data!;
    }

    public void InvalidateCache()
    {
        foreach (var key in CacheKeys)
        {
            _cache.Remove(key);
        }

        // The scoped variants have per-user keys; the shared token expires all of them.
        var previous = Interlocked.Exchange(ref _invalidation, new CancellationTokenSource());
        previous.Cancel();
        previous.Dispose();
    }

    private static double PercentChange(int current, int previous)
    {
        if (previous == 0) return current > 0 ? 100.0 : 0.0;
        return Math.Round(((double)(current - previous) / previous) * 100, 1);
    }

    private record CoreCounts(
        int TotalContacts, int ContactsActive,
        int TotalCampaigns, int CampaignsActive,
        int TemplatesTotal, int TemplatesApproved,
        int MessagesSent, int MessagesDelivered, int MessagesRead, int MessagesFailed, int MessagesPending,
        int MessagesReplies);

    private record PreviousPeriodCounts(int ContactsCount, int CampaignsCount, int TemplatesCount, int MessagesCount);

    private record SparklineData(
        List<object> MessagesSparkline, List<object> ContactsSparkline,
        List<object> CampaignsSparkline, List<object> TemplatesSparkline);

    private record HourlyChartResult(List<object> HourlyChartData, List<object> DailyChartData, List<object> DeliveryTrend, List<object> ReadTrend);

    private record TopAndRecentCampaigns(List<object> RecentCampaigns, List<object> TopCampaigns);

    private record RecentActivityResult(List<object> RecentActivity, string? BusinessName);

    // Contacts/campaigns/templates totals + message status breakdown for the active period.
    // Campaign/template counts and the campaign-id filter used for message status are all done
    // SQL-side (CountAsync / subquery), never materializing full campaign or template tables.
    /// <summary>One channel's totals for the dashboard's distribution and performance widgets.</summary>
    /// <param name="Channel">"WhatsApp" or "Email".</param>
    /// <param name="Messages">
    /// Everything that left the system, whatever happened next. Excludes suppressed recipients:
    /// those were never sent, and counting them would understate the delivery rate of a channel
    /// that is behaving correctly.
    /// </param>
    /// <param name="SharePercent">Share of all channels' messages, for the distribution donut.</param>
    private sealed record ChannelBreakdownRow(
        string Channel,
        int Messages,
        int Delivered,
        int Failed,
        int Read,
        int Pending,
        int Suppressed,
        int Replies,
        double DeliveryRate,
        /// <summary>Replies as a share of what was sent — how engaged this channel's audience is.</summary>
        double ReplyRate,
        double SharePercent);

    /// <summary>
    /// Per-channel message totals.
    ///
    /// <para>
    /// Scoped by the same campaign-id subquery the core counts use, so the per-channel numbers
    /// add up to the totals shown beside them — deriving them from a different filter is how a
    /// dashboard ends up contradicting itself.
    /// </para>
    /// <para>
    /// Every channel is returned, including ones with no traffic: an absent Email row and an
    /// Email row reading zero mean different things, and only the second says "configured but
    /// unused".
    /// </para>
    /// </summary>
    private async Task<List<ChannelBreakdownRow>> GetChannelBreakdownAsync(DateTime? currentCutoff, int[]? scope)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var queryCampaigns = db.Campaigns.ScopeTo(scope).AsNoTracking();
        if (currentCutoff.HasValue)
        {
            queryCampaigns = queryCampaigns.Where(c => c.CreatedAt >= currentCutoff.Value);
        }

        // One grouped round trip rather than a query per channel per status.
        var campaignIdsInPeriod = queryCampaigns.Select(c => c.Id);
        var rows = await db.CampaignContacts.ScopeTo(scope).AsNoTracking()
            .Where(cc => campaignIdsInPeriod.Contains(cc.CampaignId))
            .GroupBy(cc => new { cc.Campaign.Channel, cc.Status, IsOpened = cc.OpenedAt != null })
            .Select(g => new { g.Key.Channel, g.Key.Status, g.Key.IsOpened, Count = g.Count() })
            .ToListAsync();

        // Inbound messages per channel, in one grouped query rather than one per channel.
        var repliesQuery = db.ChatMessages.ScopeTo(scope).AsNoTracking()
            .Where(m => m.Direction == ChatMessageDirection.Incoming);

        if (currentCutoff.HasValue)
        {
            repliesQuery = repliesQuery.Where(m => m.CreatedAt >= currentCutoff.Value);
        }

        var repliesByChannel = await repliesQuery
            .GroupBy(m => m.Channel)
            .Select(g => new { Channel = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Channel, g => g.Count);

        var perChannel = new List<ChannelBreakdownRow>();

        foreach (var channel in Enum.GetValues<MessageChannel>())
        {
            var forChannel = rows.Where(r => r.Channel == channel).ToList();
            int CountOf(MessageStatus status) => forChannel.Where(r => r.Status == status).Sum(r => r.Count);

            var isEmail = channel == MessageChannel.Email;
            var read = isEmail
                ? forChannel.Where(r => r.IsOpened || r.Status == MessageStatus.Read).Sum(r => r.Count)
                : CountOf(MessageStatus.Read);
            var delivered = isEmail
                ? (CountOf(MessageStatus.Sent) + CountOf(MessageStatus.Delivered) + read)
                : (CountOf(MessageStatus.Delivered) + read);
            var failed = CountOf(MessageStatus.Failed)
                       + CountOf(MessageStatus.Bounced)
                       + CountOf(MessageStatus.Complained);
            var suppressed = CountOf(MessageStatus.Suppressed);
            var pending = CountOf(MessageStatus.Pending);
            var messages = forChannel.Where(r => r.Status != MessageStatus.Suppressed).Sum(r => r.Count);

            var replies = repliesByChannel.TryGetValue(channel, out var replyCount) ? replyCount : 0;

            perChannel.Add(new ChannelBreakdownRow(
                Channel: channel.ToString(),
                Messages: messages,
                Delivered: delivered,
                Failed: failed,
                Read: read,
                Pending: pending,
                Suppressed: suppressed,
                Replies: replies,
                DeliveryRate: messages > 0 ? Math.Round((double)delivered / messages * 100, 1) : 0,
                ReplyRate: messages > 0 ? Math.Round((double)replies / messages * 100, 1) : 0,
                SharePercent: 0));
        }

        var grandTotal = perChannel.Sum(r => r.Messages);

        return perChannel
            .Select(r => r with
            {
                SharePercent = grandTotal > 0 ? Math.Round((double)r.Messages / grandTotal * 100, 1) : 0
            })
            .ToList();
    }

    private async Task<CoreCounts> GetCoreCountsAsync(DateTime? currentCutoff, int[]? scope)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var queryContacts = db.Contacts.AsNoTracking();
        var queryCampaigns = db.Campaigns.ScopeTo(scope).AsNoTracking();
        var queryTemplates = db.Templates.AsNoTracking();

        if (currentCutoff.HasValue)
        {
            queryContacts = queryContacts.Where(c => c.CreatedAt >= currentCutoff.Value);
            queryCampaigns = queryCampaigns.Where(c => c.CreatedAt >= currentCutoff.Value);
            queryTemplates = queryTemplates.Where(t => t.CreatedAt >= currentCutoff.Value);
        }

        var totalContacts = await queryContacts.CountAsync();
        var contactsActive = await queryContacts.CountAsync(c => c.IsActive);

        var totalCampaigns = await queryCampaigns.CountAsync();
        var campaignsActive = await queryCampaigns.CountAsync(c => c.Status == CampaignStatus.Sending || c.Status == CampaignStatus.Scheduled);

        var templatesTotal = await queryTemplates.CountAsync();
        var templatesApproved = await queryTemplates.CountAsync(t => t.Status == TemplateStatus.Approved);

        var campaignIdsInPeriod = queryCampaigns.Select(c => c.Id);
        var statusRows = await db.CampaignContacts.ScopeTo(scope).AsNoTracking()
            .Where(cc => campaignIdsInPeriod.Contains(cc.CampaignId))
            .GroupBy(cc => new { cc.Campaign.Channel, cc.Status, IsOpened = cc.OpenedAt != null })
            .Select(g => new { g.Key.Channel, g.Key.Status, g.Key.IsOpened, Count = g.Count() })
            .ToListAsync();

        var messagesSent = statusRows
            .Where(r => r.Status != MessageStatus.Pending && r.Status != MessageStatus.Suppressed)
            .Sum(r => r.Count);

        var messagesDelivered = statusRows
            .Where(r => r.Channel == MessageChannel.Email
                ? (r.Status == MessageStatus.Sent || r.Status == MessageStatus.Delivered || r.Status == MessageStatus.Read)
                : (r.Status == MessageStatus.Delivered || r.Status == MessageStatus.Read))
            .Sum(r => r.Count);

        var messagesRead = statusRows
            .Where(r => r.Channel == MessageChannel.Email
                ? (r.IsOpened || r.Status == MessageStatus.Read)
                : (r.Status == MessageStatus.Read))
            .Sum(r => r.Count);

        var messagesFailed = statusRows
            .Where(r => r.Status == MessageStatus.Failed || r.Status == MessageStatus.Bounced || r.Status == MessageStatus.Complained)
            .Sum(r => r.Count);

        var messagesPending = statusRows
            .Where(r => r.Channel == MessageChannel.Email
                ? r.Status == MessageStatus.Pending
                : (r.Status == MessageStatus.Pending || r.Status == MessageStatus.Sent))
            .Sum(r => r.Count);

        var queryReplies = db.ChatMessages.ScopeTo(scope).AsNoTracking()
            .Where(m => m.Direction == ChatMessageDirection.Incoming);

        if (currentCutoff.HasValue)
        {
            queryReplies = queryReplies.Where(m => m.CreatedAt >= currentCutoff.Value);
        }

        var messagesReplies = await queryReplies.CountAsync();

        return new CoreCounts(
            totalContacts, contactsActive,
            totalCampaigns, campaignsActive,
            templatesTotal, templatesApproved,
            messagesSent, messagesDelivered, messagesRead, messagesFailed, messagesPending,
            messagesReplies);
    }

    // Raw counts for the immediately preceding period of equal length, used to compute the change-percent shown per stat card.
    private async Task<PreviousPeriodCounts> GetPreviousPeriodCountsAsync(DateTime previousStart, DateTime previousEnd, int[]? scope)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var prevCampaignsQuery = db.Campaigns.ScopeTo(scope).AsNoTracking()
            .Where(c => c.CreatedAt >= previousStart && c.CreatedAt < previousEnd);

        var prevContactsCount = await db.Contacts.AsNoTracking()
            .CountAsync(c => c.CreatedAt >= previousStart && c.CreatedAt < previousEnd);
        var prevCampaignsCount = await prevCampaignsQuery.CountAsync();
        var prevTemplatesCount = await db.Templates.AsNoTracking()
            .CountAsync(t => t.CreatedAt >= previousStart && t.CreatedAt < previousEnd);

        var prevCampaignIds = prevCampaignsQuery.Select(c => c.Id);
        var prevMessagesCount = await db.CampaignContacts.ScopeTo(scope).AsNoTracking()
            .CountAsync(cc => prevCampaignIds.Contains(cc.CampaignId) && cc.Status != MessageStatus.Pending);

        return new PreviousPeriodCounts(prevContactsCount, prevCampaignsCount, prevTemplatesCount, prevMessagesCount);
    }

    // 7-day sparkline trend for each stat card (independent of the active timeFilter, always daily granularity).
    // Each series is aggregated in SQL (GROUP BY day) instead of pulling every row into memory and counting.
    private async Task<SparklineData> GetSparklinesAsync(DateTime sparklineStart, int[]? scope)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var contactsByDay = await db.Contacts.AsNoTracking()
            .Where(c => c.CreatedAt >= sparklineStart)
            .GroupBy(c => c.CreatedAt.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Day, g => g.Count);

        var campaignsByDay = await db.Campaigns.ScopeTo(scope).AsNoTracking()
            .Where(c => c.CreatedAt >= sparklineStart)
            .GroupBy(c => c.CreatedAt.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Day, g => g.Count);

        var templatesByDay = await db.Templates.AsNoTracking()
            .Where(t => t.CreatedAt >= sparklineStart)
            .GroupBy(t => t.CreatedAt.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Day, g => g.Count);

        // A message's "day" for this trend is its owning campaign's CreatedAt date (matching the semantics above).
        var messagesByDay = await db.CampaignContacts.ScopeTo(scope).AsNoTracking()
            .Where(cc => cc.Status != MessageStatus.Pending && cc.Campaign.CreatedAt >= sparklineStart)
            .GroupBy(cc => cc.Campaign.CreatedAt.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Day, g => g.Count);

        var messagesSparkline = new List<object>();
        var contactsSparkline = new List<object>();
        var campaignsSparkline = new List<object>();
        var templatesSparkline = new List<object>();

        for (int i = 0; i < 7; i++)
        {
            var day = sparklineStart.AddDays(i);
            messagesSparkline.Add(new { name = day.ToString("MM/dd"), value = messagesByDay.GetValueOrDefault(day) });
            contactsSparkline.Add(new { name = day.ToString("MM/dd"), value = contactsByDay.GetValueOrDefault(day) });
            campaignsSparkline.Add(new { name = day.ToString("MM/dd"), value = campaignsByDay.GetValueOrDefault(day) });
            templatesSparkline.Add(new { name = day.ToString("MM/dd"), value = templatesByDay.GetValueOrDefault(day) });
        }

        return new SparklineData(messagesSparkline, contactsSparkline, campaignsSparkline, templatesSparkline);
    }

    // Hourly line-chart data needs each message's own SentAt timestamp for hour-of-day bucketing
    // (campaign CreatedAt can't tell us what hour a specific message went out), scoped to this period.
    //
    // SentAt is stored in UTC (Postgres timestamptz), but the chart's hour labels ("00:00".."23:00")
    // are displayed as IST wall-clock hours (the app targets IST users — see Dashboard.tsx's greeting
    // logic). Bucketing by raw UTC hour put "now" ~5.5 hours behind where a user expects to see it.
    // The IST-shifted day is small (one day's messages), so it's cheap to pull into memory and bucket
    // there — this sidesteps any risk of DateTime-arithmetic-inside-GroupBy failing to translate to SQL.
    private async Task<HourlyChartResult> GetHourlyChartAsync(DateTime? currentCutoff, int[]? scope)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var hourlyBaseQuery = db.CampaignContacts.ScopeTo(scope).AsNoTracking().Where(cc => cc.SentAt != null);
        if (currentCutoff.HasValue)
        {
            hourlyBaseQuery = hourlyBaseQuery.Where(cc => cc.SentAt >= currentCutoff.Value);
        }

        // Cheap, fully SQL-translatable lookup — no arithmetic in the query.
        var latestSentAtUtc = await hourlyBaseQuery
            .OrderByDescending(cc => cc.SentAt)
            .Select(cc => cc.SentAt!.Value)
            .FirstOrDefaultAsync();

        var latestIst = latestSentAtUtc == default
            ? DateTime.UtcNow.Add(IstOffset)
            : latestSentAtUtc.Add(IstOffset);
        var latestDateIst = latestIst.Date;

        // UTC instant range covering that IST calendar day — plain comparisons, translates trivially.
        var dayStartUtc = latestDateIst.Subtract(IstOffset);
        var dayEndUtc = dayStartUtc.AddDays(1);

        var dayRows = await hourlyBaseQuery
            .Where(cc => cc.SentAt >= dayStartUtc && cc.SentAt < dayEndUtc)
            .Select(cc => new { cc.Campaign.Channel, cc.SentAt, cc.Status, IsOpened = cc.OpenedAt != null })
            .ToListAsync();

        // Bucket by IST hour in memory — trivial LINQ-to-Objects, zero SQL-translation risk.
        var hourlyAggregates = dayRows
            .GroupBy(cc => cc.SentAt!.Value.Add(IstOffset).Hour)
            .ToDictionary(g => g.Key, g => new
            {
                Sent = g.Count(),
                WhatsAppSent = g.Count(cc => cc.Channel == MessageChannel.WhatsApp),
                EmailSent = g.Count(cc => cc.Channel == MessageChannel.Email),
                Errors = g.Count(cc => cc.Status == MessageStatus.Failed || cc.Status == MessageStatus.Bounced || cc.Status == MessageStatus.Complained),
                WhatsAppErrors = g.Count(cc => cc.Channel == MessageChannel.WhatsApp && (cc.Status == MessageStatus.Failed || cc.Status == MessageStatus.Bounced || cc.Status == MessageStatus.Complained)),
                EmailErrors = g.Count(cc => cc.Channel == MessageChannel.Email && (cc.Status == MessageStatus.Failed || cc.Status == MessageStatus.Bounced || cc.Status == MessageStatus.Complained)),
                Delivered = g.Count(cc => cc.Channel == MessageChannel.Email
                    ? (cc.Status == MessageStatus.Sent || cc.Status == MessageStatus.Delivered || cc.Status == MessageStatus.Read)
                    : (cc.Status == MessageStatus.Delivered || cc.Status == MessageStatus.Read)),
                Read = g.Count(cc => cc.Channel == MessageChannel.Email
                    ? (cc.IsOpened || cc.Status == MessageStatus.Read)
                    : (cc.Status == MessageStatus.Read))
            });

        var hourlyChartData = new List<object>();
        var deliveryTrend = new List<object>();
        var readTrend = new List<object>();

        for (int i = 0; i < 24; i++)
        {
            var hourStr = $"{i:D2}:00";
            hourlyAggregates.TryGetValue(i, out var agg);

            var sent = agg?.Sent ?? 0;
            var errors = agg?.Errors ?? 0;
            var wa = agg?.WhatsAppSent ?? 0;
            var em = agg?.EmailSent ?? 0;
            var delivered = agg?.Delivered ?? 0;
            var read = agg?.Read ?? 0;

            hourlyChartData.Add(new
            {
                name = hourStr,
                sent,
                errors,
                whatsapp = wa,
                email = em,
                whatsappErrors = agg?.WhatsAppErrors ?? 0,
                emailErrors = agg?.EmailErrors ?? 0
            });

            double delRate = sent > 0 ? ((double)delivered / sent) * 100 : 0;
            double rdRate = delivered > 0 ? ((double)read / delivered) * 100 : 0;

            deliveryTrend.Add(new { name = hourStr, value = Math.Round(delRate, 2) });
            readTrend.Add(new { name = hourStr, value = Math.Round(rdRate, 2) });
        }

        // Also build 7-day daily volume trend for multi-channel line/area chart view
        var sevenDaysAgoIst = DateTime.UtcNow.Add(IstOffset).Date.AddDays(-6);
        var sevenDaysStartUtc = sevenDaysAgoIst.Subtract(IstOffset);

        var dailyTrendRows = await db.CampaignContacts.ScopeTo(scope).AsNoTracking()
            .Where(cc => cc.SentAt != null && cc.SentAt >= sevenDaysStartUtc)
            .Select(cc => new { cc.Campaign.Channel, cc.SentAt, cc.Status })
            .ToListAsync();

        var dailyChartData = new List<object>();
        for (int d = 0; d < 7; d++)
        {
            var targetDayIst = sevenDaysAgoIst.AddDays(d);
            var dayRangeStartUtc = targetDayIst.Subtract(IstOffset);
            var dayRangeEndUtc = dayRangeStartUtc.AddDays(1);

            var rowsForDay = dailyTrendRows.Where(r => r.SentAt >= dayRangeStartUtc && r.SentAt < dayRangeEndUtc).ToList();
            var waCount = rowsForDay.Count(r => r.Channel == MessageChannel.WhatsApp);
            var emCount = rowsForDay.Count(r => r.Channel == MessageChannel.Email);
            var errCount = rowsForDay.Count(r => r.Status == MessageStatus.Failed || r.Status == MessageStatus.Bounced || r.Status == MessageStatus.Complained);

            dailyChartData.Add(new
            {
                name = targetDayIst.ToString("MMM d"),
                fullDate = targetDayIst.ToString("MMM d, yyyy"),
                whatsapp = waCount,
                email = emCount,
                sent = waCount + emCount,
                errors = errCount
            });
        }

        return new HourlyChartResult(hourlyChartData, dailyChartData, deliveryTrend, readTrend);
    }

    // Top-N campaign lists (most recent, and top-by-created-date with delivery/read rates) fetched
    // directly as their own targeted SQL queries — never derived from a fully materialized campaigns table.
    private async Task<TopAndRecentCampaigns> GetTopAndRecentCampaignsAsync(DateTime? currentCutoff, int[]? scope)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var queryCampaigns = db.Campaigns.ScopeTo(scope).AsNoTracking();
        if (currentCutoff.HasValue)
        {
            queryCampaigns = queryCampaigns.Where(c => c.CreatedAt >= currentCutoff.Value);
        }

        var recentCampaigns = await queryCampaigns
            .OrderByDescending(c => c.Id)
            .Take(5)
            .Select(c => new
            {
                id = c.Id,
                name = c.Name,
                status = c.Status.ToString(),
                channel = c.Channel.ToString().ToLower(),
                totalRecipients = c.TotalRecipients,
                deliveredCount = c.Channel == MessageChannel.Email ? c.SentCount : c.DeliveredCount
            })
            .ToListAsync();

        var topCampaignsRaw = await queryCampaigns
            .OrderByDescending(c => c.CreatedAt)
            .Take(5)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.CreatedAt,
                c.Status,
                c.Channel,
                c.TotalRecipients,
                c.SentCount,
                c.DeliveredCount,
                c.ReadCount,
                c.OpenedCount
            })
            .ToListAsync();

        // Delivery/read rates are percentages derived from the raw counts above — computed here rather
        // than in the SQL projection to keep the query itself trivially translatable.
        var topCampaigns = topCampaignsRaw
            .Select(c =>
            {
                var delivered = c.Channel == MessageChannel.Email ? c.SentCount : c.DeliveredCount;
                var read = c.Channel == MessageChannel.Email ? c.OpenedCount : c.ReadCount;
                return new
                {
                    id = c.Id,
                    name = c.Name,
                    createdAt = c.CreatedAt,
                    status = c.Status.ToString(),
                    channel = c.Channel.ToString().ToLower(),
                    messages = c.TotalRecipients,
                    delivered = delivered,
                    deliveryRate = c.TotalRecipients > 0 ? Math.Round((double)delivered / c.TotalRecipients * 100, 2) : 0,
                    readRate = delivered > 0 ? Math.Round((double)read / delivered * 100, 2) : 0
                };
            })
            .ToList();

        return new TopAndRecentCampaigns(
            recentCampaigns.Cast<object>().ToList(),
            topCampaigns.Cast<object>().ToList());
    }

    // Synthesized Recent Activity feed — merged from existing tables' own timestamps, no dedicated log table.
    private async Task<RecentActivityResult> GetRecentActivityAndBusinessNameAsync(int[]? scope)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var activityItems = new List<(DateTime Timestamp, object Item)>();

        var recentContacts = await db.Contacts.AsNoTracking()
            .OrderByDescending(c => c.CreatedAt).Take(5)
            .Select(c => new { c.Name, c.Phone, c.CreatedAt }).ToListAsync();
        foreach (var c in recentContacts)
        {
            activityItems.Add((c.CreatedAt, new
            {
                type = "contact",
                title = "New contact added",
                subtitle = $"{c.Name} • {c.Phone}",
                timestamp = c.CreatedAt
            }));
        }

        var recentCampaignActivity = await db.Campaigns.ScopeTo(scope).AsNoTracking()
            .Where(c => c.Status == CampaignStatus.Sent || c.Status == CampaignStatus.Sending || c.Status == CampaignStatus.Scheduled)
            .OrderByDescending(c => c.UpdatedAt).Take(5)
            .Select(c => new { c.Name, c.Status, c.UpdatedAt }).ToListAsync();
        foreach (var c in recentCampaignActivity)
        {
            var verb = c.Status switch
            {
                CampaignStatus.Sent => "was sent",
                CampaignStatus.Sending => "is sending",
                CampaignStatus.Scheduled => "was scheduled",
                _ => "was updated"
            };
            activityItems.Add((c.UpdatedAt, new
            {
                type = "campaign",
                title = $"Campaign \"{c.Name}\" {verb}",
                subtitle = (string?)null,
                timestamp = c.UpdatedAt
            }));
        }

        var recentTemplateActivity = await db.Templates.AsNoTracking()
            .OrderByDescending(t => t.UpdatedAt).Take(5)
            .Select(t => new { t.Name, t.Status, t.UpdatedAt }).ToListAsync();
        foreach (var t in recentTemplateActivity)
        {
            activityItems.Add((t.UpdatedAt, new
            {
                type = "template",
                title = $"Template \"{t.Name}\" {(t.Status == TemplateStatus.Approved ? "was approved" : "was updated")}",
                subtitle = (string?)null,
                timestamp = t.UpdatedAt
            }));
        }

        var recentBotFlowActivity = await db.BotFlows.AsNoTracking()
            .OrderByDescending(b => b.UpdatedAt).Take(5)
            .Select(b => new { b.Name, b.IsActive, b.UpdatedAt }).ToListAsync();
        foreach (var b in recentBotFlowActivity)
        {
            activityItems.Add((b.UpdatedAt, new
            {
                type = "botflow",
                title = $"Bot flow \"{b.Name}\" {(b.IsActive ? "was published" : "was disabled")}",
                subtitle = (string?)null,
                timestamp = b.UpdatedAt
            }));
        }

        var recentActivity = activityItems
            .OrderByDescending(a => a.Timestamp)
            .Take(8)
            .Select(a => a.Item)
            .ToList();

        var businessName = await db.Businesses.AsNoTracking()
            .OrderByDescending(b => b.Id)
            .Select(b => b.BusinessName)
            .FirstOrDefaultAsync();

        return new RecentActivityResult(recentActivity, businessName);
    }
}

/// <summary>Connection-scope filters for the dashboard's queries. A null scope means unrestricted.</summary>
internal static class DashboardScopeExtensions
{
    public static IQueryable<Models.Entities.Campaign> ScopeTo(this IQueryable<Models.Entities.Campaign> query, int[]? scope) =>
        scope is null ? query : query.Where(c => c.ConnectionId != null && scope.Contains(c.ConnectionId.Value));

    public static IQueryable<Models.Entities.CampaignContact> ScopeTo(this IQueryable<Models.Entities.CampaignContact> query, int[]? scope) =>
        scope is null ? query : query.Where(cc => cc.Campaign.ConnectionId != null && scope.Contains(cc.Campaign.ConnectionId.Value));

    public static IQueryable<Models.Entities.ChatMessage> ScopeTo(this IQueryable<Models.Entities.ChatMessage> query, int[]? scope) =>
        scope is null ? query : query.Where(m => m.ConnectionId != null && scope.Contains(m.ConnectionId.Value));
}
