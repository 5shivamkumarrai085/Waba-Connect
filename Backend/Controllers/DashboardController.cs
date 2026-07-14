using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Common;

namespace WhatsAppCampaignApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public DashboardController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary([FromQuery] string timeFilter = "all")
    {
        var queryContacts = _dbContext.Contacts.AsQueryable();
        var queryCampaigns = _dbContext.Campaigns.AsQueryable();
        var queryContactsWithSentAt = _dbContext.CampaignContacts.Where(cc => cc.SentAt != null).AsQueryable();

        if (timeFilter != "all")
        {
            var cutoff = DateTime.UtcNow;
            if (timeFilter == "today")
            {
                cutoff = DateTime.UtcNow.Date;
            }
            else if (timeFilter == "week")
            {
                cutoff = DateTime.UtcNow.AddDays(-7);
            }
            else if (timeFilter == "month")
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

        var data = new
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
        };

        return Ok(new ApiResponse<object> { Success = true, Data = data });
    }
}
