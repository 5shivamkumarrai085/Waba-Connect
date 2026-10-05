using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// Sends a single email from the inbox — a reply, a reply-all, or a forward.
///
/// <para>
/// Distinct from the campaign path, which exists to send the same template to many recipients
/// through a queue with retries and rate limiting. This is one operator, one message, sent while
/// they wait: queueing it would mean the composer could not tell them whether it went, and the
/// retry semantics a campaign needs are wrong for a message someone is watching send.
/// </para>
/// <para>
/// It shares everything that matters though — the same <see cref="IEmailProvider"/>, the same
/// MIME builder, and the same conversation and message rows — so a reply threads into the inbox
/// beside the campaign mail it is replying to.
/// </para>
/// </summary>
public interface IEmailReplyService
{
    Task<EmailReplyResult> SendAsync(int conversationId, EmailReplyRequest request, CancellationToken ct = default);
}

/// <param name="InReplyToMessageId">
/// The inbox message being replied to, when there is one. Its Message-ID becomes this message's
/// In-Reply-To, which is what makes a mail client thread the two together.
/// </param>
public record EmailReplyRequest(
    string Subject,
    string BodyHtml,
    IReadOnlyList<string> To,
    IReadOnlyList<string>? Cc = null,
    IReadOnlyList<string>? Bcc = null,
    int? InReplyToMessageId = null,
    IReadOnlyList<EmailReplyAttachment>? Attachments = null);

/// <summary>One file attachment coming from the inbox composer.</summary>
public record EmailReplyAttachment(
    string FileName,
    string ContentType,
    byte[] Content);

public record EmailReplyResult(bool Success, string? Message, int? ChatMessageId);

/// <inheritdoc />
public class EmailReplyService : IEmailReplyService
{
    private readonly AppDbContext _dbContext;
    private readonly IEmailProviderFactory _providerFactory;
    private readonly IMimeMessageBuilder _mimeBuilder;
    private readonly IEmailSuppressionService _suppression;
    private readonly IAuditService _auditService;
    private readonly ILogger<EmailReplyService> _logger;

    public EmailReplyService(
        AppDbContext dbContext,
        IEmailProviderFactory providerFactory,
        IMimeMessageBuilder mimeBuilder,
        IEmailSuppressionService suppression,
        IAuditService auditService,
        ILogger<EmailReplyService> logger)
    {
        _dbContext = dbContext;
        _providerFactory = providerFactory;
        _mimeBuilder = mimeBuilder;
        _suppression = suppression;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<EmailReplyResult> SendAsync(
        int conversationId,
        EmailReplyRequest request,
        CancellationToken ct = default)
    {
        var conversation = await _dbContext.ChatConversations
            .Include(c => c.Contact)
            .FirstOrDefaultAsync(c => c.Id == conversationId, ct)
            ?? throw new KeyNotFoundException($"Conversation {conversationId} not found.");

        if (conversation.Channel != MessageChannel.Email)
        {
            throw new InvalidOperationException("That conversation is not an email thread.");
        }

        if (conversation.ConnectionId is not { } connectionId)
        {
            throw new InvalidOperationException("This email thread has no connection to send through.");
        }

        var recipients = Clean(request.To);
        if (recipients.Count == 0)
        {
            return new EmailReplyResult(false, "Add at least one recipient.", null);
        }

        // Checked before sending, not after. Mailing an address that asked not to hear from us is
        // the one failure that cannot be undone by a retry.
        foreach (var address in recipients)
        {
            if (await _suppression.IsSuppressedAsync(address, connectionId, ct))
            {
                return new EmailReplyResult(false,
                    $"{address} has unsubscribed or previously bounced, so it cannot be emailed.", null);
            }
        }

        var (provider, providerContext) = await _providerFactory.ResolveAsync(connectionId, ct);

        var sender = await _dbContext.EmailSenderIdentities
            .Include(s => s.EmailConfiguration)
            .Where(s => s.EmailConfiguration!.ConnectionId == connectionId && s.IsActive)
            .OrderByDescending(s => s.IsDefault)
            .FirstOrDefaultAsync(ct);

        if (sender is null)
        {
            return new EmailReplyResult(false,
                "This connection has no active sender address to send from.", null);
        }

        // The thread's own headers, so the recipient's mail client files this under the existing
        // conversation instead of starting a new one.
        var (inReplyTo, parentReferences) = await ResolveThreadingHeadersAsync(
            _dbContext, conversationId, request.InReplyToMessageId, ct);

        var fromDomain = sender.EmailAddress.Contains('@')
            ? sender.EmailAddress.Split('@')[1]
            : "localhost";

        // Decode the base64 attachments coming from the composer.
        var attachments = (request.Attachments ?? [])
            .Select(a => new EmailAttachment(
                FileName: string.IsNullOrWhiteSpace(a.FileName) ? "attachment" : a.FileName,
                ContentType: string.IsNullOrWhiteSpace(a.ContentType) ? "application/octet-stream" : a.ContentType,
                Content: a.Content))
            .ToList();

        var message = new EmailMessage
        {
            From = new EmailAddress(sender.EmailAddress, sender.DisplayName),
            ReplyTo = string.IsNullOrWhiteSpace(sender.ReplyTo) ? null : new EmailAddress(sender.ReplyTo),
            To = recipients.Select(a => new EmailAddress(a)).ToList(),
            Cc = Clean(request.Cc).Select(a => new EmailAddress(a)).ToList(),
            Bcc = Clean(request.Bcc).Select(a => new EmailAddress(a)).ToList(),
            Subject = request.Subject.Trim(),
            Attachments = attachments,

            // Generated by us rather than the provider, so it can be stored and matched against
            // inbound In-Reply-To headers deterministically.
            MessageId = _mimeBuilder.NewMessageId(fromDomain),

            // Sanitised here rather than trusted: the body arrives from a rich-text editor in the
            // browser, so it is user input on its way into other people's mail clients.
            HtmlBody = SanitizationHelper.SanitizeHtml(request.BodyHtml),
            InReplyTo = inReplyTo,
            ReferencesHeader = BuildReferencesChain(parentReferences, inReplyTo)
        };

        // No List-Unsubscribe. These headers belong on bulk mail; putting them on a one-to-one
        // reply invites the recipient to unsubscribe from a conversation they started.

        var result = await provider.SendAsync(message, providerContext, ct);

        if (!result.Success)
        {
            _logger.LogWarning("Reply on conversation {ConversationId} failed: {Error}",
                conversationId, result.ErrorMessage);

            return new EmailReplyResult(false, result.ErrorMessage ?? "The message could not be sent.", null);
        }

        var chatMessage = await RecordAsync(conversation, message, result, ct);

        await _auditService.LogAsync(
            "EmailReply.Sent", "Messaging",
            $"Replied to {string.Join(", ", recipients)} on conversation {conversationId}.",
            // Filed under the conversation, so its Activity tab can list every reply in it.
            nameof(ChatConversation), conversationId.ToString());

        return new EmailReplyResult(true, "Sent.", chatMessage.Id);
    }

    /// <summary>
    /// The threading headers for the message being replied to: its Message-ID (which becomes our
    /// In-Reply-To) and its existing References chain (which we extend).
    /// </summary>
    /// <remarks>
    /// Falls back to the newest message in the thread when the caller did not name one, which is
    /// what "Reply" means from a thread view.
    /// </remarks>
    public static async Task<(string? InReplyTo, string? References)> ResolveThreadingHeadersAsync(
        AppDbContext db, int conversationId, int? messageId, CancellationToken ct)
    {
        // No message to answer means New Email: a new thread. It used to fall back to the latest
        // message, so a "new" email arrived threaded under an old conversation in Gmail.
        if (messageId is not { } id) return (null, null);

        var headers = await db.ChatMessages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId && m.EmailDetail != null && m.Id == id)
            .Select(m => new { m.EmailDetail!.MessageIdHeader, m.EmailDetail.ReferencesHeader })
            .FirstOrDefaultAsync(ct);

        return (headers?.MessageIdHeader, headers?.ReferencesHeader);
    }

    /// <summary>
    /// Builds the full RFC 2822 References chain: the parent's existing chain plus the parent's
    /// own Message-ID appended. This is what keeps a long thread from fragmenting into separate
    /// conversations in the recipient's mail client.
    /// </summary>
    private static string? BuildReferencesChain(string? parentReferences, string? parentMessageId)
    {
        if (string.IsNullOrWhiteSpace(parentMessageId)) return null;

        return string.IsNullOrWhiteSpace(parentReferences)
            ? parentMessageId
            : $"{parentReferences} {parentMessageId}";
    }

    /// <summary>
    /// Threads the sent reply into the inbox, in the same shape the campaign path records its own
    /// sends — so the thread reads as one conversation rather than two sources of message.
    /// </summary>
    private async Task<ChatMessage> RecordAsync(
        ChatConversation conversation,
        EmailMessage message,
        EmailSendResult result,
        CancellationToken ct)
    {
        var preview = !string.IsNullOrWhiteSpace(message.TextBody) ? message.TextBody : message.Subject;

        var chatMessage = new ChatMessage
        {
            ConversationId = conversation.Id,
            ContactId = conversation.ContactId,
            ConnectionId = conversation.ConnectionId,
            Channel = MessageChannel.Email,
            Direction = ChatMessageDirection.Outgoing,
            Status = ChatMessageStatus.Sent,
            Text = Truncate(preview, 4096) ?? message.Subject,

            // Not a template: this is something an operator typed, and the inbox distinguishes
            // the two when it decides what may be edited or resent.
            IsTemplate = false,
            SentAt = DateTime.UtcNow,
            ProviderMessageId = Truncate(result.ProviderMessageId, 255),
            EmailDetail = new EmailMessageDetail
            {
                Subject = Truncate(message.Subject, 300),
                FromAddress = Truncate(message.From.Address, 255),
                FromName = Truncate(message.From.DisplayName, 200),
                ToAddresses = Truncate(string.Join(", ", message.To.Select(t => t.Address)), 2000),
                CcAddresses = message.Cc.Count == 0
                    ? null
                    : Truncate(string.Join(", ", message.Cc.Select(t => t.Address)), 2000),
                BccAddresses = message.Bcc.Count == 0
                    ? null
                    : Truncate(string.Join(", ", message.Bcc.Select(t => t.Address)), 2000),
                ReplyTo = Truncate(message.ReplyTo?.Address, 255),
                HtmlBody = message.HtmlBody,
                MessageIdHeader = Truncate(message.MessageId, 500),
                InReplyTo = Truncate(message.InReplyTo, 500),
                ReferencesHeader = Truncate(message.ReferencesHeader, 4000),
                HasAttachments = message.Attachments.Count > 0
            }
        };

        _dbContext.ChatMessages.Add(chatMessage);

        conversation.LastMessageText = Truncate(message.Subject, 1024);
        conversation.LastMessageAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(ct);
        return chatMessage;
    }

    /// <summary>Trimmed, de-duplicated, and with the blanks a comma-separated field leaves behind.</summary>
    private static List<string> Clean(IReadOnlyList<string>? addresses) =>
        (addresses ?? [])
            .Select(a => a?.Trim() ?? string.Empty)
            .Where(a => a.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) ? value
        : value.Length <= maxLength ? value
        : value[..maxLength];
}
