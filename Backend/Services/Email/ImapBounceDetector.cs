using MimeKit;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// Detects and parses Delivery Status Notification (DSN) / bounce messages from IMAP inboxes.
///
/// <para>
/// When an email cannot be delivered, the receiving server returns a DSN report to the sender's
/// mailbox. These arrive as regular emails with Content-Type: multipart/report;
/// report-type=delivery-status. This class detects them, extracts the final recipient, status
/// code, and diagnostic text, and returns a structured result that the event pipeline can use
/// to record a BOUNCED event.
/// </para>
///
/// <para>
/// SMTP only tells us the next hop accepted the message. A DSN/bounce in the mailbox is the only
/// way to learn that delivery ultimately failed, so the connection's mailbox is polled for them.
/// </para>
/// </summary>
public static class ImapBounceDetector
{
    /// <summary>
    /// Returns true if the given MIME message is a DSN/bounce report.
    /// </summary>
    public static bool IsBounceReport(MimeMessage mime)
    {
        // RFC 3464: the top-level content type is multipart/report with report-type=delivery-status
        if (mime.Body is not MultipartReport report) return false;
        return string.Equals(report.ReportType, "delivery-status", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Attempts to parse a DSN report. Returns a <see cref="BounceInfo"/> if the message is a
    /// deliverable-status notification, or null if it is not a recognisable bounce.
    /// </summary>
    public static BounceInfo? TryParse(MimeMessage mime)
    {
        if (!IsBounceReport(mime)) return null;

        if (mime.Body is not MultipartReport report) return null;

        // The delivery-status part (RFC 3464 §2) carries the machine-readable status
        var dsnPart = report.OfType<MessageDeliveryStatus>().FirstOrDefault();
        if (dsnPart is null) return null;

        var finalRecipient = string.Empty;
        var statusCode     = string.Empty;
        var diagnostic     = string.Empty;
        var action         = string.Empty;
        var originalMsgId  = string.Empty;

        // Per-recipient fields live in the per-recipient blocks (index ≥ 1)
        foreach (var block in dsnPart.StatusGroups.Skip(1))
        {
            // Final-Recipient: rfc822; user@example.com
            var recipientHeader = block["Final-Recipient"];
            if (recipientHeader is not null)
            {
                var parts = recipientHeader.Split(';', 2, StringSplitOptions.TrimEntries);
                finalRecipient = parts.Length >= 2 ? parts[1] : recipientHeader.Trim();
            }

            // Status: 5.1.1 (permanent failure) or 4.x.x (transient)
            statusCode = block["Status"]?.Trim() ?? string.Empty;

            // Diagnostic-Code: smtp; 550 5.1.1 The email account that you tried to reach does not exist
            var diagHeader = block["Diagnostic-Code"];
            if (diagHeader is not null)
            {
                var parts = diagHeader.Split(';', 2, StringSplitOptions.TrimEntries);
                diagnostic = parts.Length >= 2 ? parts[1] : diagHeader.Trim();
            }

            // Action: failed | delayed | delivered | relayed | expanded
            action = block["Action"]?.Trim() ?? string.Empty;

            // Take the first non-empty block
            if (!string.IsNullOrEmpty(finalRecipient)) break;
        }

        // Message-ID of the original sent message, most reliable source first:
        // 1. The returned copy of the original message (RFC 3462 third part) — either the full
        //    message (message/rfc822) or its headers only (text/rfc822-headers). Almost every MTA
        //    includes one, and it is our own Message-ID verbatim.
        // 2. Original-Message-ID in the per-message block, which some MTAs add.
        // 3. In-Reply-To on the report itself, which a few MTAs set.
        // Reporting-MTA is deliberately not used: it names the reporting server, not a message.
        originalMsgId = ExtractReturnedMessageId(report) ?? string.Empty;

        if (string.IsNullOrEmpty(originalMsgId))
        {
            var msgBlock = dsnPart.StatusGroups.FirstOrDefault();
            originalMsgId = msgBlock?["Original-Message-ID"]?.Trim() ?? string.Empty;
        }

        if (string.IsNullOrEmpty(originalMsgId) && mime.InReplyTo is not null)
            originalMsgId = mime.InReplyTo;

        // Classify: a 5.x.x status code or Action: failed = permanent bounce
        // A 4.x.x = transient bounce (still worth recording but not necessarily suppressing)
        var bounceType = ClassifyBounce(statusCode, action);

        return new BounceInfo(
            FinalRecipient:  finalRecipient.Trim().ToLowerInvariant(),
            StatusCode:      statusCode,
            DiagnosticText:  diagnostic.Length > 2000 ? diagnostic[..2000] : diagnostic,
            BounceType:      bounceType,
            OriginalMessageId: NormaliseMsgId(originalMsgId),
            ReceivedAt:      mime.Date != default ? mime.Date.UtcDateTime : DateTime.UtcNow
        );
    }

    /// <summary>Reads the Message-ID from the copy of the original message returned in the report.</summary>
    private static string? ExtractReturnedMessageId(MultipartReport report)
    {
        foreach (var part in report)
        {
            if (part is MessagePart { Message: { } returned } && !string.IsNullOrWhiteSpace(returned.MessageId))
                return returned.MessageId;

            if (part is TextPart text && text.ContentType.IsMimeType("text", "rfc822-headers"))
            {
                try
                {
                    using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text.Text ?? string.Empty));
                    var headers = HeaderList.Load(stream);
                    var id = headers[HeaderId.MessageId];
                    if (!string.IsNullOrWhiteSpace(id)) return id.Trim();
                }
                catch (FormatException)
                {
                    // Malformed headers block — fall through to the other sources.
                }
            }
        }

        return null;
    }

    private static EmailBounceType ClassifyBounce(string statusCode, string action)
    {
        if (action.Equals("failed", StringComparison.OrdinalIgnoreCase))
        {
            // Status code 5.x.x = permanent; 4.x.x = transient
            return statusCode.StartsWith("4.") ? EmailBounceType.Transient : EmailBounceType.Permanent;
        }

        if (statusCode.StartsWith("5.")) return EmailBounceType.Permanent;
        if (statusCode.StartsWith("4.")) return EmailBounceType.Transient;

        return EmailBounceType.Undetermined;
    }

    private static string? NormaliseMsgId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var trimmed = id.Trim();
        if (!trimmed.StartsWith('<')) trimmed = $"<{trimmed}>";
        if (!trimmed.EndsWith('>')) trimmed = $"{trimmed}>";
        return trimmed;
    }
}

/// <summary>Result of parsing a DSN bounce report.</summary>
public sealed record BounceInfo(
    string FinalRecipient,
    string StatusCode,
    string DiagnosticText,
    EmailBounceType BounceType,
    string? OriginalMessageId,
    DateTime ReceivedAt
);
