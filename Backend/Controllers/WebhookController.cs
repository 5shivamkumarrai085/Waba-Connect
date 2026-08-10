using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.Webhook;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Controllers;

/// <remarks>
/// The two Meta-facing actions MUST remain [AllowAnonymous]. Meta's servers call them and will
/// never present a bearer token — gating them would silently stop every inbound WhatsApp message
/// and every delivery-status update, with no error surfaced anywhere in the app.
///
/// <para>
/// [AllowAnonymous] sits on those two actions rather than on the class, because AllowAnonymous
/// anywhere on an endpoint beats an action-level [Authorize]: with it at class level the /debug
/// action stayed anonymous no matter what it was decorated with.
/// </para>
/// </remarks>
[ApiController]
[Route("api/webhook/whatsapp")]
public class WebhookController : ControllerBase
{
    private readonly IWhatsAppService _whatsAppService;
    private readonly IDashboardCacheService _dashboardCacheService;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(
        IWhatsAppService whatsAppService,
        IDashboardCacheService dashboardCacheService,
        AppDbContext dbContext,
        ILogger<WebhookController> logger)
    {
        _whatsAppService = whatsAppService;
        _dashboardCacheService = dashboardCacheService;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Verification challenge from Meta
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
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
    [AllowAnonymous]
    public async Task<IActionResult> ReceiveWebhook([FromBody] WhatsAppWebhookPayload payload)
    {
        // Log detailed info about incoming webhook for debugging
        if (payload.Entry != null)
        {
            foreach (var entry in payload.Entry)
            {
                if (entry.Changes == null) continue;
                foreach (var change in entry.Changes)
                {
                    var phoneNumberId = change.Value?.Metadata?.PhoneNumberId;
                    var displayPhone = change.Value?.Metadata?.DisplayPhoneNumber;
                    var messageCount = change.Value?.Messages?.Count ?? 0;
                    var statusCount = change.Value?.Statuses?.Count ?? 0;

                    _logger.LogInformation(
                        "Webhook POST received — PhoneNumberId: {PhoneNumberId}, DisplayPhone: {DisplayPhone}, Messages: {MsgCount}, Statuses: {StatusCount}",
                        phoneNumberId, displayPhone, messageCount, statusCount);
                }
            }
        }

        // Process inside the request scope so DbContext-backed webhook updates are reliable.
        await _whatsAppService.ProcessWebhookAsync(payload);
        
        // Invalidate dashboard cache for real-time metric updates
        _dashboardCacheService.InvalidateCache();
        
        return Ok();
    }

    /// <summary>
    /// Debug endpoint: shows all registered connections, phone numbers, and verify tokens
    /// so you can verify your webhook configuration on Meta's developer portal.
    /// </summary>
    /// <remarks>
    /// The controller is [AllowAnonymous] because Meta calls it without a token, which meant
    /// this action was anonymously disclosing verify tokens and connection config. It now
    /// requires a signed-in user holding ConnectAccount.View — Meta never calls this route,
    /// only the verify and receive routes.
    /// </remarks>
    [HttpGet("debug")]
    [Authorize]
    [RequiresPermission("ConnectAccount.View")]
    public async Task<IActionResult> DebugWebhookConfig()
    {
        var configs = await _dbContext.WabaConfigurations
            .Include(c => c.Connection)
            .OrderBy(c => c.Id)
            .ToListAsync();

        var phones = await _dbContext.WabaPhoneNumbers
            .Include(p => p.Connection)
            .OrderBy(p => p.Id)
            .ToListAsync();

        var result = new
        {
            webhookEndpoint = $"{Request.Scheme}://{Request.Host}/api/webhook/whatsapp",
            instructions = new
            {
                step1 = "Go to Meta Developer Portal → Your App → WhatsApp → Configuration",
                step2 = "Set 'Callback URL' to the webhookEndpoint above (use your ngrok URL in place of localhost)",
                step3 = "Set 'Verify Token' to one of the verify tokens listed below",
                step4 = "Subscribe to 'messages' webhook field",
                important = "If your WABAs are under DIFFERENT Meta Apps, each app needs its own webhook URL configured with the SAME ngrok URL"
            },
            connections = configs.Select(c => new
            {
                connectionId = c.ConnectionId,
                connectionName = c.Connection?.Name,
                wabaId = c.WabaId,
                verifyToken = c.VerifyToken,
                isConnected = c.Connected,
                hasAccessToken = !string.IsNullOrWhiteSpace(c.AccessToken),
                matchingPhone = phones.FirstOrDefault(p => p.ConnectionId == c.ConnectionId)?.PhoneNumber
            }),
            phoneNumbers = phones.Select(p => new
            {
                id = p.Id,
                connectionId = p.ConnectionId,
                connectionName = p.Connection?.Name,
                phoneNumber = p.PhoneNumber,
                phoneNumberId = p.PhoneNumberId,
                displayName = p.DisplayName
            })
        };

        return Ok(result);
    }
}
