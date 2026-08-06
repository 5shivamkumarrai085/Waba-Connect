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

    public DashboardCacheService(IDbContextFactory<AppDbContext> dbContextFactory, IMemoryCache cache, IConfiguration configuration)
    {
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
        string cacheKey = $"Dashboard_Summary_{normalizedFilter}";

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
                return await GetPreviousPeriodCountsAsync(previousStart.Value, previousEnd.Value);
            }

            var coreCountsTask = GetCoreCountsAsync(currentCutoff);
            var previousPeriodTask = GetPreviousPeriodOrNullAsync();
            var sparklinesTask = GetSparklinesAsync(sparklineStart);
            var hourlyTask = GetHourlyChartAsync(currentCutoff);
            var topAndRecentCampaignsTask = GetTopAndRecentCampaignsAsync(currentCutoff);
            var recentActivityTask = GetRecentActivityAndBusinessNameAsync();

            await Task.WhenAll(
                coreCountsTask,
                previousPeriodTask,
                sparklinesTask,
                hourlyTask,
                topAndRecentCampaignsTask,
                recentActivityTask);

            var core = coreCountsTask.Result;
            var prev = previousPeriodTask.Result;
            var sparklines = sparklinesTask.Result;
            var hourly = hourlyTask.Result;
            var topAndRecent = topAndRecentCampaignsTask.Result;
            var recentActivityResult = recentActivityTask.Result;

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
                messagesPending = core.MessagesPending,
                previousPeriod,
                messagesSparkline = sparklines.MessagesSparkline,
                contactsSparkline = sparklines.ContactsSparkline,
                campaignsSparkline = sparklines.CampaignsSparkline,
                templatesSparkline = sparklines.TemplatesSparkline,
                recentCampaigns = topAndRecent.RecentCampaigns,
                hourlyChartData = hourly.HourlyChartData,
                deliveryTrend = hourly.DeliveryTrend,
                readTrend = hourly.ReadTrend,
                topCampaigns = topAndRecent.TopCampaigns,
                deliveryBreakdown,
                readBreakdown,
                recentActivity = recentActivityResult.RecentActivity,
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
        int MessagesSent, int MessagesDelivered, int MessagesRead, int MessagesFailed, int MessagesPending);

    private record PreviousPeriodCounts(int ContactsCount, int CampaignsCount, int TemplatesCount, int MessagesCount);

    private record SparklineData(
        List<object> MessagesSparkline, List<object> ContactsSparkline,
        List<object> CampaignsSparkline, List<object> TemplatesSparkline);

    private record HourlyChartResult(List<object> HourlyChartData, List<object> DeliveryTrend, List<object> ReadTrend);

    private record TopAndRecentCampaigns(List<object> RecentCampaigns, List<object> TopCampaigns);

    private record RecentActivityResult(List<object> RecentActivity, string? BusinessName);

    // Contacts/campaigns/templates totals + message status breakdown for the active period.
    // Campaign/template counts and the campaign-id filter used for message status are all done
    // SQL-side (CountAsync / subquery), never materializing full campaign or template tables.
    private async Task<CoreCounts> GetCoreCountsAsync(DateTime? currentCutoff)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var queryContacts = db.Contacts.AsNoTracking();
        var queryCampaigns = db.Campaigns.AsNoTracking();
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

        // Message aggregates are scoped by the *campaign's* CreatedAt (via campaign id), not CampaignContact.SentAt —
        // some historical rows have Status set (Sent/Delivered/...) without SentAt ever being populated, so a
        // SentAt-based filter would silently drop real messages. Status is the reliable source of truth here.
        // The campaign-id filter is a correlated SQL subquery (queryCampaigns.Select(Id)), never materialized client-side.
        var campaignIdsInPeriod = queryCampaigns.Select(c => c.Id);
        var statusCounts = await db.CampaignContacts.AsNoTracking()
            .Where(cc => campaignIdsInPeriod.Contains(cc.CampaignId))
            .GroupBy(cc => cc.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Count);

        int CountByStatus(MessageStatus status) => statusCounts.TryGetValue(status, out var c) ? c : 0;

        var messagesSent = statusCounts.Where(kv => kv.Key != MessageStatus.Pending).Sum(kv => kv.Value);
        var messagesDelivered = CountByStatus(MessageStatus.Delivered) + CountByStatus(MessageStatus.Read);
        var messagesRead = CountByStatus(MessageStatus.Read);
        var messagesFailed = CountByStatus(MessageStatus.Failed);
        // In-flight: dispatched but not yet resolved to Delivered/Read/Failed. delivered+failed+pending == messagesSent.
        var messagesPending = CountByStatus(MessageStatus.Sent);

        return new CoreCounts(
            totalContacts, contactsActive,
            totalCampaigns, campaignsActive,
            templatesTotal, templatesApproved,
            messagesSent, messagesDelivered, messagesRead, messagesFailed, messagesPending);
    }

    // Raw counts for the immediately preceding period of equal length, used to compute the change-percent shown per stat card.
    private async Task<PreviousPeriodCounts> GetPreviousPeriodCountsAsync(DateTime previousStart, DateTime previousEnd)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var prevCampaignsQuery = db.Campaigns.AsNoTracking()
            .Where(c => c.CreatedAt >= previousStart && c.CreatedAt < previousEnd);

        var prevContactsCount = await db.Contacts.AsNoTracking()
            .CountAsync(c => c.CreatedAt >= previousStart && c.CreatedAt < previousEnd);
        var prevCampaignsCount = await prevCampaignsQuery.CountAsync();
        var prevTemplatesCount = await db.Templates.AsNoTracking()
            .CountAsync(t => t.CreatedAt >= previousStart && t.CreatedAt < previousEnd);

        var prevCampaignIds = prevCampaignsQuery.Select(c => c.Id);
        var prevMessagesCount = await db.CampaignContacts.AsNoTracking()
            .CountAsync(cc => prevCampaignIds.Contains(cc.CampaignId) && cc.Status != MessageStatus.Pending);

        return new PreviousPeriodCounts(prevContactsCount, prevCampaignsCount, prevTemplatesCount, prevMessagesCount);
    }

    // 7-day sparkline trend for each stat card (independent of the active timeFilter, always daily granularity).
    // Each series is aggregated in SQL (GROUP BY day) instead of pulling every row into memory and counting.
    private async Task<SparklineData> GetSparklinesAsync(DateTime sparklineStart)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var contactsByDay = await db.Contacts.AsNoTracking()
            .Where(c => c.CreatedAt >= sparklineStart)
            .GroupBy(c => c.CreatedAt.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Day, g => g.Count);

        var campaignsByDay = await db.Campaigns.AsNoTracking()
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
        var messagesByDay = await db.CampaignContacts.AsNoTracking()
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
    private async Task<HourlyChartResult> GetHourlyChartAsync(DateTime? currentCutoff)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var hourlyBaseQuery = db.CampaignContacts.AsNoTracking().Where(cc => cc.SentAt != null);
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
            .Select(cc => new { cc.SentAt, cc.Status })
            .ToListAsync();

        // Bucket by IST hour in memory — trivial LINQ-to-Objects, zero SQL-translation risk.
        var hourlyAggregates = dayRows
            .GroupBy(cc => cc.SentAt!.Value.Add(IstOffset).Hour)
            .ToDictionary(g => g.Key, g => new
            {
                Sent = g.Count(),
                Errors = g.Count(cc => cc.Status == MessageStatus.Failed),
                Delivered = g.Count(cc => cc.Status == MessageStatus.Delivered || cc.Status == MessageStatus.Read),
                Read = g.Count(cc => cc.Status == MessageStatus.Read)
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
            var delivered = agg?.Delivered ?? 0;
            var read = agg?.Read ?? 0;

            hourlyChartData.Add(new { name = hourStr, sent, errors });

            double delRate = sent > 0 ? ((double)delivered / sent) * 100 : 0;
            double rdRate = delivered > 0 ? ((double)read / delivered) * 100 : 0;

            deliveryTrend.Add(new { name = hourStr, value = Math.Round(delRate, 2) });
            readTrend.Add(new { name = hourStr, value = Math.Round(rdRate, 2) });
        }

        return new HourlyChartResult(hourlyChartData, deliveryTrend, readTrend);
    }

    // Top-N campaign lists (most recent, and top-by-created-date with delivery/read rates) fetched
    // directly as their own targeted SQL queries — never derived from a fully materialized campaigns table.
    private async Task<TopAndRecentCampaigns> GetTopAndRecentCampaignsAsync(DateTime? currentCutoff)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var queryCampaigns = db.Campaigns.AsNoTracking();
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
                totalRecipients = c.TotalRecipients,
                deliveredCount = c.DeliveredCount
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
                c.TotalRecipients,
                c.DeliveredCount,
                c.ReadCount
            })
            .ToListAsync();

        // Delivery/read rates are percentages derived from the raw counts above — computed here rather
        // than in the SQL projection to keep the query itself trivially translatable.
        var topCampaigns = topCampaignsRaw
            .Select(c => new
            {
                id = c.Id,
                name = c.Name,
                createdAt = c.CreatedAt,
                status = c.Status.ToString(),
                messages = c.TotalRecipients,
                delivered = c.DeliveredCount,
                deliveryRate = c.TotalRecipients > 0 ? Math.Round((double)c.DeliveredCount / c.TotalRecipients * 100, 2) : 0,
                readRate = c.TotalRecipients > 0 ? Math.Round((double)c.ReadCount / c.TotalRecipients * 100, 2) : 0
            })
            .ToList();

        return new TopAndRecentCampaigns(
            recentCampaigns.Cast<object>().ToList(),
            topCampaigns.Cast<object>().ToList());
    }

    // Synthesized Recent Activity feed — merged from existing tables' own timestamps, no dedicated log table.
    private async Task<RecentActivityResult> GetRecentActivityAndBusinessNameAsync()
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

        var recentCampaignActivity = await db.Campaigns.AsNoTracking()
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
