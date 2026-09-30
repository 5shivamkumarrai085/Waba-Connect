using System.Text;
using MimeKit;
using MimeKit.Text;
using WhatsAppCampaignApi.Helpers;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// Turns an <see cref="EmailMessage"/> into a MIME message.
///
/// <para>
/// Every send path goes through here, rather than each assembling its own message, so campaign,
/// chat, proof and test mail are byte-identical in structure: headers like <c>List-Unsubscribe</c>
/// and our correlation id cannot drift between them.
/// </para>
/// </summary>
public interface IMimeMessageBuilder
{
    MimeMessage Build(EmailMessage message);

    /// <summary>
    /// Generates a Message-ID for a new outbound message. Ours, not the provider's, because
    /// inbound replies are threaded by matching their In-Reply-To against it.
    /// </summary>
    string NewMessageId(string domain);
}

public class MimeMessageBuilder : IMimeMessageBuilder
{
    public MimeMessage Build(EmailMessage message)
    {
        var mime = new MimeMessage();

        mime.From.Add(ToMailbox(message.From));

        foreach (var to in message.To) mime.To.Add(ToMailbox(to));
        foreach (var cc in message.Cc) mime.Cc.Add(ToMailbox(cc));

        // Bcc is deliberately NOT set here.
        //
        // MimeKit writes a Bcc header when serialising, which would disclose every blind recipient
        // to everyone on the message. Bcc is carried in the SMTP envelope instead, through the
        // explicit recipients argument. Adding Bcc here would silently undo that.

        if (message.ReplyTo is { } replyTo) mime.ReplyTo.Add(ToMailbox(replyTo));

        mime.Subject = message.Subject;

        if (!string.IsNullOrWhiteSpace(message.MessageId)) mime.MessageId = message.MessageId;
        if (!string.IsNullOrWhiteSpace(message.InReplyTo)) mime.InReplyTo = message.InReplyTo;

        if (!string.IsNullOrWhiteSpace(message.ReferencesHeader))
        {
            // References is a space-separated chain. Preserving it in full is what keeps a long
            // thread from fragmenting into separate conversations in the recipient's client.
            foreach (var reference in message.ReferencesHeader.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                mime.References.Add(reference.Trim('<', '>'));
            }
        }

        var builder = new BodyBuilder();

        var html = message.HtmlBody;
        if (!string.IsNullOrWhiteSpace(html)) builder.HtmlBody = html;

        // A text alternative is always present. Sending HTML alone is one of the strongest
        // single signals a spam filter has, and it leaves the message unreadable in any client
        // with HTML disabled — so one is derived when the caller did not supply it.
        builder.TextBody = !string.IsNullOrWhiteSpace(message.TextBody)
            ? message.TextBody
            : DeriveTextFromHtml(html);

        foreach (var attachment in message.Attachments)
        {
            var contentType = ContentType.TryParse(attachment.ContentType, out var parsed)
                ? parsed
                // Falling back to a generic binary type keeps the send working; guessing a
                // specific one would be worse, since a mislabelled attachment often will not
                // open at all.
                : new ContentType("application", "octet-stream");

            if (!string.IsNullOrWhiteSpace(attachment.ContentId))
            {
                // Inline, referenced from the HTML by cid:. Goes in Linked rather than
                // Attachments so clients render it in place instead of listing it separately.
                var linked = builder.LinkedResources.Add(attachment.FileName, attachment.Content, contentType);
                linked.ContentId = attachment.ContentId;
            }
            else
            {
                builder.Attachments.Add(attachment.FileName, attachment.Content, contentType);
            }
        }

        mime.Body = builder.ToMessageBody();

        // Applied last so callers can override anything above — notably List-Unsubscribe and the
        // correlation id, which must survive whatever the body builder did.
        foreach (var (name, value) in message.Headers)
        {
            if (string.IsNullOrWhiteSpace(name) || value is null) continue;
            mime.Headers.Remove(name);
            mime.Headers.Add(name, value);
        }

        return mime;
    }

    public string NewMessageId(string domain)
    {
        // Shaped like any other Message-ID, with a random left-hand side. The domain must be one
        // we actually send from, or receiving servers treat the id as forged.
        var safeDomain = string.IsNullOrWhiteSpace(domain) ? "localhost" : domain.Trim().TrimStart('@');
        return $"<{Guid.NewGuid():N}.{DateTime.UtcNow:yyyyMMddHHmmss}@{safeDomain}>";
    }

    /// <summary>
    /// A readable plain-text rendering of the HTML body.
    ///
    /// <para>
    /// Not a general HTML-to-text converter, and not trying to be. It reuses the project's
    /// existing tag stripper and only adds the one thing that stripper cannot know about: that
    /// block-level tags are line breaks. Without that, the entire email collapses into a single
    /// unreadable paragraph, which is worse than having no text part at all.
    /// </para>
    /// </summary>
    private static string DeriveTextFromHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        var withBreaks = System.Text.RegularExpressions.Regex.Replace(
            html,
            @"<\s*(br|/p|/div|/tr|/li|/h[1-6])\s*/?\s*>",
            "\n",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        var stripped = SanitizationHelper.StripHtmlTags(withBreaks) ?? string.Empty;

        // Collapse the runs of blank lines that stripping tags leaves behind, without flattening
        // deliberate paragraph breaks.
        var lines = stripped
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(l => l.Trim());

        var result = new StringBuilder();
        var blankRun = 0;
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                if (++blankRun > 1) continue;
                result.AppendLine();
                continue;
            }

            blankRun = 0;
            result.AppendLine(line);
        }

        return result.ToString().Trim();
    }

    private static MailboxAddress ToMailbox(EmailAddress address) =>
        new(address.DisplayName ?? string.Empty, address.Address);
}
