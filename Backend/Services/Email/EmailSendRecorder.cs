using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// Writes down what happened to a sent email.
///
/// <para>
/// Extracted from the dispatch worker because it is the same four writes every time — the
/// recipient row, the conversation thread, the message, and the activity log — and having them in
/// one place is what keeps them consistent. Notably it is also what the WhatsApp path does, via
/// ChatService and RecordMessageActivityAsync, so an email send shows up in the same inbox and
/// the same activity log rather than in a parallel world of its own.
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
    /// Records a send that failed before a message existed — a missing connection, an
    /// undecryptable credential, a template that could not be rendered.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="RecordFailedAsync"/> because there is no EmailMessage and no
    /// provider result to describe: the failure happened on the way to building one. Without this
    /// those failures reached no operator-visible surface at all — the queue row was the only
    /// evidence, and nothing in the product reads it.
    /// </remarks>
    Task RecordJobFailureAsync(
        CampaignContact recipient,
        string reason,
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

        await WriteActivityLogAsync(campaign, recipient, message, result, providerName, isSuccess: true, ct);
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

        await WriteActivityLogAsync(campaign, recipient, message, result, providerName, isSuccess: false, ct);
        await _dbContext.SaveChangesAsync(ct);

        // ── 2. Normalized FAILED event ────────────────────────────────────────────────────
        await _eventProcessor.ProcessAsync(
            kind:              EmailEventKind.Failed,
            idempotencyKey:    $"smtp:fail:{recipient.Id}",
            source:            providerName,
            campaignId:        campaign.Id,
            campaignContactId: recipient.Id,
            messageId:         message.MessageId,
            recipientAddress:  message.To.FirstOrDefault()?.Address,
            occurredAt:        DateTime.UtcNow,
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

    /// <summary>
    /// Writes the message-traffic log row, the same table the WhatsApp path writes to.
    ///
    /// <para>
    /// Reusing it rather than adding an email-only log means the Setup → Activity Log screen shows
    /// both channels' traffic with no change, and reporting does not have to union two tables.
    /// <c>WhatsAppMessageId</c> carries the provider's id — the column name is WhatsApp-era, and
    /// renaming a column the activity viewer and existing queries read is not worth the risk.
    /// </para>
    /// </summary>
    private Task WriteActivityLogAsync(
        Campaign campaign,
        CampaignContact recipient,
        EmailMessage message,
        EmailSendResult result,
        string providerName,
        bool isSuccess,
        CancellationToken ct)
    {
        _dbContext.MessageActivityLogs.Add(new MessageActivityLog
        {
            Category = "Campaign",
            Name = Truncate(campaign.Name, 200),
            TemplateName = Truncate(campaign.EmailTemplate?.Name, 200),
            RelationType = Truncate(recipient.Contact?.Type.ToString(), 50),
            ContactId = recipient.ContactId,

            // The phone column holds the email address for this channel. A dedicated column would
            // be cleaner, but it would also mean a migration on a log table for a value the
            // viewer already renders as "recipient".
            ContactPhone = Truncate(message.To.FirstOrDefault()?.Address, 255),

            ConnectionId = campaign.ConnectionId,
            WhatsAppMessageId = Truncate(result.ProviderMessageId, 200),
            IsSuccess = isSuccess,
            ErrorMessage = Truncate(result.ErrorMessage, 1000),

            // No HttpContext here: this runs in a background worker, so the absence of a user is
            // accurate rather than something to fill in with a placeholder.
            TriggeredBy = "Scheduler",

            // No payload is stored. Unlike the WhatsApp path, where the request body is a small
            // JSON template reference, an email request body is the whole rendered message —
            // which would put every recipient's personalised content, in full, into a log table.
            RequestPayload = null,
            ResponsePayload = null
        });

        _ = providerName;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task RecordJobFailureAsync(
        CampaignContact recipient,
        string reason,
        CancellationToken ct = default)
    {
        // Loaded rather than taken from the caller: this runs from an exception handler, where
        // the campaign may never have been read, and a log row naming neither the campaign nor
        // the recipient would not be worth writing.
        var campaign = await _dbContext.Campaigns
            .AsNoTracking()
            .Include(c => c.EmailTemplate)
            .FirstOrDefaultAsync(c => c.Id == recipient.CampaignId, ct);

        var contact = recipient.Contact ?? await _dbContext.Contacts
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == recipient.ContactId, ct);

        _dbContext.MessageActivityLogs.Add(new MessageActivityLog
        {
            Category = "Campaign",
            Name = Truncate(campaign?.Name, 200),
            TemplateName = Truncate(campaign?.EmailTemplate?.Name, 200),
            RelationType = Truncate(contact?.Type.ToString(), 50),
            ContactId = recipient.ContactId,
            ContactPhone = Truncate(contact?.Email, 255),
            ConnectionId = campaign?.ConnectionId,
            IsSuccess = false,
            ErrorMessage = Truncate(reason, 1000),
            TriggeredBy = "Scheduler",
            RequestPayload = null,
            ResponsePayload = null
        });

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogWarning(
            "Recorded a job failure for recipient {RecipientId} on campaign {CampaignId}: {Reason}",
            recipient.Id, recipient.CampaignId, reason);
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
