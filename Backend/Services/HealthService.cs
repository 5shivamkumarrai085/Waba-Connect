using System;
using System.Linq;
using System.Threading.Tasks;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Models.Entities;

namespace WhatsAppCampaignApi.Services
{
    public class HealthService : IHealthService
    {
        private readonly IWabaRepository _wabaRepository;
        private readonly IHealthLogRepository _healthLogRepository;
        private readonly IMetaGraphService _metaGraphService;

        public HealthService(
            IWabaRepository wabaRepository,
            IHealthLogRepository healthLogRepository,
            IMetaGraphService metaGraphService)
        {
            _wabaRepository = wabaRepository;
            _healthLogRepository = healthLogRepository;
            _metaGraphService = metaGraphService;
        }

        public async Task<HealthLog> RunHealthCheckAsync()
        {
            var config = await _wabaRepository.GetAsync();
            if (config == null || !config.Connected)
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

            bool appValid = false;
            bool businessValid = false;
            bool phoneValid = false;

            try
            {
                // 1. App Validation
                appValid = await _metaGraphService.ValidateAppAsync(config.FacebookAppId, config.FacebookAppSecret);

                // 2. Business Check
                var biz = await _metaGraphService.GetBusinessDetailsAsync(config.WabaId, config.AccessToken);
                businessValid = !string.IsNullOrEmpty(biz?.BusinessId);

                // 3. Phone Check
                var phones = await _metaGraphService.GetPhoneNumbersAsync(config.WabaId, config.AccessToken);
                phoneValid = phones != null && phones.Any();
            }
            catch
            {
                // Errors handled inside try/catch to maintain validation flow
            }

            string overallStatus;
            string description;

            if (appValid && businessValid && phoneValid)
            {
                overallStatus = "AVAILABLE";
                description = "All systems operational. App ID, WABA configurations, and phone lines checked and validated successfully.";
            }
            else if (!appValid && !businessValid && !phoneValid)
            {
                overallStatus = "UNAVAILABLE";
                description = "Critical outage. App verification, WABA data retrieval, and phone synchronization failed. Verify API credentials.";
            }
            else
            {
                overallStatus = "PARTIAL";
                var issues = new System.Collections.Generic.List<string>();
                if (!appValid) issues.Add("App ID Validation Failed");
                if (!businessValid) issues.Add("WABA Account Retrieval Failed");
                if (!phoneValid) issues.Add("Phone Numbers Synchronization Failed");
                description = $"Degraded performance. Issues detected: {string.Join(", ", issues)}.";
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
    }
}
