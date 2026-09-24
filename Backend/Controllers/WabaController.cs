using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Models.Entities;

using WhatsAppCampaignApi.Helpers;

namespace WhatsAppCampaignApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class WabaController : ControllerBase
    {
        private readonly IWabaRepository _wabaRepository;
        private readonly IBusinessRepository _businessRepository;
        private readonly IPhoneRepository _phoneRepository;
        private readonly IHealthLogRepository _healthLogRepository;
        private readonly IMetaGraphService _metaGraphService;
        private readonly IWebhookService _webhookService;
        private readonly ITemplateService _templateService;
        private readonly IDashboardService _dashboardService;
        private readonly IHealthService _healthService;
        private readonly IConnectionService _connectionService;

        private readonly AppDbContext _dbContext;
        private readonly IMemoryCache _cache;
        private readonly IAuditService _auditService;

        public WabaController(
            IWabaRepository wabaRepository,
            IBusinessRepository businessRepository,
            IPhoneRepository phoneRepository,
            IHealthLogRepository healthLogRepository,
            IMetaGraphService metaGraphService,
            IWebhookService webhookService,
            ITemplateService templateService,
            IDashboardService dashboardService,
            IHealthService healthService,
            IConnectionService connectionService,
            AppDbContext dbContext,
            IMemoryCache cache,
            IAuditService auditService)
        {
            _auditService = auditService;
            _cache = cache;
            _wabaRepository = wabaRepository;
            _businessRepository = businessRepository;
            _phoneRepository = phoneRepository;
            _healthLogRepository = healthLogRepository;
            _metaGraphService = metaGraphService;
            _webhookService = webhookService;
            _templateService = templateService;
            _dashboardService = dashboardService;
            _healthService = healthService;
            _connectionService = connectionService;
            _dbContext = dbContext;
        }

        [HttpPost("connect-app")]
        [RequiresPermission("ConnectAccount.Connect")]
        public async Task<IActionResult> ConnectApp([FromBody] ConnectAppRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            if (string.IsNullOrWhiteSpace(request.FacebookAppId) || string.IsNullOrWhiteSpace(request.FacebookAppSecret))
            {
                return BadRequest(new { message = "Facebook App ID and App Secret are required." });
            }

            // Check for duplicate Facebook App ID across connected configurations.
            //
            // The connection must still be active as well as connected. Without that clause a
            // deleted connection went on reserving its App ID forever: the row stayed Connected,
            // but the connection itself no longer appears anywhere in the UI — so this endpoint
            // refused the ID and told the operator to "disconnect it first", pointing at something
            // they could not see, let alone disconnect. Deleting now releases the configuration
            // too (ConnectionService.SoftDeleteAsync), and this guard no longer counts the dead.
            var existingAppConfig = await _dbContext.WabaConfigurations
                .Include(c => c.Connection)
                .ForLiveConnections()
                .FirstOrDefaultAsync(c => c.FacebookAppId == request.FacebookAppId.Trim() &&
                    (!request.ConnectionId.HasValue || c.ConnectionId != request.ConnectionId.Value));
            if (existingAppConfig != null)
            {
                var connName = existingAppConfig.Connection?.Name ?? $"Connection {existingAppConfig.ConnectionId}";
                return BadRequest(new { message = $"This Facebook App ID is already in use by connection '{connName}'. Please disconnect it first or use a different App ID." });
            }

            // Ensure we have a Connection for this WABA configuration
            ConnectionResponse conn;
            if (request.ConnectionId.HasValue)
            {
                var existing = await _connectionService.GetByIdAsync(request.ConnectionId.Value);
                if (existing == null) return BadRequest(new { message = $"Connection {request.ConnectionId.Value} not found." });
                conn = existing;
            }
            else
            {
                var allConn = await _connectionService.GetAllAsync();
                string nextName = string.IsNullOrWhiteSpace(request.ConnectionName)
                    ? $"Connection {allConn.Count + 1}"
                    : request.ConnectionName.Trim();

                conn = await _connectionService.CreateAsync(new CreateConnectionRequest
                {
                    Name = nextName,
                    Description = $"WhatsApp Business Connection for App {request.FacebookAppId}"
                });
            }

            // Fetch existing WabaConfiguration for this ConnectionId if available
            var allConfigs = await _dbContext.WabaConfigurations.ToListAsync();
            var config = allConfigs.FirstOrDefault(c => c.ConnectionId == conn.Id);

            string verifyToken = config?.VerifyToken ?? string.Empty;
            if (string.IsNullOrWhiteSpace(verifyToken))
            {
                verifyToken = "waba_verify_token_" + Guid.NewGuid().ToString("N").Substring(0, 16);
            }

            string webhookUrl = $"{Request.Scheme}://{Request.Host}/api/webhook/whatsapp";

            if (config == null)
            {
                config = new WabaConfiguration
                {
                    FacebookAppId = request.FacebookAppId.Trim(),
                    FacebookAppSecret = request.FacebookAppSecret.Trim(),
                    VerifyToken = verifyToken,
                    WebhookUrl = webhookUrl,
                    ConnectionId = conn.Id,
                    Connected = false
                };
            }
            else
            {
                config.FacebookAppId = request.FacebookAppId.Trim();
                config.FacebookAppSecret = request.FacebookAppSecret.Trim();
                config.VerifyToken = verifyToken;
                config.WebhookUrl = webhookUrl;
                config.UpdatedAt = DateTime.UtcNow;
            }

            await _wabaRepository.AddOrUpdateAsync(config);

            return Ok(new
            {
                message = "Facebook App connected successfully.",
                connectionId = conn.Id,
                connectionName = conn.Name,
                webhookUrl = webhookUrl,
                verifyToken = verifyToken
            });
        }

        [HttpPost("configure")]
        [RequiresPermission("ConnectAccount.Connect")]
        public async Task<IActionResult> Configure([FromBody] ConfigureWabaRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            // Check for duplicate WABA ID across live connections.
            //
            // A WABA may only be attached to one connection at a time — two would mean two
            // routes for the same inbound traffic — so this guard is correct in principle. What
            // it was missing is that a *deleted* connection is not holding anything: it kept
            // reserving its WABA ID with no way for the operator to release it, because the
            // connection named in the error no longer exists in the UI.
            var duplicateConfig = await _dbContext.WabaConfigurations
                .Include(c => c.Connection)
                .ForLiveConnections()
                .FirstOrDefaultAsync(c => c.WabaId == request.WabaId &&
                    (!request.ConnectionId.HasValue || c.ConnectionId != request.ConnectionId.Value));
            if (duplicateConfig != null)
            {
                var connName = duplicateConfig.Connection?.Name ?? $"Connection {duplicateConfig.ConnectionId}";
                return BadRequest(new { message = $"This WhatsApp Business Account (WABA ID: {request.WabaId}) is already configured on connection '{connName}'. Please disconnect it first or use a different WABA ID." });
            }

            WabaConfiguration? config = null;
            if (request.ConnectionId.HasValue && request.ConnectionId.Value > 0)
            {
                var allConfigs = await _dbContext.WabaConfigurations.ToListAsync();
                config = allConfigs.FirstOrDefault(c => c.ConnectionId == request.ConnectionId.Value);

                if (config == null)
                {
                    config = new WabaConfiguration
                    {
                        ConnectionId = request.ConnectionId.Value,
                        FacebookAppId = string.Empty,
                        FacebookAppSecret = string.Empty,
                        VerifyToken = "waba_verify_token_" + Guid.NewGuid().ToString("N").Substring(0, 16),
                        WebhookUrl = $"{Request.Scheme}://{Request.Host}/api/webhook/whatsapp"
                    };
                }
            }
            else
            {
                config = await _wabaRepository.GetAsync();
            }

            if (config == null)
            {
                return BadRequest(new { message = "Please connect a Facebook App first (Step 1)." });
            }

            // Fetch business info to validate access token and WABA ID
            Business biz;
            try
            {
                biz = await _metaGraphService.GetBusinessDetailsAsync(request.WabaId, request.AccessToken);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = $"Configuration failed: Unable to fetch WABA details. {ex.Message}" });
            }

            // Save details to database
            config.WabaId = request.WabaId;
            config.AccessToken = request.AccessToken;
            config.Connected = true;
            await _wabaRepository.AddOrUpdateAsync(config);

            // Fetch and save details
            await _businessRepository.SaveAsync(biz);

            // Fetch phone numbers
            var phones = await _metaGraphService.GetPhoneNumbersAsync(request.WabaId, request.AccessToken);
            if (phones != null && phones.Any())
            {
                foreach (var p in phones)
                {
                    p.ConnectionId = config.ConnectionId;
                }
                await _phoneRepository.SaveRangeAsync(phones);
            }

            // CRITICAL: Subscribe the Meta App to receive webhooks for this WABA.
            // Without this call, Meta will NOT deliver incoming messages to the webhook URL.
            var subscribed = await _metaGraphService.SubscribeAppToWabaAsync(request.WabaId, request.AccessToken);
            if (!subscribed)
            {
                // Log warning but don't fail — the user can subscribe manually via the subscribe-webhooks endpoint
                return Ok(new { 
                    message = "WhatsApp Business Account configured, but webhook subscription failed. Please call POST /api/Waba/subscribe-webhooks to retry.",
                    webhookSubscribed = false 
                });
            }

            // Fetch message templates
            await _templateService.SyncFromWhatsAppAsync();

            // Run initial health status check
            await _healthService.RunHealthCheckAsync();

            // Connecting a WhatsApp Business Account is a configuration change with real
            // consequences — it decides which number the product sends from. It was unaudited.
            // The access token is never named here; the change capture redacts it by field name
            // regardless.
            await _auditService.LogAsync(
                "WabaConfiguration.Configured",
                "Settings",
                $"Configured the WhatsApp Business Account for connection #{request.ConnectionId}.",
                entityType: "WabaConfiguration",
                entityId: request.ConnectionId.ToString());

            return Ok(new { message = "WhatsApp Business Account configured successfully.", webhookSubscribed = true });
        }

        [HttpGet("dashboard")]
        [RequiresPermission("ConnectAccount.View")]
        public async Task<IActionResult> GetDashboard([FromQuery] int? connectionId = null)
        {
            if (connectionId.HasValue)
            {
                var conn = await _connectionService.GetByIdAsync(connectionId.Value);
                if (conn != null)
                {
                    // Filtered in the database, not in memory. These used to load every
                    // configuration and every phone number in the system to pick one of each.
                    var config = await _dbContext.WabaConfigurations
                        .FirstOrDefaultAsync(c => c.ConnectionId == connectionId.Value);

                    var allPhones = await _phoneRepository.GetAllAsync();
                    var connPhones = allPhones.Where(p => p.ConnectionId == connectionId.Value).ToList();

                    // Use config.Connected as the source of truth (not conn.IsConnected which may be stale)
                    bool actuallyConnected = config?.Connected ?? false;

                    if (!actuallyConnected)
                    {
                        return Ok(new
                        {
                            connectionId = conn.Id,
                            connectionName = conn.Name,
                            isConnected = false,
                            facebookAppId = config?.FacebookAppId ?? string.Empty,
                            facebookAppSecret = !string.IsNullOrEmpty(config?.FacebookAppSecret) ? "••••••••" : string.Empty,
                            wabaId = string.Empty,
                            accessToken = string.Empty,
                            webhookUrl = config?.WebhookUrl ?? string.Empty,
                            verifyToken = config?.VerifyToken ?? string.Empty,
                            phoneNumbers = Array.Empty<object>(),
                            tokenInfo = (object?)null,
                            business = (object?)null
                        });
                    }

                    var todayUtc = DateTime.UtcNow.Date;
                    int sentToday = await _dbContext.ChatMessages
                        .CountAsync(m => m.ConnectionId == connectionId.Value && m.Direction == Models.Enums.ChatMessageDirection.Outgoing && (m.IsTemplate || m.CampaignContactId != null) && m.CreatedAt >= todayUtc);

                    // The webhook URL is read back from Meta, which costs up to two sequential
                    // Graph calls — on a GET that three separate pages hit on mount. It changes
                    // only when someone reconfigures the app, so it is cached and the stored
                    // value serves every request in between. On a cache hit this also skips the
                    // database write that used to happen on a read.
                    string finalWebhookUrl = config?.WebhookUrl ?? string.Empty;
                    if (config != null && !string.IsNullOrEmpty(config.FacebookAppId) && !string.IsNullOrEmpty(config.FacebookAppSecret))
                    {
                        var cacheKey = $"Waba_WebhookUrl_{connectionId.Value}";
                        if (!_cache.TryGetValue(cacheKey, out string? metaWebhookUrl))
                        {
                            metaWebhookUrl = await _metaGraphService.FetchWebhookUrlFromMetaAsync(
                                config.FacebookAppId, config.FacebookAppSecret, config.WabaId, config.AccessToken);

                            // Negative results are cached too, for less time: a misconfigured app
                            // would otherwise re-pay both Graph calls on every single request.
                            _cache.Set(cacheKey, metaWebhookUrl,
                                string.IsNullOrWhiteSpace(metaWebhookUrl)
                                    ? TimeSpan.FromMinutes(2)
                                    : TimeSpan.FromMinutes(10));

                            if (!string.IsNullOrWhiteSpace(metaWebhookUrl) && config.WebhookUrl != metaWebhookUrl)
                            {
                                config.WebhookUrl = metaWebhookUrl;
                                await _wabaRepository.AddOrUpdateAsync(config);
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(metaWebhookUrl))
                        {
                            finalWebhookUrl = metaWebhookUrl;
                        }
                    }

                    return Ok(new
                    {
                        connectionId = conn.Id,
                        connectionName = conn.Name,
                        isConnected = actuallyConnected,
                        facebookAppId = config?.FacebookAppId ?? string.Empty,
                        facebookAppSecret = !string.IsNullOrEmpty(config?.FacebookAppSecret) ? "••••••••" : string.Empty,
                        wabaId = config?.WabaId ?? string.Empty,
                        accessToken = config?.AccessToken ?? string.Empty,
                        webhookUrl = finalWebhookUrl,
                        verifyToken = string.IsNullOrWhiteSpace(config?.VerifyToken) ? string.Empty : config.VerifyToken,
                        phoneNumbers = connPhones.Select(p => new
                        {
                            phoneNumber = p.PhoneNumber,
                            displayName = p.DisplayName,
                            verifiedName = p.VerifiedName,
                            phoneNumberId = p.PhoneNumberId,
                            quality = p.Quality,
                            messagesSent = sentToday,
                            messageLimit = p.MessageLimit ?? "1000"
                        }),
                        tokenInfo = new
                        {
                            scopes = new[] { "whatsapp_business_management", "whatsapp_business_messaging", "public_profile" },
                            issuedAt = conn.ConnectedOn ?? DateTime.UtcNow
                        },
                        business = new
                        {
                            businessId = config?.WabaId ?? string.Empty
                        }
                    });
                }
            }

            var data = await _dashboardService.GetDashboardDataAsync();
            if (data == null)
            {
                return Ok(new { isConnected = false });
            }
            return Ok(data);
        }

        [HttpPost("send-message")]
        [RequiresPermission("Chat.Send")]
        public async Task<IActionResult> SendMessage([FromBody] SendTestMessageRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            // Find the correct config for the given connectionId
            WabaConfiguration? config = null;
            if (request.ConnectionId.HasValue)
            {
                config = await _dbContext.WabaConfigurations
                    .FirstOrDefaultAsync(c => c.ConnectionId == request.ConnectionId.Value && c.Connected);
            }
            config ??= await _wabaRepository.GetAsync();

            if (config == null || !config.Connected)
            {
                return BadRequest(new { message = "WABA is not configured. Please complete the setup." });
            }

            // Find the phone number for this specific connection
            var phones = await _phoneRepository.GetAllAsync();
            var primaryPhone = request.ConnectionId.HasValue
                ? phones.FirstOrDefault(p => p.ConnectionId == request.ConnectionId.Value)
                    ?? phones.FirstOrDefault()
                : phones.FirstOrDefault();

            if (primaryPhone == null)
            {
                return BadRequest(new { message = "No registered WABA phone number found to send from." });
            }

            bool success = await _metaGraphService.SendTemplateMessageAsync(
                primaryPhone.PhoneNumberId,
                config.AccessToken,
                request.RecipientNumber,
                request.TemplateName,
                request.LanguageCode
            );

            if (success)
            {
                return Ok(new { message = $"Test message sent successfully to {request.RecipientNumber} using template '{request.TemplateName}'." });
            }

            return BadRequest(new { message = "Failed to send message. Please review access token permissions or recipient number format." });
        }

        [HttpPost("verify-webhook")]
        [RequiresPermission("ConnectAccount.Connect")]
        public async Task<IActionResult> VerifyWebhook([FromBody] VerifyWebhookRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            bool isVerified = await _webhookService.TriggerVerificationAsync(request.VerifyToken);
            if (isVerified)
            {
                return Ok(new { message = "Webhook verified successfully.", verified = true });
            }

            return BadRequest(new { message = "Webhook verification failed. Token mismatch or endpoint unreachable.", verified = false });
        }

        [HttpPost("disconnect")]
        [RequiresPermission("ConnectAccount.Disconnect")]
        public async Task<IActionResult> Disconnect([FromQuery] int? connectionId = null)
        {
            if (connectionId.HasValue)
            {
                await _connectionService.SoftDisconnectAsync(connectionId.Value);
                return Ok(new { message = $"Connection {connectionId.Value} soft-disconnected successfully." });
            }

            await _wabaRepository.DeleteAsync();
            return Ok(new { message = "WhatsApp Business Account soft-disconnected successfully." });
        }

        [HttpPost("disconnect-webhook")]
        [RequiresPermission("ConnectAccount.Disconnect")]
        public async Task<IActionResult> DisconnectWebhook([FromQuery] int? connectionId = null)
        {
            if (!connectionId.HasValue)
            {
                return BadRequest(new { message = "Connection ID is required." });
            }

            var config = await _dbContext.WabaConfigurations
                .FirstOrDefaultAsync(c => c.ConnectionId == connectionId.Value);

            if (config == null)
            {
                return BadRequest(new { message = "No configuration found for this connection." });
            }

            config.VerifyToken = string.Empty;
            config.FacebookAppSecret = string.Empty;
            config.WebhookUrl = string.Empty;
            config.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            return Ok(new { message = "Webhook disconnected successfully. Verify Token and App Secret have been cleared." });
        }

        [HttpPost("refresh")]
        [RequiresPermission("ConnectAccount.Connect")]
        public async Task<IActionResult> Refresh([FromQuery] int? connectionId = null)
        {
            // Find the correct config for the given connectionId
            WabaConfiguration? config = null;
            if (connectionId.HasValue)
            {
                config = await _dbContext.WabaConfigurations
                    .FirstOrDefaultAsync(c => c.ConnectionId == connectionId.Value && c.Connected);
            }
            config ??= await _wabaRepository.GetAsync();

            if (config == null || !config.Connected)
            {
                return BadRequest(new { message = "No connected integration to refresh." });
            }

            // Sync latest elements from Graph API
            try
            {
                var biz = await _metaGraphService.GetBusinessDetailsAsync(config.WabaId, config.AccessToken);
                await _businessRepository.SaveAsync(biz);

                var phones = await _metaGraphService.GetPhoneNumbersAsync(config.WabaId, config.AccessToken);
                if (phones != null && phones.Any())
                {
                    foreach (var p in phones)
                    {
                        p.ConnectionId = config.ConnectionId;
                    }
                    await _phoneRepository.SaveRangeAsync(phones);
                }

                await _templateService.SyncFromWhatsAppAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during background refresh: {ex.Message}");
            }

            // Run fresh health check
            await _healthService.RunHealthCheckAsync();

            // Return updated dashboard data
            var data = await _dashboardService.GetDashboardDataAsync();
            return Ok(data);
        }

        /// <summary>
        /// Subscribes the Meta App to receive webhooks for ALL connected WABAs.
        /// Call this if incoming messages are not being received for a connection.
        /// This is safe to call multiple times — Meta treats it as idempotent.
        /// </summary>
        [HttpPost("subscribe-webhooks")]
        [RequiresPermission("ConnectAccount.Connect")]
        public async Task<IActionResult> SubscribeWebhooks([FromQuery] int? connectionId = null)
        {
            var query = _dbContext.WabaConfigurations
                .Include(c => c.Connection)
                .ForLiveConnections()
                .Where(c => !string.IsNullOrEmpty(c.WabaId) && !string.IsNullOrEmpty(c.AccessToken));

            if (connectionId.HasValue)
            {
                query = query.Where(c => c.ConnectionId == connectionId.Value);
            }

            var configs = await query.ToListAsync();

            if (configs.Count == 0)
            {
                return BadRequest(new { message = "No connected WABA configurations found." });
            }

            var results = new List<object>();

            foreach (var cfg in configs)
            {
                // Check current subscription status
                var isSubscribed = await _metaGraphService.IsAppSubscribedToWabaAsync(cfg.WabaId, cfg.AccessToken);
                
                bool subscribeResult = isSubscribed;
                if (!isSubscribed)
                {
                    // Subscribe the app
                    subscribeResult = await _metaGraphService.SubscribeAppToWabaAsync(cfg.WabaId, cfg.AccessToken);
                }

                results.Add(new
                {
                    connectionId = cfg.ConnectionId,
                    connectionName = cfg.Connection?.Name,
                    wabaId = cfg.WabaId,
                    wasAlreadySubscribed = isSubscribed,
                    subscribed = subscribeResult
                });
            }

            return Ok(new
            {
                message = "Webhook subscription check complete.",
                results
            });
        }

        /// <summary>
        /// Checks the webhook subscription status for all connected WABAs.
        /// </summary>
        [HttpGet("webhook-status")]
        [RequiresPermission("ConnectAccount.View")]
        public async Task<IActionResult> GetWebhookStatus()
        {
            var configs = await _dbContext.WabaConfigurations
                .Include(c => c.Connection)
                .ForLiveConnections()
                .Where(c => !string.IsNullOrEmpty(c.WabaId) && !string.IsNullOrEmpty(c.AccessToken))
                .ToListAsync();

            var results = new List<object>();

            foreach (var cfg in configs)
            {
                var isSubscribed = await _metaGraphService.IsAppSubscribedToWabaAsync(cfg.WabaId, cfg.AccessToken);

                results.Add(new
                {
                    connectionId = cfg.ConnectionId,
                    connectionName = cfg.Connection?.Name,
                    wabaId = cfg.WabaId,
                    isSubscribed
                });
            }

            return Ok(new { connections = results });
        }

        [HttpPost("message-limit")]
        [RequiresPermission("ConnectAccount.Connect")]
        public async Task<IActionResult> UpdateMessageLimit([FromBody] SetMessageLimitRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var phones = await _phoneRepository.GetAllAsync();
            var phoneList = phones.ToList();
            WabaPhoneNumber? targetPhone = null;

            if (!string.IsNullOrWhiteSpace(request.PhoneNumberId))
            {
                targetPhone = phoneList.FirstOrDefault(p => p.PhoneNumberId == request.PhoneNumberId);
            }
            else if (request.ConnectionId.HasValue)
            {
                targetPhone = phoneList.FirstOrDefault(p => p.ConnectionId == request.ConnectionId.Value);
            }

            if (targetPhone == null && phoneList.Count > 0)
            {
                targetPhone = phoneList.First();
            }

            if (targetPhone == null)
            {
                return BadRequest(new { message = "No registered phone number found to update message limit." });
            }

            targetPhone.MessageLimit = request.MessageLimit.ToString();
            await _phoneRepository.SaveRangeAsync(new[] { targetPhone });

            return Ok(new
            {
                message = $"Message limit updated to {request.MessageLimit} for {targetPhone.DisplayName} ({targetPhone.PhoneNumber}).",
                connectionId = targetPhone.ConnectionId,
                phoneNumberId = targetPhone.PhoneNumberId,
                messageLimit = request.MessageLimit
            });
        }
    }
}
