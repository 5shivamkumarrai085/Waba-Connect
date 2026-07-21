using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using WhatsAppCampaignApi.Data;
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

            var queryContacts = _dbContext.Contacts.AsNoTracking();
            var queryCampaigns = _dbContext.Campaigns.AsNoTracking();
            var queryContactsWithSentAt = _dbContext.CampaignContacts.AsNoTracking().Where(cc => cc.SentAt != null);

            if (normalizedFilter != "all")
            {
                var cutoff = DateTime.UtcNow;
                if (normalizedFilter == "today")
                {
                    cutoff = DateTime.UtcNow.Date;
                }
                else if (normalizedFilter == "week")
                {
                    cutoff = DateTime.UtcNow.AddDays(-7);
                }
                else if (normalizedFilter == "month")
                {
                    cutoff = DateTime.UtcNow.AddDays(-30);
                }

                queryContacts = queryContacts.Where(c => c.CreatedAt >= cutoff);
                queryCampaigns = queryCampaigns.Where(c => c.CreatedAt >= cutoff);
                queryContactsWithSentAt = queryContactsWithSentAt.Where(cc => cc.SentAt >= cutoff);
            }

            var totalContacts = await queryContacts.CountAsync();
            var totalCampaigns = await queryCampaigns.CountAsync();

            var campaigns = await queryCampaigns.ToListAsync();
            
            var messagesSent = campaigns.Sum(c => c.TotalRecipients);
            var messagesDelivered = campaigns.Sum(c => c.DeliveredCount);
            var messagesRead = campaigns.Sum(c => c.ReadCount);
            var messagesFailed = campaigns.Sum(c => c.FailedCount);

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

            var contactsWithSentAt = await queryContactsWithSentAt.ToListAsync();

            var latestDate = contactsWithSentAt.Any() 
                ? contactsWithSentAt.Max(cc => cc.SentAt!.Value.Date) 
                : DateTime.UtcNow.Date;

            var todayContacts = contactsWithSentAt
                .Where(cc => cc.SentAt!.Value.Date == latestDate)
                .ToList();

            var hourlyChartData = new List<object>();
            var deliveryTrend = new List<object>();
            var readTrend = new List<object>();

            for (int i = 0; i < 24; i++)
            {
                var hourStr = $"{i:D2}:00";
                var hourContacts = todayContacts.Where(cc => cc.SentAt!.Value.Hour == i).ToList();
                
                var sent = hourContacts.Count;
                var errors = hourContacts.Count(cc => cc.Status == Models.Enums.MessageStatus.Failed);
                var delivered = hourContacts.Count(cc => cc.Status == Models.Enums.MessageStatus.Delivered || cc.Status == Models.Enums.MessageStatus.Read);
                var read = hourContacts.Count(cc => cc.Status == Models.Enums.MessageStatus.Read);

                hourlyChartData.Add(new { name = hourStr, sent, errors });

                double delRate = sent > 0 ? ((double)delivered / sent) * 100 : 0;
                double rdRate = delivered > 0 ? ((double)read / delivered) * 100 : 0;

                deliveryTrend.Add(new { name = hourStr, value = Math.Round(delRate, 2) });
                readTrend.Add(new { name = hourStr, value = Math.Round(rdRate, 2) });
            }

            // Top Campaigns
            var topReadRateCampaigns = campaigns
                .Where(c => c.TotalRecipients > 0)
                .Select(c => new
                {
                    id = c.Id,
                    campaign = c.Name,
                    messages = c.TotalRecipients,
                    primaryRate = $"{((double)c.ReadCount / c.TotalRecipients * 100):F2}%",
                    secondaryRate = $"{((double)c.DeliveredCount / c.TotalRecipients * 100):F2}%",
                    primaryPercent = Math.Round((double)c.ReadCount / c.TotalRecipients * 100, 2),
                    secondaryPercent = Math.Round((double)c.DeliveredCount / c.TotalRecipients * 100, 2)
                })
                .OrderByDescending(x => x.primaryPercent)
                .Take(5)
                .ToList();

            var topDeliveryRateCampaigns = campaigns
                .Where(c => c.TotalRecipients > 0)
                .Select(c => new
                {
                    id = c.Id,
                    campaign = c.Name,
                    messages = c.TotalRecipients,
                    primaryRate = $"{((double)c.DeliveredCount / c.TotalRecipients * 100):F2}%",
                    secondaryRate = $"{((double)c.ReadCount / c.TotalRecipients * 100):F2}%",
                    primaryPercent = Math.Round((double)c.DeliveredCount / c.TotalRecipients * 100, 2),
                    secondaryPercent = Math.Round((double)c.ReadCount / c.TotalRecipients * 100, 2)
                })
                .OrderByDescending(x => x.primaryPercent)
                .Take(5)
                .ToList();

            // Overall Rates
            double overallDeliveryRate = messagesSent > 0 ? ((double)messagesDelivered / messagesSent) * 100 : 0;
            double overallReadRate = messagesDelivered > 0 ? ((double)messagesRead / messagesDelivered) * 100 : 0;

            return new
            {
                totalContacts,
                totalCampaigns,
                messagesSent,
                messagesDelivered,
                messagesRead,
                messagesFailed,
                recentCampaigns,
                hourlyChartData,
                deliveryTrend,
                readTrend,
                topReadRateCampaigns,
                topDeliveryRateCampaigns,
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
