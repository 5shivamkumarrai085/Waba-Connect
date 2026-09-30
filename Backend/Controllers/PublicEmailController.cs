using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Email;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Services.Compliance;
using WhatsAppCampaignApi.Models.Entities;

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
    private readonly ICampaignEmailEventProcessor _eventProcessor;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<PublicEmailController> _logger;
    private readonly IConsentService _consent;
    private readonly IOmniSettingsService _settings;

    public PublicEmailController(
        IUnsubscribeTokenService tokens,
        IEmailSuppressionService suppression,
        IAuditService auditService,
        ICampaignEmailEventProcessor eventProcessor,
        AppDbContext dbContext,
        ILogger<PublicEmailController> logger,
        IConsentService consent,
        IOmniSettingsService settings)
    {
        _consent = consent;
        _settings = settings;
        _tokens = tokens;
        _suppression = suppression;
        _auditService = auditService;
        _eventProcessor = eventProcessor;
        _dbContext = dbContext;
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

        var preferencesUrl = $"preferences?t={Uri.EscapeDataString(t!)}";
        return Content(BuildPage(
            "You have been unsubscribed",
            $"<strong>{System.Net.WebUtility.HtmlEncode(payload.Email)}</strong> has been removed from this "
          + "mailing list. You will not receive further marketing email at this address. "
          + $"<a href=\"{System.Net.WebUtility.HtmlEncode(preferencesUrl)}\">Manage your email preferences</a>."), "text/html");
    }

    private async Task SuppressAsync(UnsubscribePayload payload, string source, CancellationToken ct)
    {
        // The consent record — with where and how it was given — alongside the suppression.
        await _consent.SetForEmailAsync(
            payload.Email, ConsentTopics.All, ConsentStatus.OptedOut, WhatsAppCampaignApi.Services.Catalogs.ConsentCatalog.Sources.UnsubscribeLink,
            proof: new { link = source, campaignId = payload.CampaignId, userAgent = Truncate(Request.Headers.UserAgent.ToString(), 300) },
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
            ct: ct);

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

        // Count it against the campaign it came from, so the Unsubscribed KPI and the live page
        // see it. Previously the address was suppressed but the campaign never heard about it.
        if (payload.CampaignId is { } fromCampaign)
        {
            int? recipientId = payload.ContactId is { } contactId
                ? await _dbContext.CampaignContacts
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(cc => cc.CampaignId == fromCampaign && cc.ContactId == contactId)
                    .Select(cc => (int?)cc.Id)
                    .FirstOrDefaultAsync(ct)
                : null;

            await _eventProcessor.ProcessAsync(
                kind: EmailEventKind.Unsubscribed,
                idempotencyKey: recipientId is { } rid
                    ? $"unsub:recipient:{rid}"
                    : $"unsub:campaign:{fromCampaign}:{payload.Email.ToLowerInvariant()}",
                source: "UnsubscribeLink",
                campaignId: fromCampaign,
                campaignContactId: recipientId,
                recipientAddress: payload.Email,
                userAgent: Truncate(Request.Headers.UserAgent.ToString(), 500),
                ct: ct);
        }

        _logger.LogInformation(
            "A recipient unsubscribed via {Source} (campaign {CampaignId}).", source, payload.CampaignId);
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];

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
    // ── Preference centre ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// The recipient's email preferences, per topic. Reached from the unsubscribe page and
    /// authorised by the same signed token as the unsubscribe link — no account needed.
    /// </summary>
    [HttpGet("preferences")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Preferences([FromQuery(Name = "t")] string? t, CancellationToken ct)
    {
        var payload = _tokens.Validate(t ?? string.Empty);
        if (payload is null)
            return Content(BuildPage("Link not valid", "This preferences link is not valid or has expired."), "text/html");

        var (topics, state) = await LoadPreferencesAsync(payload.Email, ct);
        return Content(BuildPreferencesPage(payload.Email, t!, topics, state, saved: false), "text/html");
    }

    [HttpPost("preferences")]
    [EnableRateLimiting("auth")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> SavePreferences([FromForm(Name = "t")] string? t, CancellationToken ct)
    {
        var payload = _tokens.Validate(t ?? string.Empty);
        if (payload is null)
            return Content(BuildPage("Link not valid", "This preferences link is not valid or has expired."), "text/html");

        var form = await Request.ReadFormAsync(ct);
        var (topics, _) = await LoadPreferencesAsync(payload.Email, ct);
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var proof = new { page = "preference-centre", campaignId = payload.CampaignId, userAgent = Truncate(Request.Headers.UserAgent.ToString(), 300) };

        if (form.ContainsKey("unsubscribe_all"))
        {
            await SuppressAsync(payload, "preference centre", ct);
        }
        else
        {
            // Opting back in to anything lifts an earlier "unsubscribe from all".
            var anyOn = topics.Any(topic => form.ContainsKey($"topic_{topic}"));
            if (anyOn)
            {
                await _consent.SetForEmailAsync(payload.Email, ConsentTopics.All, ConsentStatus.OptedIn, WhatsAppCampaignApi.Services.Catalogs.ConsentCatalog.Sources.PreferenceCentre, proof, ip, ct);
                await _suppression.UnsuppressAsync(payload.Email, null, "Recipient (preference centre)", ct);
            }

            foreach (var topic in topics)
            {
                var status = form.ContainsKey($"topic_{topic}") ? ConsentStatus.OptedIn : ConsentStatus.OptedOut;
                await _consent.SetForEmailAsync(payload.Email, topic, status, WhatsAppCampaignApi.Services.Catalogs.ConsentCatalog.Sources.PreferenceCentre, proof, ip, ct);
            }
        }

        var (_, state) = await LoadPreferencesAsync(payload.Email, ct);
        return Content(BuildPreferencesPage(payload.Email, t!, topics, state, saved: true), "text/html");
    }

    /// <summary>The configured topics, and whether this address currently receives each one.</summary>
    private async Task<(IReadOnlyList<string> Topics, Dictionary<string, bool> Receives)> LoadPreferencesAsync(string email, CancellationToken ct)
    {
        var configured = await _settings.GetListAsync("compliance.topics");
        var topics = (configured.Count > 0 ? configured : [ConsentTopics.Marketing])
            .Select(ConsentTopics.Normalize).Where(x => x != ConsentTopics.All).Distinct().ToList();

        var normalized = email.Trim().ToLower();
        var rows = await _dbContext.ContactConsents.AsNoTracking()
            .Where(c => c.Channel == MessageChannel.Email && c.Contact.Email != null && c.Contact.Email.ToLower() == normalized)
            .Select(c => new { c.Topic, c.Status })
            .ToListAsync(ct);

        var allOut = rows.Any(r => r.Topic == ConsentTopics.All && r.Status == ConsentStatus.OptedOut);
        var receives = topics.ToDictionary(
            topic => topic,
            topic => !allOut && !rows.Any(r => r.Topic == topic && r.Status == ConsentStatus.OptedOut));

        return (topics, receives);
    }

    private static string BuildPreferencesPage(string email, string token, IReadOnlyList<string> topics, Dictionary<string, bool> receives, bool saved)
    {
        static string Label(string topic) => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(topic.Replace('-', ' ').Replace('_', ' '));
        Func<string?, string?> enc = System.Net.WebUtility.HtmlEncode;

        var rows = string.Join("", topics.Select(topic =>
            $"<label class=\"row\"><input type=\"checkbox\" name=\"topic_{enc(topic)}\" {(receives.GetValueOrDefault(topic) ? "checked" : "")}> {enc(Label(topic))}</label>"));

        var body =
            (saved ? "<p class=\"ok\">Your preferences have been saved.</p>" : "") +
            $"<p>Choose which emails <strong>{enc(email)}</strong> receives.</p>" +
            $"<form method=\"post\" action=\"preferences\"><input type=\"hidden\" name=\"t\" value=\"{enc(token)}\">" +
            rows +
            "<label class=\"row all\"><input type=\"checkbox\" name=\"unsubscribe_all\"> Unsubscribe from all marketing email</label>" +
            "<button type=\"submit\">Save preferences</button></form>";

        return BuildPage("Email preferences", body)
            .Replace("<p><p", "<div><p").Replace("</form></p>", "</form></div>")
            .Replace("</style>", """
                form { margin-top: 16px; display: grid; gap: 10px; }
                .row { display: flex; gap: 10px; align-items: center; font-size: 14px; }
                .all { margin-top: 8px; padding-top: 12px; border-top: 1px solid rgba(148,163,184,.4); }
                button { margin-top: 12px; padding: 10px 16px; border: 0; border-radius: 10px; background: #2563eb; color: #fff; font-weight: 600; cursor: pointer; }
                .ok { color: #047857 !important; margin-bottom: 12px !important; }
              </style>
            """);
    }

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
