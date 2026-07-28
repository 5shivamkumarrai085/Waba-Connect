using Microsoft.AspNetCore.Mvc;
using WhatsAppCampaignApi.Models.DTOs.Webhook;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Controllers;

[ApiController]
[Route("api/webhook/whatsapp")]
public class WebhookController : ControllerBase
{
    private readonly IWhatsAppService _whatsAppService;
    private readonly IDashboardCacheService _dashboardCacheService;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(
        IWhatsAppService whatsAppService,
        IDashboardCacheService dashboardCacheService,
        ILogger<WebhookController> logger)
    {
        _whatsAppService = whatsAppService;
        _dashboardCacheService = dashboardCacheService;
        _logger = logger;
    }

    /// <summary>
    /// Verification challenge from Meta
    /// </summary>
    [HttpGet]
    public IActionResult VerifyWebhook(
        [FromQuery(Name = "hub.mode")] string mode,
        [FromQuery(Name = "hub.challenge")] string challenge,
        [FromQuery(Name = "hub.verify_token")] string token)
    {
        _logger.LogInformation("Received WhatsApp Webhook GET verification request. Mode: {Mode}, Token: {Token}", mode, token);

        var isValid = _whatsAppService.VerifyWebhook(mode, token, challenge);
        
        if (isValid)
        {
            _logger.LogInformation("WhatsApp Webhook GET verification succeeded. Returning raw challenge.");
            return Content(challenge, "text/plain"); // Meta expects raw challenge string back with HTTP 200
        }
            
        _logger.LogWarning("WhatsApp Webhook GET verification failed for mode: {Mode}, token: {Token}", mode, token);
        return Forbid();
    }

    /// <summary>
    /// Event notifications (message status updates / inbound messages) from Meta
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ReceiveWebhook([FromBody] WhatsAppWebhookPayload payload)
    {
        _logger.LogInformation("Received WhatsApp Webhook POST event payload.");

        // Process inside the request scope so DbContext-backed webhook updates are reliable.
        await _whatsAppService.ProcessWebhookAsync(payload);
        
        // Invalidate dashboard cache for real-time metric updates
        _dashboardCacheService.InvalidateCache();
        
        return Ok();
    }
}
