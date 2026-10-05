using System.Text.RegularExpressions;
using MimeKit;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Catalogs;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// Reads the plain-text delivery reports most shared hosts send (Exim and cPanel, qmail's
/// "failure notice", older Postfix) — the ones that are not RFC 3464 multipart/report and so
/// carry no machine-readable fields. Without this they look like ordinary auto-replies and every
/// bounce is silently dropped.
///
/// <para>
/// Deliberately conservative. A message is read as a report only when it comes from a mail
/// daemon or has a report subject, and it becomes a bounce only with evidence of a permanent
/// failure (a 5.x.x / 5xx status or wording such as "permanent error"); 4.x.x is transient. A
/// person's reply that happens to contain "550" never matches. Every pattern lives in
/// <see cref="BounceCatalog"/>.
/// </para>
/// </summary>
public static class PlainTextBounceParser
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex EnhancedStatus = new(@"(?<![\d.])([245])\.\d{1,3}\.\d{1,3}(?![\d.])", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex BasicStatus = new(@"(?<![\d.])([45])\d\d(?=[\s-])", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex Address = new(@"[A-Z0-9._%+'-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.Compiled | RegexOptions.IgnoreCase, RegexTimeout);
    private static readonly Regex ReturnedMessageId = new(@"(?im)^\s*Message-ID:\s*(<[^>\s]+>)", RegexOptions.Compiled, RegexTimeout);

    public static BounceInfo? TryParse(MimeMessage mime)
    {
        if (!LooksLikeReport(mime)) return null;

        var body = mime.TextBody ?? StripTags(mime.HtmlBody) ?? string.Empty;
        var (report, returned) = Split(body);

        // Host lines carry IP addresses ("[10.4.5.6]"), hence the digit/dot lookarounds above.
        var token = EnhancedStatus.Match(report) is { Success: true } enhanced ? enhanced.Value
            : BasicStatus.Match(report) is { Success: true } basic ? basic.Value
            : string.Empty;
        var status = token.Length == 0 ? string.Empty : token.Contains('.') ? token : $"{token[0]}.0.0";
        var permanentWords = BounceCatalog.PermanentPhrases.Any(p => report.Contains(p, StringComparison.OrdinalIgnoreCase));

        EmailBounceType type;
        if (status.StartsWith('5') || (status.Length == 0 && permanentWords)) type = EmailBounceType.Permanent;
        else if (status.StartsWith('4')) type = EmailBounceType.Transient;
        else return null;   // no evidence either way — not something to suppress an address over

        var recipient = FailedRecipient(mime, report);
        if (recipient is null) return null;

        return new BounceInfo(
            FinalRecipient: recipient,
            StatusCode: status.Length > 0 ? status : "5.0.0",
            DiagnosticText: Diagnostic(report, token),
            BounceType: type,
            OriginalMessageId: OriginalMessageId(mime, returned),
            ReceivedAt: mime.Date != default ? mime.Date.UtcDateTime : DateTime.UtcNow);
    }

    private static bool LooksLikeReport(MimeMessage mime)
    {
        var from = mime.From.Mailboxes.FirstOrDefault()?.Address ?? string.Empty;
        var local = from.Contains('@') ? from[..from.IndexOf('@')] : from;
        if (BounceCatalog.SenderLocalParts.Any(p => local.Equals(p, StringComparison.OrdinalIgnoreCase))) return true;
        var subject = mime.Subject ?? string.Empty;
        return BounceCatalog.SubjectPatterns.Any(p => subject.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The report is the text above the returned copy; addresses below it are the original's headers.</summary>
    private static (string Report, string Returned) Split(string body)
    {
        var cut = BounceCatalog.ReturnedCopyMarkers
            .Select(m => body.IndexOf(m, StringComparison.OrdinalIgnoreCase))
            .Where(i => i >= 0)
            .DefaultIfEmpty(-1)
            .Min();
        if (cut < 0) return (body, string.Empty);
        var lineStart = body.LastIndexOf('\n', cut) + 1;
        return (body[..lineStart], body[lineStart..]);
    }

    /// <summary>The first address in the report that is not the report's own sender or recipient (us).</summary>
    private static string? FailedRecipient(MimeMessage mime, string report)
    {
        var ignore = mime.From.Mailboxes.Concat(mime.To.Mailboxes).Concat(mime.Sender is null ? [] : [mime.Sender])
            .Select(m => m.Address.ToLowerInvariant())
            .ToHashSet();
        return Address.Matches(report)
            .Select(m => m.Value.Trim('.', '\'').ToLowerInvariant())
            .FirstOrDefault(a => !ignore.Contains(a) && !BounceCatalog.SenderLocalParts.Any(p => a.StartsWith(p + "@", StringComparison.Ordinal)));
    }

    private static string? OriginalMessageId(MimeMessage mime, string returned)
    {
        foreach (var part in mime.BodyParts.OfType<MessagePart>())
        {
            if (!string.IsNullOrWhiteSpace(part.Message?.MessageId)) return $"<{part.Message.MessageId.Trim('<', '>')}>";
        }
        var match = ReturnedMessageId.Match(returned);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>The report line carrying the status, or the first line with a permanent-failure phrase.</summary>
    private static string Diagnostic(string report, string token)
    {
        var lines = report.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var line = (token.Length > 0 ? lines.FirstOrDefault(l => l.Contains(token, StringComparison.Ordinal)) : null)
            ?? lines.FirstOrDefault(l => BounceCatalog.PermanentPhrases.Any(p => l.Contains(p, StringComparison.OrdinalIgnoreCase)))
            ?? string.Empty;
        return line.Length > 2000 ? line[..2000] : line;
    }

    private static string? StripTags(string? html) =>
        html is null ? null : Regex.Replace(html, "<[^>]+>", " ", RegexOptions.None, RegexTimeout);
}
