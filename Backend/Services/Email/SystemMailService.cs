using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Options;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// System mail built from the email templates administrators manage (Setup › Email templates),
/// sent through the platform's own SMTP account (<see cref="IEmailSender"/>). Never throws: a
/// mail problem must not fail the operation that triggered it.
/// </summary>
public interface ISystemMailService
{
    /// <summary>Whether platform SMTP is configured at all.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Renders the template with <paramref name="templateKey"/> ({name} placeholders, values
    /// HTML-encoded) and sends it. False when mail is off, the template is missing or disabled, or
    /// the send failed; the reason is logged.
    /// </summary>
    Task<bool> SendTemplateAsync(string templateKey, string toAddress, string toName, IReadOnlyDictionary<string, string?> values, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed partial class SystemMailService(
    AppDbContext db, IEmailSender sender, IOptions<SmtpOptions> smtp, ILogger<SystemMailService> logger) : ISystemMailService
{
    /// <summary>Templates this service sends. Keys match the seeded system templates.</summary>
    public const string WelcomeTemplateKey = "WelcomeEmail";

    public bool IsEnabled => sender.IsEnabled;

    public async Task<bool> SendTemplateAsync(
        string templateKey, string toAddress, string toName, IReadOnlyDictionary<string, string?> values, CancellationToken ct = default)
    {
        if (!sender.IsEnabled)
        {
            logger.LogInformation("System mail '{Template}' to {Recipient} skipped: platform SMTP is not configured.", templateKey, toAddress);
            return false;
        }

        try
        {
            var template = await db.EmailTemplates.AsNoTracking()
                .Where(t => t.Key == templateKey && t.IsEnabled)
                .Select(t => new { t.Subject, t.BodyHtml })
                .FirstOrDefaultAsync(ct);
            if (template is null)
            {
                logger.LogWarning("System mail '{Template}' not sent: the template is missing or disabled.", templateKey);
                return false;
            }

            // Every template may use {site_name} and {app_url}; callers add their own values.
            var all = new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase)
            {
                ["site_name"] = values.TryGetValue("site_name", out var site) && !string.IsNullOrWhiteSpace(site) ? site : smtp.Value.FromName,
                ["app_url"] = values.TryGetValue("app_url", out var url) && !string.IsNullOrWhiteSpace(url) ? url : smtp.Value.AppBaseUrl
            };

            var subject = Fill(template.Subject, all, encode: false);
            var html = Fill(template.BodyHtml, all, encode: true);
            var text = WebUtility.HtmlDecode(Tags().Replace(Breaks().Replace(html, "\n"), string.Empty)).Trim();

            return await sender.SendAsync(toAddress, toName, subject, html, text, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "System mail '{Template}' to {Recipient} could not be prepared.", templateKey, toAddress);
            return false;
        }
    }

    private static string Fill(string source, IReadOnlyDictionary<string, string?> values, bool encode) =>
        Placeholder().Replace(source, m =>
            values.TryGetValue(m.Groups[1].Value, out var v) && v is not null
                ? (encode ? WebUtility.HtmlEncode(v) : v)
                : m.Value);

    [GeneratedRegex(@"\{([a-zA-Z0-9_]+)\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"<(br|/p|/div|/li|/h\d)\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex Breaks();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();
}
