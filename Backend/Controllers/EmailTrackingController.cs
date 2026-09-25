using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Email;

namespace WhatsAppCampaignApi.Controllers;

/// <summary>
/// Anonymous public endpoints for email engagement tracking.
///
/// <para>
/// These endpoints are embedded in outbound campaign emails and must be publicly reachable.
/// They do not require authentication but are protected by HMAC-signed tokens.
/// </para>
///
/// <para>
/// Security measures:
/// - Tokens are HMAC-SHA256 signed (unforgeable without the server's signing key)
/// - Open tokens are single-purpose (open-pixel only; cannot be used as click tokens)
/// - Click tokens are validated before redirecting; arbitrary redirect is prevented by
///   requiring the destination URL to be part of the signed payload
/// - Rate limiting should be applied at the load balancer / reverse proxy level
/// - No internal IDs are exposed in the URL; only the opaque tracking token
/// </para>
/// </summary>
[Route("api/t")]
[ApiController]
[AllowAnonymous]
public class EmailTrackingController : ControllerBase
{
    private readonly IEmailTrackingService _tracking;
    private readonly ICampaignEmailEventProcessor _eventProcessor;
    private readonly AppDbContext _db;
    private readonly ILogger<EmailTrackingController> _logger;

    public EmailTrackingController(
        IEmailTrackingService tracking,
        ICampaignEmailEventProcessor eventProcessor,
        AppDbContext db,
        ILogger<EmailTrackingController> logger)
    {
        _tracking       = tracking;
        _eventProcessor = eventProcessor;
        _db             = db;
        _logger         = logger;
    }

    /// <summary>
    /// Open-tracking pixel endpoint.
    ///
    /// Returns a 1×1 transparent GIF. Called when the recipient's email client loads images.
    /// Records an OPENED event (idempotent — loading the pixel twice is one open).
    /// </summary>
    [HttpGet("o/{token}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> TrackOpen(string token, CancellationToken ct)
    {
        var trackingId = _tracking.ValidateOpenToken(token);

        if (trackingId is null)
        {
            _logger.LogWarning("Invalid open tracking token received.");
            // Return a pixel anyway — do not reveal whether the token was valid
            return PixelResponse();
        }

        var recipient = await _db.CampaignContacts
            .AsNoTracking()
            .Where(cc => cc.TrackingId == trackingId)
            .Select(cc => new { cc.Id, cc.CampaignId })
            .FirstOrDefaultAsync(ct);

        if (recipient is not null)
        {
            var userAgent  = Request.Headers.UserAgent.ToString();
            var ipAddress  = GetClientIp();
            var idempotencyKey = $"pixel:{trackingId}:unique";  // One unique open per recipient

            await _eventProcessor.ProcessAsync(
                kind:              EmailEventKind.Opened,
                idempotencyKey:    idempotencyKey,
                source:            "Pixel",
                campaignId:        recipient.CampaignId,
                campaignContactId: recipient.Id,
                userAgent:         userAgent,
                ipAddress:         ipAddress,
                occurredAt:        DateTime.UtcNow,
                ct:                ct);
        }
        else
        {
            _logger.LogDebug("Open pixel: no recipient found for tracking ID {Id}.", trackingId[..8]);
        }

        return PixelResponse();
    }

    /// <summary>
    /// Click-tracking redirect endpoint.
    ///
    /// Validates the token, records a CLICKED event, then redirects to the original URL.
    /// The destination URL is part of the signed payload — arbitrary redirect is not possible.
    /// </summary>
    [HttpGet("c/{token}")]
    public async Task<IActionResult> TrackClick(string token, CancellationToken ct)
    {
        var result = _tracking.ValidateClickToken(token);

        if (result is null)
        {
            _logger.LogWarning("Invalid click tracking token received.");
            // Return 400 rather than 404 — 404 from a click link looks like a broken URL
            return BadRequest("Invalid tracking token.");
        }

        var (trackingId, destinationUrl) = result.Value;

        // Open-redirect protection: only allow HTTP/HTTPS URLs
        if (!IsAllowedUrl(destinationUrl))
        {
            _logger.LogWarning("Click token contained a disallowed URL scheme: {Url}",
                destinationUrl.Length > 100 ? destinationUrl[..100] : destinationUrl);
            return BadRequest("Destination URL is not allowed.");
        }

        var recipient = await _db.CampaignContacts
            .AsNoTracking()
            .Where(cc => cc.TrackingId == trackingId)
            .Select(cc => new { cc.Id, cc.CampaignId })
            .FirstOrDefaultAsync(ct);

        if (recipient is not null)
        {
            var userAgent  = Request.Headers.UserAgent.ToString();
            var ipAddress  = GetClientIp();
            var now        = DateTime.UtcNow;

            // 1. Click event
            var timestamp  = now.ToString("yyyyMMddHHmm");
            var linkHash   = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(destinationUrl)))[..8];
            var idempotencyKey = $"click:{trackingId}:{linkHash}:{timestamp}";

            await _eventProcessor.ProcessAsync(
                kind:              EmailEventKind.Clicked,
                idempotencyKey:    idempotencyKey,
                source:            "Click",
                campaignId:        recipient.CampaignId,
                campaignContactId: recipient.Id,
                originalUrl:       destinationUrl,
                userAgent:         userAgent,
                ipAddress:         ipAddress,
                occurredAt:        now,
                ct:                ct);

            // 2. Click implies Open: if image proxy blocked the tracking pixel, clicking the link confirms email was read
            var openIdempotencyKey = $"pixel:{trackingId}:unique";
            await _eventProcessor.ProcessAsync(
                kind:              EmailEventKind.Opened,
                idempotencyKey:    openIdempotencyKey,
                source:            "ClickImplicitOpen",
                campaignId:        recipient.CampaignId,
                campaignContactId: recipient.Id,
                userAgent:         userAgent,
                ipAddress:         ipAddress,
                occurredAt:        now,
                ct:                ct);
        }

        // Always redirect — do not block the user's experience
        return Redirect(destinationUrl);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────

    private static readonly byte[] _transparentGif =
    [
        0x47, 0x49, 0x46, 0x38, 0x39, 0x61, // GIF89a
        0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00,
        0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00,
        0x21, 0xF9, 0x04, 0x01, 0x00, 0x00, 0x00, 0x00,
        0x2C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
        0x02, 0x02, 0x44, 0x01, 0x00,
        0x3B
    ];

    private IActionResult PixelResponse()
    {
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        Response.Headers.Pragma       = "no-cache";
        return File(_transparentGif, "image/gif");
    }

    private static bool IsAllowedUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        return uri.Scheme is "http" or "https";
    }

    private string GetClientIp()
    {
        // Respects X-Forwarded-For when behind a reverse proxy (configured via ForwardedHeaders middleware)
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        return ip.Length > 64 ? ip[..64] : ip;
    }
}
