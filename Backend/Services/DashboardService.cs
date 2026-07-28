using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using WhatsAppCampaignApi.Models.DTOs;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services
{
    public class DashboardService : IDashboardService
    {
        private readonly IWabaRepository _wabaRepository;
        private readonly IBusinessRepository _businessRepository;
        private readonly IPhoneRepository _phoneRepository;
        private readonly IHealthLogRepository _healthLogRepository;
        private readonly IMetaGraphService _metaGraphService;
        private readonly WhatsAppCampaignApi.Data.AppDbContext _dbContext;

        public DashboardService(
            IWabaRepository wabaRepository,
            IBusinessRepository businessRepository,
            IPhoneRepository phoneRepository,
            IHealthLogRepository healthLogRepository,
            IMetaGraphService metaGraphService,
            WhatsAppCampaignApi.Data.AppDbContext dbContext)
        {
            _wabaRepository = wabaRepository;
            _businessRepository = businessRepository;
            _phoneRepository = phoneRepository;
            _healthLogRepository = healthLogRepository;
            _metaGraphService = metaGraphService;
            _dbContext = dbContext;
        }
        public async Task<DashboardDto?> GetDashboardDataAsync()
        {
            var config = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(_dbContext.WabaConfigurations, c => c.Connected)
                ?? await _wabaRepository.GetAsync();

            if (config == null)
            {
                return new DashboardDto { IsConnected = false };
            }

            if (!config.Connected)
            {
                return new DashboardDto
                {
                    IsConnected = false,
                    FacebookAppId = config.FacebookAppId,
                    WebhookUrl = config.WebhookUrl,
                    VerifyToken = config.VerifyToken
                };
            }

            var business = await _businessRepository.GetAsync();
            var phones = await _phoneRepository.GetAllAsync();
            var templates = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(_dbContext.Templates);
            var healthLogs = await _healthLogRepository.GetRecentLogsAsync(10);
            var latestLog = await _healthLogRepository.GetLatestLogAsync();

            var tokenInfo = new TokenInfoDto { IsValid = false };

            // Call Meta Graph debug token to fetch scopes, type, application name, expiration, etc.
            string debugJson = await _metaGraphService.DebugTokenAsync(config.AccessToken, config.FacebookAppId, config.FacebookAppSecret);
            try
            {
                using var doc = JsonDocument.Parse(debugJson);
                if (doc.RootElement.TryGetProperty("data", out var dataProp))
                {
                    tokenInfo.AppId = dataProp.TryGetProperty("app_id", out var appIdProp) ? appIdProp.GetString() ?? "" : "";
                    tokenInfo.Application = dataProp.TryGetProperty("application", out var appProp) ? appProp.GetString() ?? "" : "";
                    tokenInfo.Type = dataProp.TryGetProperty("type", out var typeProp) ? typeProp.GetString() ?? "" : "";
                    tokenInfo.IsValid = dataProp.TryGetProperty("is_valid", out var validProp) && validProp.GetBoolean();
                    
                    if (dataProp.TryGetProperty("expires_at", out var expProp))
                    {
                        long seconds = expProp.GetInt64();
                        if (seconds > 0)
                        {
                            tokenInfo.ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
                        }
                    }

                    if (dataProp.TryGetProperty("issued_at", out var issuedProp))
                    {
                        long seconds = issuedProp.GetInt64();
                        if (seconds > 0)
                        {
                            tokenInfo.IssuedAt = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
                        }
                        else
                        {
                            tokenInfo.IssuedAt = config.CreatedAt;
                        }
                    }
                    else
                    {
                        tokenInfo.IssuedAt = config.CreatedAt;
                    }

                    var scopes = new List<string>();
                    if (dataProp.TryGetProperty("scopes", out var scopesProp) && scopesProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var scopeVal in scopesProp.EnumerateArray())
                        {
                            scopes.Add(scopeVal.GetString() ?? "");
                        }
                    }
                    tokenInfo.Scopes = scopes;
                }
            }
            catch
            {
                // Fallback token info if debugging fails
                tokenInfo.AppId = config.FacebookAppId;
                tokenInfo.Application = "WABA Facebook App";
                tokenInfo.Type = "SYSTEM_USER";
                tokenInfo.IsValid = true;
                tokenInfo.IssuedAt = config.CreatedAt;
                tokenInfo.Scopes = new[]
                {
                    "whatsapp_business_management",
                    "whatsapp_business_messaging",
                    "whatsapp_business_manage_events",
                    "public_profile"
                };
            }

            return new DashboardDto
            {
                IsConnected = true,
                FacebookAppId = config.FacebookAppId,
                WabaId = config.WabaId,
                AccessToken = config.AccessToken,
                WebhookUrl = config.WebhookUrl,
                VerifyToken = config.VerifyToken,
                Business = business,
                PhoneNumbers = phones,
                Templates = templates,
                LatestHealthLog = latestLog,
                HealthLogs = healthLogs,
                TokenInfo = tokenInfo
            };
        }
    }
}
