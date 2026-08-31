using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Models.Entities;

namespace WhatsAppCampaignApi.Services
{
    public class HealthService : IHealthService
    {
        private readonly IWabaRepository _wabaRepository;
        private readonly IHealthLogRepository _healthLogRepository;
        private readonly IMetaGraphService _metaGraphService;
        private readonly IPhoneRepository _phoneRepository;
        private readonly AppDbContext _dbContext;

        public HealthService(
            IWabaRepository wabaRepository,
            IHealthLogRepository healthLogRepository,
            IMetaGraphService metaGraphService,
            IPhoneRepository phoneRepository,
            AppDbContext dbContext)
        {
            _wabaRepository = wabaRepository;
            _healthLogRepository = healthLogRepository;
            _metaGraphService = metaGraphService;
            _phoneRepository = phoneRepository;
            _dbContext = dbContext;
        }

        public async Task<HealthLog> RunHealthCheckAsync()
        {
            // Deleted connections are not health-checked: reporting an outage for an integration
            // the operator already removed is noise they cannot act on.
            var configs = await _dbContext.WabaConfigurations
                .ForLiveConnections()
                .Where(c => c.ConnectionId != null)
                .ToListAsync();
            
            if (configs == null || !configs.Any())
            {
                var notConnectedLog = new HealthLog
                {
                    Status = "UNAVAILABLE",
                    CheckedAt = DateTime.UtcNow,
                    Description = "No active WhatsApp Business Account configuration found."
                };
                await _healthLogRepository.AddLogAsync(notConnectedLog);
                return notConnectedLog;
            }

            bool allAppValid = true;
            bool allBusinessValid = true;
            bool allPhoneValid = true;
            bool allWebhookPublic = true;
            var allIssues = new System.Collections.Generic.List<string>();

            foreach (var config in configs)
            {
                bool appValid = false;
                bool businessValid = false;
                bool phoneValid = false;
                bool webhookPublic = !IsLocalWebhookUrl(config.WebhookUrl);

                try
                {
                    // 1. App Validation
                    appValid = await _metaGraphService.ValidateAppAsync(config.FacebookAppId, config.FacebookAppSecret);

                    // 2. Business Check
                    var biz = await _metaGraphService.GetBusinessDetailsAsync(config.WabaId, config.AccessToken);
                    businessValid = !string.IsNullOrEmpty(biz?.BusinessId);

                    // 3. Phone Check
                    var phones = await _metaGraphService.GetPhoneNumbersAsync(config.WabaId, config.AccessToken);
                    if (phones != null && phones.Any())
                    {
                        phoneValid = true;
                        foreach (var p in phones)
                        {
                            if (config.ConnectionId.HasValue)
                            {
                                p.ConnectionId = config.ConnectionId;
                            }
                        }
                        await _phoneRepository.SaveRangeAsync(phones);
                    }
                }
                catch
                {
                    // Errors handled inside try/catch to maintain validation flow
                }

                if (!appValid) 
                { 
                    allAppValid = false; 
                    if (!allIssues.Contains("App ID Validation Failed")) allIssues.Add("App ID Validation Failed"); 
                }
                if (!businessValid) 
                { 
                    allBusinessValid = false; 
                    if (!allIssues.Contains("WABA Account Retrieval Failed")) allIssues.Add("WABA Account Retrieval Failed"); 
                }
                if (!phoneValid) 
                { 
                    allPhoneValid = false; 
                    if (!allIssues.Contains("Phone Numbers Synchronization Failed")) allIssues.Add("Phone Numbers Synchronization Failed"); 
                }
                if (!webhookPublic) 
                { 
                    allWebhookPublic = false; 
                    if (!allIssues.Contains("Webhook URL is localhost, so Meta cannot send delivery, read, inbound, or failed-message callbacks")) allIssues.Add("Webhook URL is localhost, so Meta cannot send delivery, read, inbound, or failed-message callbacks"); 
                }
            }

            string overallStatus;
            string description;

            if (allAppValid && allBusinessValid && allPhoneValid && allWebhookPublic)
            {
                overallStatus = "AVAILABLE";
                description = "All systems operational. App ID, WABA configurations, phone lines, and public webhook callback checked successfully.";
            }
            else if (!allAppValid && !allBusinessValid && !allPhoneValid)
            {
                overallStatus = "UNAVAILABLE";
                description = "Critical outage. App verification, WABA data retrieval, and phone synchronization failed. Verify API credentials.";
            }
            else
            {
                overallStatus = "PARTIAL";
                description = $"Degraded performance. Issues detected: {string.Join(", ", allIssues)}.";
            }

            var log = new HealthLog
            {
                Status = overallStatus,
                CheckedAt = DateTime.UtcNow,
                Description = description
            };

            await _healthLogRepository.AddLogAsync(log);
            return log;
        }

        private static bool IsLocalWebhookUrl(string webhookUrl)
        {
            if (!Uri.TryCreate(webhookUrl, UriKind.Absolute, out var uri)) return false;

            return uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("::1", StringComparison.OrdinalIgnoreCase);
        }
    }
}
