using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.Webhook;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Services.Queue;
using WhatsAppCampaignApi.Services.WhatsApp;

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
    private readonly IWhatsAppWebhookSignatureVerifier _signatureVerifier;
    private readonly IJobQueue _queue;
    private readonly IWebHostEnvironment _environment;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(
        IWhatsAppService whatsAppService,
        IWhatsAppWebhookSignatureVerifier signatureVerifier,
        IJobQueue queue,
        IWebHostEnvironment environment,
        AppDbContext dbContext,
        ILogger<WebhookController> logger)
    {
        _whatsAppService = whatsAppService;
        _signatureVerifier = signatureVerifier;
        _queue = queue;
        _environment = environment;
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
        // The verify token is a shared secret: never logged.
        _logger.LogInformation("Received WhatsApp Webhook GET verification request. Mode: {Mode}", mode);

        var isValid = _whatsAppService.VerifyWebhook(mode, token, challenge);
        
        if (isValid)
        {
            _logger.LogInformation("WhatsApp Webhook GET verification succeeded. Returning raw challenge.");
            return Content(challenge, "text/plain"); // Meta expects raw challenge string back with HTTP 200
        }
            
        _logger.LogWarning("WhatsApp Webhook GET verification failed for mode: {Mode}", mode);
        return Forbid();
    }

    /// <summary>
    /// Event notifications (message status updates / inbound messages) from Meta.
    /// </summary>
    /// <remarks>
    /// Verify, store, acknowledge. The signature is checked against the raw bytes (re-serialising
    /// would change them), the delivery is written to the durable queue, and Meta gets its 200
    /// straight away; <see cref="WhatsAppWebhookWorker"/> does the processing. If the queue write
    /// fails the response is a 500, so Meta redelivers rather than the event being lost.
    /// </remarks>
    [HttpPost]
    [AllowAnonymous]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> ReceiveWebhook(CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, ct);
        var body = buffer.ToArray();

        var signature = await _signatureVerifier.VerifyAsync(body, Request.Headers["X-Hub-Signature-256"], ct);
        switch (signature)
        {
            case WebhookSignatureResult.Invalid:
                _logger.LogWarning("Rejected a WhatsApp webhook POST with a missing or invalid X-Hub-Signature-256.");
                return Unauthorized();

            case WebhookSignatureResult.NotConfigured when _environment.IsProduction():
                _logger.LogError("Rejected a WhatsApp webhook POST: no app secret is configured to verify it against.");
                return Unauthorized();

            case WebhookSignatureResult.NotConfigured:
                _logger.LogWarning("WhatsApp webhook accepted WITHOUT signature verification: no app secret is configured.");
                break;
        }

        var text = Encoding.UTF8.GetString(body);
        if (string.IsNullOrWhiteSpace(text)) return Ok();

        // Meta redelivers the identical body when it did not see our 200; hashing it makes that
        // redelivery a no-op at the queue instead of a second round of processing.
        var idempotencyKey = Convert.ToHexString(SHA256.HashData(body));

        await _queue.EnqueueAsync(new[]
        {
            new QueueMessage
            {
                QueueName = QueueNames.WhatsAppWebhook,
                Payload = JsonSerializer.Serialize(new WhatsAppWebhookJob(text, DateTime.UtcNow)),
                IdempotencyKey = idempotencyKey,
                Priority = 10
            }
        }, ct);

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
                // Masked: enough to tell which token is configured, not enough to reuse it.
                verifyToken = string.IsNullOrEmpty(c.VerifyToken) ? null
                    : c.VerifyToken.Length <= 4 ? "****" : $"****{c.VerifyToken[^4..]}",
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
