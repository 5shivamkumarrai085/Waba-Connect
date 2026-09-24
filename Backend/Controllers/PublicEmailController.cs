using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Email;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Controllers;

/// <summary>
/// The unsubscribe endpoints recipients reach from the mail itself.
///
/// <para>
/// Anonymous, and necessarily so: a recipient is not a user of this system and must never be
/// asked to sign in to stop receiving mail. Authorisation comes from an HMAC-signed token in the
/// link instead, which carries the address it is allowed to suppress — so the endpoint cannot be
/// used to unsubscribe anyone else, and the URLs cannot be enumerated.
/// </para>
/// </summary>
[ApiController]
[Route("api/public/email")]
[AllowAnonymous]
public class PublicEmailController : ControllerBase
{
    private readonly IUnsubscribeTokenService _tokens;
    private readonly IEmailSuppressionService _suppression;
    private readonly IAuditService _auditService;
    private readonly ILogger<PublicEmailController> _logger;

    public PublicEmailController(
        IUnsubscribeTokenService tokens,
        IEmailSuppressionService suppression,
        IAuditService auditService,
        ILogger<PublicEmailController> logger)
    {
        _tokens = tokens;
        _suppression = suppression;
        _auditService = auditService;
        _logger = logger;
    }

    /// <summary>
    /// RFC 8058 one-click unsubscribe.
    ///
    /// <para>
    /// This is what Gmail and Outlook call when a recipient uses the unsubscribe control in their
    /// mail client. It must be a POST that suppresses immediately with no confirmation step — a
    /// landing page here would fail the specification, the clients would stop showing the control,
    /// and recipients would use the spam button instead, which costs far more sending reputation
    /// than an unsubscribe does.
    /// </para>
    /// </summary>
    [HttpPost("unsubscribe")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> UnsubscribeOneClick([FromQuery(Name = "t")] string? t, CancellationToken ct)
    {
        var payload = _tokens.Validate(t ?? string.Empty);

        if (payload is null)
        {
            // One generic answer for every reason a token can be invalid. Distinguishing
            // "expired" from "forged" would tell someone probing the endpoint which of the two
            // they had produced.
            return BadRequest(new { message = "This unsubscribe link is not valid or has expired." });
        }

        await SuppressAsync(payload, "one-click", ct);

        // A bare 200. The mail client made this request, not a person, and nothing renders it.
        return Ok(new { message = "You have been unsubscribed." });
    }

    /// <summary>
    /// The landing page, for a recipient who clicks the link in the message body.
    ///
    /// <para>
    /// Suppresses on the GET rather than showing a confirmation button. That is a deliberate
    /// trade-off: a confirmation step means some recipients who intended to unsubscribe do not
    /// complete it and report the message as spam instead. The risk of an accidental unsubscribe
    /// is small and recoverable; a spam complaint is neither.
    /// </para>
    /// <para>
    /// Returns HTML rather than JSON — this one is read by a person, in a browser.
    /// </para>
    /// </summary>
    [HttpGet("unsubscribe")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> UnsubscribeLanding([FromQuery(Name = "t")] string? t, CancellationToken ct)
    {
        var payload = _tokens.Validate(t ?? string.Empty);

        if (payload is null)
        {
            return Content(BuildPage(
                "Link not valid",
                "This unsubscribe link is not valid or has expired. If you are still receiving mail you did "
              + "not ask for, please reply to the message and we will remove you."), "text/html");
        }

        await SuppressAsync(payload, "landing page", ct);

        return Content(BuildPage(
            "You have been unsubscribed",
            $"<strong>{System.Net.WebUtility.HtmlEncode(payload.Email)}</strong> has been removed from this "
          + "mailing list. You will not receive further marketing email at this address."), "text/html");
    }

    private async Task SuppressAsync(UnsubscribePayload payload, string source, CancellationToken ct)
    {
        await _suppression.SuppressAsync(
            payload.Email,
            SuppressionReason.Unsubscribe,
            source: $"UnsubscribeLink ({source})",
            detail: payload.CampaignId is { } campaignId ? $"From campaign {campaignId}." : null,

            // Global, not scoped to the campaign's connection. Somebody asking not to be mailed
            // means not from any of our sending identities — honouring it on one and continuing
            // on another is the sort of thing that turns an unsubscribe into a complaint.
            connectionId: null,
            createdBy: "Recipient",
            ct: ct);

        await _auditService.LogAsync(
            "EmailSuppression.Unsubscribed", "Data",
            $"{payload.Email} unsubscribed via the {source} link"
          + $"{(payload.CampaignId is { } id ? $" from campaign {id}" : "")}.",
            "EmailSuppression", payload.Email);

        _logger.LogInformation(
            "{Email} unsubscribed via {Source}.", payload.Email, source);
    }

    /// <summary>
    /// A self-contained confirmation page.
    ///
    /// <para>
    /// No external stylesheet, font or script. This page is opened from an email client by someone
    /// who has just asked to hear less from us, so it should not then load third-party resources
    /// from their browser — and it has to render even if this service's static assets are
    /// unavailable.
    /// </para>
    /// </summary>
    // A $$ raw string, so the CSS braces are literal and only {{...}} interpolates. With a single
    // $ every `{` in the stylesheet would start an interpolation.
    private static string BuildPage(string title, string bodyHtml) =>
        $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>{{System.Net.WebUtility.HtmlEncode(title)}}</title>
          <style>
            :root { color-scheme: light dark; }
            body {
              font-family: system-ui, -apple-system, "Segoe UI", Roboto, sans-serif;
              display: flex; align-items: center; justify-content: center;
              min-height: 100vh; margin: 0; padding: 24px;
              background: #f4f6f8; color: #1e293b;
            }
            .card {
              background: #fff; border-radius: 16px; padding: 40px;
              max-width: 480px; width: 100%;
              box-shadow: 0 4px 24px rgba(15, 23, 42, 0.08);
            }
            h1 { font-size: 20px; margin: 0 0 12px; }
            p { font-size: 14px; line-height: 1.6; margin: 0; color: #475569; }
            @media (prefers-color-scheme: dark) {
              body { background: #0f172a; color: #f8fafc; }
              .card { background: #1e293b; box-shadow: none; }
              p { color: #cbd5e1; }
            }
          </style>
        </head>
        <body>
          <div class="card">
            <h1>{{System.Net.WebUtility.HtmlEncode(title)}}</h1>
            <p>{{bodyHtml}}</p>
          </div>
        </body>
        </html>
        """;
}
