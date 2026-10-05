using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// Writes down what happened to a sent email.
///
/// <para>
/// Extracted from the dispatch worker because it is the same writes every time — the recipient
/// row, the conversation thread and the message — and having them in one place is what keeps
/// them consistent. It is also what the WhatsApp path does via ChatService, so an email send
/// shows up in the same inbox rather than in a parallel world of its own.
/// </para>
/// </summary>
public interface IEmailSendRecorder
{
    Task RecordSentAsync(
        Campaign campaign,
        CampaignContact recipient,
        EmailMessage message,
        EmailSendResult result,
        string providerName,
        CancellationToken ct = default);

    Task RecordFailedAsync(
        Campaign campaign,
        CampaignContact recipient,
        EmailMessage message,
        EmailSendResult result,
        string providerName,
        CancellationToken ct = default);

    /// <summary>
    /// Moves a campaign to its terminal status once no recipient is still pending, and refreshes
    /// its counters.
    /// </summary>
    /// <remarks>
    /// Safe to call after every recipient and from several workers at once: it is one conditional
    /// statement at the database, and a no-op for a campaign that is not finished or has already
    /// been finalised.
    /// </remarks>
    Task TryFinalizeCampaignAsync(int campaignId, CancellationToken ct = default);
}

/// <inheritdoc />
public class EmailSendRecorder : IEmailSendRecorder
{
    private readonly AppDbContext _dbContext;
    private readonly ICampaignEmailEventProcessor _eventProcessor;
    private readonly ILogger<EmailSendRecorder> _logger;

    public EmailSendRecorder(
        AppDbContext dbContext,
        ICampaignEmailEventProcessor eventProcessor,
        ILogger<EmailSendRecorder> logger)
    {
        _dbContext      = dbContext;
        _eventProcessor = eventProcessor;
        _logger         = logger;
    }

    public async Task RecordSentAsync(
        Campaign campaign,
        CampaignContact recipient,
        EmailMessage message,
        EmailSendResult result,
        string providerName,
        CancellationToken ct = default)
    {
        // ── 1. Conversation thread + activity log (existing responsibilities) ──────────────
        recipient.Status = MessageStatus.Sent;
        recipient.SentAt ??= DateTime.UtcNow;
        recipient.ProviderMessageId = result.ProviderMessageId;
        recipient.ErrorMessage = null;

        var chatMessage = await WriteChatMessageAsync(campaign, recipient, message, ChatMessageStatus.Sent, ct);
        chatMessage.ProviderMessageId = result.ProviderMessageId;

        await _dbContext.SaveChangesAsync(ct);

        // ── 2. Normalized SENT event (counter increment + real-time) ─────────────────────
        // Idempotency key: smtp:send:{campaignContactId} — stable, exactly one per recipient send
        await _eventProcessor.ProcessAsync(
            kind:              EmailEventKind.Sent,
            idempotencyKey:    $"smtp:send:{recipient.Id}",
            source:            providerName,
            campaignId:        campaign.Id,
            campaignContactId: recipient.Id,
            messageId:         message.MessageId,
            providerMessageId: result.ProviderMessageId,
            recipientAddress:  message.To.FirstOrDefault()?.Address,
            occurredAt:        DateTime.UtcNow,
            ct:                ct);
    }

    public async Task RecordFailedAsync(
        Campaign campaign,
        CampaignContact recipient,
        EmailMessage message,
        EmailSendResult result,
        string providerName,
        CancellationToken ct = default)
    {
        // ── 1. Conversation thread + activity log ──────────────────────────────────────────
        recipient.Status = MessageStatus.Failed;
        recipient.ErrorMessage = Truncate(result.ErrorMessage ?? "The send failed.", 500);
        recipient.SendAttemptedAt = null;

        var chatMessage = await WriteChatMessageAsync(campaign, recipient, message, ChatMessageStatus.Failed, ct);
        chatMessage.ErrorMessage = Truncate(result.ErrorMessage, 1000);

        await _dbContext.SaveChangesAsync(ct);

        // ── 2. Normalized FAILED (or, for a refused mailbox, BOUNCED) event ───────────────
        var bounced = result.IsPermanentBounce;
        await _eventProcessor.ProcessAsync(
            kind:              bounced ? EmailEventKind.Bounced : EmailEventKind.Failed,
            idempotencyKey:    bounced ? $"smtp:bounce:{recipient.Id}" : $"smtp:fail:{recipient.Id}",
            source:            providerName,
            campaignId:        campaign.Id,
            campaignContactId: recipient.Id,
            messageId:         message.MessageId,
            recipientAddress:  message.To.FirstOrDefault()?.Address,
            occurredAt:        DateTime.UtcNow,
            bounceType:        bounced ? EmailBounceType.Permanent : null,
            bounceSubType:     bounced ? result.ErrorCode : null,
            diagnosticCode:    bounced ? result.ErrorMessage : null,
            ct:                ct);
    }

    /// <summary>
    /// Threads the sent message into the unified inbox.
    /// </summary>
    private async Task<ChatMessage> WriteChatMessageAsync(
        Campaign campaign,
        CampaignContact recipient,
        EmailMessage message,
        ChatMessageStatus status,
        CancellationToken ct)
    {
        var conversation = await GetOrCreateEmailConversationAsync(recipient.ContactId, campaign.ConnectionId, ct);

        // The plain-text rendering goes in Text because that is what the conversation list, the
        // search and the reporting queries already read. The subject and HTML live on the side
        // table, so nothing that already queries ChatMessage has to change.
        var preview = !string.IsNullOrWhiteSpace(message.TextBody)
            ? message.TextBody
            : message.Subject;

        var chatMessage = new ChatMessage
        {
            ConversationId = conversation.Id,
            ContactId = recipient.ContactId,
            ConnectionId = campaign.ConnectionId,
            CampaignId = campaign.Id,
            CampaignContactId = recipient.Id,
            Channel = MessageChannel.Email,
            Direction = ChatMessageDirection.Outgoing,
            Status = status,
            Text = Truncate(preview, 4096) ?? message.Subject,
            IsTemplate = true,
            SentAt = status == ChatMessageStatus.Sent ? DateTime.UtcNow : null,
            EmailDetail = new EmailMessageDetail
            {
                Subject = Truncate(message.Subject, 300),
                FromAddress = Truncate(message.From.Address, 255),
                FromName = Truncate(message.From.DisplayName, 200),
                ToAddresses = Truncate(string.Join(", ", message.To.Select(t => t.Address)), 2000),
                CcAddresses = message.Cc.Count == 0
                    ? null
                    : Truncate(string.Join(", ", message.Cc.Select(t => t.Address)), 2000),

                // Bcc is recorded here but never written into the MIME — see MimeMessageBuilder.
                // Keeping it on our own row is how an operator can still see who was copied.
                BccAddresses = message.Bcc.Count == 0
                    ? null
                    : Truncate(string.Join(", ", message.Bcc.Select(t => t.Address)), 2000),

                ReplyTo = Truncate(message.ReplyTo?.Address, 255),
                HtmlBody = message.HtmlBody,

                // Our own Message-ID. This is the column an inbound reply's In-Reply-To is
                // matched against, which is what makes reply threading deterministic.
                MessageIdHeader = Truncate(message.MessageId, 500),
                HasAttachments = message.Attachments.Count > 0
            }
        };

        _dbContext.ChatMessages.Add(chatMessage);

        conversation.LastMessageText = Truncate(message.Subject, 1024);
        conversation.LastMessageAt = DateTime.UtcNow;

        return chatMessage;
    }

    /// <summary>
    /// Finds or creates the email thread for a contact on a connection.
    ///
    /// <para>
    /// Deliberately not routed through <c>IChatService.GetOrCreateConversationAsync</c>: that
    /// method knows nothing about channels and would resolve — or create — the contact's WhatsApp
    /// thread, merging email into it. Extending it would mean changing a method the WhatsApp send
    /// path depends on, so the email channel resolves its own instead.
    /// </para>
    /// </summary>
    private async Task<ChatConversation> GetOrCreateEmailConversationAsync(
        int contactId,
        int? connectionId,
        CancellationToken ct)
    {
        var existing = await _dbContext.ChatConversations
            .FirstOrDefaultAsync(c => c.ContactId == contactId
                                   && c.ConnectionId == connectionId
                                   && c.Channel == MessageChannel.Email, ct);

        if (existing is not null) return existing;

        var conversation = new ChatConversation
        {
            ContactId = contactId,
            ConnectionId = connectionId,
            Channel = MessageChannel.Email,
            UnreadCount = 0
        };

        _dbContext.ChatConversations.Add(conversation);

        try
        {
            // Saved immediately so the row has an id for the message's foreign key.
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost a race with a concurrent dispatch worker for the same contact. The unique
            // index on {ContactId, ConnectionId, Channel} did its job; the other worker's row is
            // just as good, so it is adopted.
            _dbContext.ChangeTracker.Clear();

            var raced = await _dbContext.ChatConversations
                .FirstOrDefaultAsync(c => c.ContactId == contactId
                                       && c.ConnectionId == connectionId
                                       && c.Channel == MessageChannel.Email, ct);

            if (raced is null) throw;

            _logger.LogDebug(
                "Adopted a concurrently created email conversation for contact {ContactId}.", contactId);
            return raced;
        }

        return conversation;
    }

    /// <inheritdoc />
    public async Task TryFinalizeCampaignAsync(int campaignId, CancellationToken ct = default)
    {
        var status = await CampaignFinalizer.TryFinalizeAsync(_dbContext, campaignId, ct);
        if (status is not null)
        {
            _logger.LogInformation("Campaign {CampaignId} finished as {Status}.", campaignId, status);
        }
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) ? value
        : value.Length <= maxLength ? value
        : value[..maxLength];
}
