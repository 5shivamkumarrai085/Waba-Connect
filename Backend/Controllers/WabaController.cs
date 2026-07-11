using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using WhatsAppCampaignApi.Models.DTOs;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Models.Entities;

namespace WhatsAppCampaignApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
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

        public WabaController(
            IWabaRepository wabaRepository,
            IBusinessRepository businessRepository,
            IPhoneRepository phoneRepository,
            IHealthLogRepository healthLogRepository,
            IMetaGraphService metaGraphService,
            IWebhookService webhookService,
            ITemplateService templateService,
            IDashboardService dashboardService,
            IHealthService healthService)
        {
            _wabaRepository = wabaRepository;
            _businessRepository = businessRepository;
            _phoneRepository = phoneRepository;
            _healthLogRepository = healthLogRepository;
            _metaGraphService = metaGraphService;
            _webhookService = webhookService;
            _templateService = templateService;
            _dashboardService = dashboardService;
            _healthService = healthService;
        }

        [HttpPost("connect-app")]
        public async Task<IActionResult> ConnectApp([FromBody] ConnectAppRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            bool isValid = await _metaGraphService.ValidateAppAsync(request.FacebookAppId, request.FacebookAppSecret);
            if (!isValid)
            {
                return BadRequest(new { message = "Invalid Facebook App credentials. Meta Graph API validation failed." });
            }

            // Generate webhook verification details
            string verifyToken = "waba_verify_token_" + Guid.NewGuid().ToString("N").Substring(0, 16);
            string webhookUrl = $"{Request.Scheme}://{Request.Host}/api/webhook/whatsapp";

            var config = new WabaConfiguration
            {
                FacebookAppId = request.FacebookAppId,
                FacebookAppSecret = request.FacebookAppSecret,
                VerifyToken = verifyToken,
                WebhookUrl = webhookUrl,
                Connected = false
            };

            await _wabaRepository.AddOrUpdateAsync(config);

            return Ok(new
            {
                message = "Facebook App connected successfully.",
                webhookUrl = webhookUrl,
                verifyToken = verifyToken
            });
        }

        [HttpPost("configure")]
        public async Task<IActionResult> Configure([FromBody] ConfigureWabaRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var config = await _wabaRepository.GetAsync();
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
                await _phoneRepository.SaveRangeAsync(phones);
            }

            // Fetch message templates
            await _templateService.SyncFromWhatsAppAsync();

            // Run initial health status check
            await _healthService.RunHealthCheckAsync();

            return Ok(new { message = "WhatsApp Business Account configured successfully." });
        }

        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboard()
        {
            var data = await _dashboardService.GetDashboardDataAsync();
            if (data == null)
            {
                return Ok(new { isConnected = false });
            }
            return Ok(data);
        }

        [HttpPost("send-message")]
        public async Task<IActionResult> SendMessage([FromBody] SendTestMessageRequest request)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var config = await _wabaRepository.GetAsync();
            if (config == null || !config.Connected)
            {
                return BadRequest(new { message = "WABA is not configured. Please complete the setup." });
            }

            var phones = await _phoneRepository.GetAllAsync();
            var primaryPhone = phones.FirstOrDefault();
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
        public async Task<IActionResult> Disconnect()
        {
            await _wabaRepository.DeleteAsync();
            await _businessRepository.ClearAllAsync();
            await _phoneRepository.ClearAllAsync();
            await _healthLogRepository.ClearAllAsync();

            return Ok(new { message = "WhatsApp Business Account disconnected successfully. Configuration wiped." });
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh()
        {
            var config = await _wabaRepository.GetAsync();
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
                    await _phoneRepository.SaveRangeAsync(phones);
                }

                await _templateService.SyncFromWhatsAppAsync();
            }
            catch (Exception ex)
            {
                // Log and continue to let health check report issues
                Console.WriteLine($"Error during background refresh: {ex.Message}");
            }

            // Run fresh health check
            await _healthService.RunHealthCheckAsync();

            // Return updated dashboard data
            var data = await _dashboardService.GetDashboardDataAsync();
            return Ok(data);
        }
    }
}
