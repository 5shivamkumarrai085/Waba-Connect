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
    private readonly AppDbContext _dbContext;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _configuration;

    private static readonly string[] CacheKeys =
    {
        "Dashboard_Summary_today",
        "Dashboard_Summary_week",
        "Dashboard_Summary_month",
        "Dashboard_Summary_all"
    };

    public DashboardCacheService(AppDbContext dbContext, IMemoryCache cache, IConfiguration configuration)
    {
        _dbContext = dbContext;
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
                currentCutoff = now.Date;
                previousStart = now.Date.AddDays(-1);
                previousEnd = now.Date;
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

            var queryContacts = _dbContext.Contacts.AsNoTracking();
            var queryCampaigns = _dbContext.Campaigns.AsNoTracking();
            var queryTemplates = _dbContext.Templates.AsNoTracking();

            if (currentCutoff.HasValue)
            {
                queryContacts = queryContacts.Where(c => c.CreatedAt >= currentCutoff.Value);
                queryCampaigns = queryCampaigns.Where(c => c.CreatedAt >= currentCutoff.Value);
                queryTemplates = queryTemplates.Where(t => t.CreatedAt >= currentCutoff.Value);
            }

            var totalContacts = await queryContacts.CountAsync();
            var contactsActive = await queryContacts.CountAsync(c => c.IsActive);

            var campaigns = await queryCampaigns.ToListAsync();
            var totalCampaigns = campaigns.Count;
            var campaignsActive = campaigns.Count(c => c.Status == CampaignStatus.Sending || c.Status == CampaignStatus.Scheduled);

            var templates = await queryTemplates.ToListAsync();
            var templatesTotal = templates.Count;
            var templatesApproved = templates.Count(t => t.Status == TemplateStatus.Approved);

            // Message aggregates are scoped by the *campaign's* CreatedAt (via campaign id), not CampaignContact.SentAt —
            // some historical rows have Status set (Sent/Delivered/...) without SentAt ever being populated, so a
            // SentAt-based filter would silently drop real messages. Status is the reliable source of truth here.
            // Aggregated in SQL (GROUP BY status) instead of pulling every CampaignContact row into memory.
            var campaignIdsInPeriod = campaigns.Select(c => c.Id).ToHashSet();
            var statusCounts = campaignIdsInPeriod.Count > 0
                ? await _dbContext.CampaignContacts.AsNoTracking()
                    .Where(cc => campaignIdsInPeriod.Contains(cc.CampaignId))
                    .GroupBy(cc => cc.Status)
                    .Select(g => new { Status = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(g => g.Status, g => g.Count)
                : new Dictionary<MessageStatus, int>();

            int CountByStatus(MessageStatus status) => statusCounts.TryGetValue(status, out var c) ? c : 0;

            var messagesSent = statusCounts.Where(kv => kv.Key != MessageStatus.Pending).Sum(kv => kv.Value);
            var messagesDelivered = CountByStatus(MessageStatus.Delivered) + CountByStatus(MessageStatus.Read);
            var messagesRead = CountByStatus(MessageStatus.Read);
            var messagesFailed = CountByStatus(MessageStatus.Failed);
            // In-flight: dispatched but not yet resolved to Delivered/Read/Failed. delivered+failed+pending == messagesSent.
            var messagesPending = CountByStatus(MessageStatus.Sent);

            // Previous-period comparison (null for "all", where there is no meaningful prior window).
            object? previousPeriod = null;
            if (previousStart.HasValue && previousEnd.HasValue)
            {
                var prevContactsCount = await _dbContext.Contacts.AsNoTracking()
                    .CountAsync(c => c.CreatedAt >= previousStart.Value && c.CreatedAt < previousEnd.Value);
                var prevCampaignsCount = await _dbContext.Campaigns.AsNoTracking()
                    .CountAsync(c => c.CreatedAt >= previousStart.Value && c.CreatedAt < previousEnd.Value);
                var prevTemplatesCount = await _dbContext.Templates.AsNoTracking()
                    .CountAsync(t => t.CreatedAt >= previousStart.Value && t.CreatedAt < previousEnd.Value);
                var prevCampaignIds = await _dbContext.Campaigns.AsNoTracking()
                    .Where(c => c.CreatedAt >= previousStart.Value && c.CreatedAt < previousEnd.Value)
                    .Select(c => c.Id).ToListAsync();
                var prevMessagesCount = prevCampaignIds.Count > 0
                    ? await _dbContext.CampaignContacts.AsNoTracking()
                        .CountAsync(cc => prevCampaignIds.Contains(cc.CampaignId) && cc.Status != MessageStatus.Pending)
                    : 0;

                static double PercentChange(int current, int previous)
                {
                    if (previous == 0) return current > 0 ? 100.0 : 0.0;
                    return Math.Round(((double)(current - previous) / previous) * 100, 1);
                }

                previousPeriod = new
                {
                    messagesChangePercent = PercentChange(messagesSent, prevMessagesCount),
                    contactsChangePercent = PercentChange(totalContacts, prevContactsCount),
                    campaignsChangePercent = PercentChange(totalCampaigns, prevCampaignsCount),
                    templatesChangePercent = PercentChange(templatesTotal, prevTemplatesCount)
                };
            }

            // 7-day sparkline trend for each stat card (independent of the active timeFilter, always daily granularity).
            // Each series is aggregated in SQL (GROUP BY day) instead of pulling every row into memory and counting.
            var sparklineStart = now.Date.AddDays(-6);

            var contactsByDay = await _dbContext.Contacts.AsNoTracking()
                .Where(c => c.CreatedAt >= sparklineStart)
                .GroupBy(c => c.CreatedAt.Date)
                .Select(g => new { Day = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Day, g => g.Count);

            var campaignsByDay = await _dbContext.Campaigns.AsNoTracking()
                .Where(c => c.CreatedAt >= sparklineStart)
                .GroupBy(c => c.CreatedAt.Date)
                .Select(g => new { Day = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Day, g => g.Count);

            var templatesByDay = await _dbContext.Templates.AsNoTracking()
                .Where(t => t.CreatedAt >= sparklineStart)
                .GroupBy(t => t.CreatedAt.Date)
                .Select(g => new { Day = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Day, g => g.Count);

            // A message's "day" for this trend is its owning campaign's CreatedAt date (matching the semantics above).
            var messagesByDay = await _dbContext.CampaignContacts.AsNoTracking()
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

            var recentCampaigns = campaigns
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
                .ToList();

            // Hourly line-chart data needs each message's own SentAt timestamp for hour-of-day bucketing
            // (campaign CreatedAt can't tell us what hour a specific message went out), scoped to this period.
            // Aggregated in SQL (GROUP BY hour, with conditional counts) instead of pulling every row into memory.
            var hourlyBaseQuery = _dbContext.CampaignContacts.AsNoTracking().Where(cc => cc.SentAt != null);
            if (currentCutoff.HasValue)
            {
                hourlyBaseQuery = hourlyBaseQuery.Where(cc => cc.SentAt >= currentCutoff.Value);
            }

            var latestDate = await hourlyBaseQuery
                .OrderByDescending(cc => cc.SentAt)
                .Select(cc => cc.SentAt!.Value.Date)
                .FirstOrDefaultAsync();
            if (latestDate == default)
            {
                latestDate = DateTime.UtcNow.Date;
            }

            var hourlyAggregates = await hourlyBaseQuery
                .Where(cc => cc.SentAt!.Value.Date == latestDate)
                .GroupBy(cc => cc.SentAt!.Value.Hour)
                .Select(g => new
                {
                    Hour = g.Key,
                    Sent = g.Count(),
                    Errors = g.Count(cc => cc.Status == MessageStatus.Failed),
                    Delivered = g.Count(cc => cc.Status == MessageStatus.Delivered || cc.Status == MessageStatus.Read),
                    Read = g.Count(cc => cc.Status == MessageStatus.Read)
                })
                .ToDictionaryAsync(g => g.Hour);

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

            // Unified Top Campaigns list — most recent 5, each carrying both delivery & read rate.
            var topCampaigns = campaigns
                .OrderByDescending(c => c.CreatedAt)
                .Take(5)
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

            // Overall Rates
            double overallDeliveryRate = messagesSent > 0 ? ((double)messagesDelivered / messagesSent) * 100 : 0;
            double overallReadRate = messagesDelivered > 0 ? ((double)messagesRead / messagesDelivered) * 100 : 0;

            // Delivery / Read breakdown for the donut widget (both always returned; frontend toggles client-side).
            object deliveryBreakdown = new
            {
                delivered = messagesDelivered,
                failed = messagesFailed,
                pending = messagesPending,
                deliveredPercent = messagesSent > 0 ? Math.Round((double)messagesDelivered / messagesSent * 100, 1) : 0,
                failedPercent = messagesSent > 0 ? Math.Round((double)messagesFailed / messagesSent * 100, 1) : 0,
                pendingPercent = messagesSent > 0 ? Math.Round((double)messagesPending / messagesSent * 100, 1) : 0
            };

            var messagesUnread = Math.Max(messagesDelivered - messagesRead, 0);
            var messagesNotDelivered = messagesFailed + messagesPending;
            object readBreakdown = new
            {
                read = messagesRead,
                unread = messagesUnread,
                notDelivered = messagesNotDelivered,
                readPercent = messagesSent > 0 ? Math.Round((double)messagesRead / messagesSent * 100, 1) : 0,
                unreadPercent = messagesSent > 0 ? Math.Round((double)messagesUnread / messagesSent * 100, 1) : 0,
                notDeliveredPercent = messagesSent > 0 ? Math.Round((double)messagesNotDelivered / messagesSent * 100, 1) : 0
            };

            // Synthesized Recent Activity feed — merged from existing tables' own timestamps, no dedicated log table.
            var activityItems = new List<(DateTime Timestamp, object Item)>();

            var recentContacts = await _dbContext.Contacts.AsNoTracking()
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

            var recentCampaignActivity = await _dbContext.Campaigns.AsNoTracking()
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

            var recentTemplateActivity = await _dbContext.Templates.AsNoTracking()
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

            var recentBotFlowActivity = await _dbContext.BotFlows.AsNoTracking()
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

            var businessName = await _dbContext.Businesses.AsNoTracking()
                .OrderByDescending(b => b.Id)
                .Select(b => b.BusinessName)
                .FirstOrDefaultAsync();

            return new
            {
                totalContacts,
                contactsActive,
                totalCampaigns,
                campaignsActive,
                templatesTotal,
                templatesApproved,
                messagesSent,
                messagesDelivered,
                messagesRead,
                messagesFailed,
                messagesPending,
                previousPeriod,
                messagesSparkline,
                contactsSparkline,
                campaignsSparkline,
                templatesSparkline,
                recentCampaigns,
                hourlyChartData,
                deliveryTrend,
                readTrend,
                topCampaigns,
                deliveryBreakdown,
                readBreakdown,
                recentActivity,
                businessName,
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
}
