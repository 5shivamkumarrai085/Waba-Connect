using Microsoft.EntityFrameworkCore;
using MimeKit;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// Takes a parsed inbound email and threads it into the correct Chat conversation.
/// </summary>
public interface IInboundEmailThreader
{
    /// <summary>
    /// Processes one inbound email: matches it to an existing conversation (or creates one),
    /// creates the incoming ChatMessage with EmailMessageDetail, and returns it.
    /// </summary>
    Task<ChatMessage?> ThreadInboundMessageAsync(
        MimeMessage mime,
        int connectionId,
        CancellationToken ct = default);
}

/// <inheritdoc />
public class InboundEmailThreader : IInboundEmailThreader
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<InboundEmailThreader> _logger;

    public InboundEmailThreader(AppDbContext dbContext, ILogger<InboundEmailThreader> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<ChatMessage?> ThreadInboundMessageAsync(
        MimeMessage mime,
        int connectionId,
        CancellationToken ct = default)
    {
        var senderAddress = mime.From.Mailboxes.FirstOrDefault()?.Address;
        if (string.IsNullOrWhiteSpace(senderAddress))
        {
            _logger.LogWarning("Ignoring inbound email with no From address. Subject: {Subject}", mime.Subject);
            return null;
        }

        var senderName = mime.From.Mailboxes.FirstOrDefault()?.Name;
        var messageId = mime.MessageId;
        var inReplyTo = mime.InReplyTo;
        var referencesChain = mime.References.Count > 0
            ? string.Join(" ", mime.References.Select(r => $"<{r}>"))
            : null;

        // ── 1. Thread matching ───────────────────────────────────────────────────────────────
        //
        // In order of specificity:
        // a) In-Reply-To matches a stored MessageIdHeader → same thread
        // b) Any Reference matches a stored MessageIdHeader → same thread
        // c) Sender email matches a Contact with an existing email conversation on this connection
        // d) No match → create a new contact and conversation

        ChatConversation? conversation = null;

        // (a) Match by In-Reply-To
        if (!string.IsNullOrWhiteSpace(inReplyTo))
        {
            conversation = await FindConversationByMessageIdAsync(inReplyTo, connectionId, ct);
        }

        // (b) Match by References chain
        if (conversation is null && mime.References.Count > 0)
        {
            // Walk References from newest to oldest (reversed) for a faster match
            foreach (var reference in mime.References.Reverse())
            {
                var refId = reference.Contains('<') ? reference : $"<{reference}>";
                conversation = await FindConversationByMessageIdAsync(refId, connectionId, ct);
                if (conversation is not null) break;
            }
        }

        // (c) Match by sender email + connection
        if (conversation is null)
        {
            conversation = await FindConversationBySenderAsync(senderAddress, connectionId, ct);
        }

        // (d) Create new contact + conversation
        if (conversation is null)
        {
            conversation = await CreateContactAndConversationAsync(
                senderAddress, senderName, connectionId, ct);
        }

        // ── 2. Extract body ──────────────────────────────────────────────────────────────────

        var htmlBody = mime.HtmlBody;
        var textBody = mime.TextBody;

        // Use plain text as fallback
        var preview = !string.IsNullOrWhiteSpace(textBody) ? textBody : mime.Subject ?? "(no subject)";

        // Sanitise HTML if present
        if (!string.IsNullOrWhiteSpace(htmlBody))
        {
            htmlBody = SanitizationHelper.SanitizeHtml(htmlBody);
        }

        // ── 3. Extract recipient addresses ───────────────────────────────────────────────────

        var toAddresses = string.Join(", ", mime.To.Mailboxes.Select(m => m.Address));
        var ccAddresses = mime.Cc.Mailboxes.Any()
            ? string.Join(", ", mime.Cc.Mailboxes.Select(m => m.Address))
            : null;

        // ── 4. Create the ChatMessage ────────────────────────────────────────────────────────

        var chatMessage = new ChatMessage
        {
            ConversationId = conversation.Id,
            ContactId = conversation.ContactId,
            ConnectionId = connectionId,
            Channel = MessageChannel.Email,
            Direction = ChatMessageDirection.Incoming,
            Status = ChatMessageStatus.Delivered,
            Text = Truncate(preview, 4096) ?? "(no content)",
            IsTemplate = false,
            SentAt = mime.Date != default ? mime.Date.UtcDateTime : DateTime.UtcNow,
            DeliveredAt = DateTime.UtcNow,
            ProviderMessageId = Truncate(messageId, 255),
            EmailDetail = new EmailMessageDetail
            {
                Subject = Truncate(mime.Subject, 300),
                FromAddress = Truncate(senderAddress, 255),
                FromName = Truncate(senderName, 200),
                ToAddresses = Truncate(toAddresses, 2000),
                CcAddresses = Truncate(ccAddresses, 2000),
                HtmlBody = htmlBody,
                MessageIdHeader = Truncate(messageId is not null ? $"<{messageId}>" : null, 500),
                InReplyTo = Truncate(inReplyTo, 500),
                ReferencesHeader = Truncate(referencesChain, 4000),
                HasAttachments = mime.Attachments.Any()
            }
        };

        _dbContext.ChatMessages.Add(chatMessage);

        conversation.LastMessageText = Truncate(mime.Subject ?? preview, 1024);
        conversation.LastMessageAt = DateTime.UtcNow;
        conversation.UnreadCount += 1;

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Threaded inbound email from {Sender} into conversation {ConversationId} (message {MessageId}).",
            senderAddress, conversation.Id, chatMessage.Id);

        return chatMessage;
    }

    /// <summary>
    /// Looks up a conversation by matching a Message-ID against stored EmailMessageDetail.MessageIdHeader.
    /// </summary>
    private async Task<ChatConversation?> FindConversationByMessageIdAsync(
        string messageId, int connectionId, CancellationToken ct)
    {
        // Normalise: ensure the message-id is angle-bracketed for comparison
        var normalised = messageId.Trim();
        if (!normalised.StartsWith('<')) normalised = $"<{normalised}>";
        if (!normalised.EndsWith('>')) normalised = $"{normalised}>";

        var conversationId = await _dbContext.EmailMessageDetails
            .AsNoTracking()
            .Where(d => d.MessageIdHeader == normalised)
            .Join(
                _dbContext.ChatMessages.AsNoTracking()
                    .Where(m => m.ConnectionId == connectionId && m.Channel == MessageChannel.Email),
                d => d.ChatMessageId,
                m => m.Id,
                (d, m) => m.ConversationId)
            .FirstOrDefaultAsync(ct);

        if (conversationId == 0) return null;

        return await _dbContext.ChatConversations
            .Include(c => c.Contact)
            .FirstOrDefaultAsync(c => c.Id == conversationId, ct);
    }

    /// <summary>
    /// Finds an existing email conversation for this sender address on this connection.
    /// </summary>
    private async Task<ChatConversation?> FindConversationBySenderAsync(
        string senderEmail, int connectionId, CancellationToken ct)
    {
        var normalised = senderEmail.Trim().ToLowerInvariant();

        return await _dbContext.ChatConversations
            .Include(c => c.Contact)
            .Where(c => c.ConnectionId == connectionId
                     && c.Channel == MessageChannel.Email
                     && c.Contact.Email != null
                     && c.Contact.Email.ToLower() == normalised)
            .OrderByDescending(c => c.LastMessageAt)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Creates a new Contact from the sender address and an email conversation for it.
    /// </summary>
    private async Task<ChatConversation> CreateContactAndConversationAsync(
        string senderEmail, string? senderName, int connectionId, CancellationToken ct)
    {
        // Check if a contact with this email already exists
        var existingContact = await _dbContext.Contacts
            .FirstOrDefaultAsync(c => c.Email != null
                                   && c.Email.ToLower() == senderEmail.ToLowerInvariant(), ct);

        if (existingContact is not null)
        {
            // Contact exists but has no email conversation on this connection — create one
            var conversation = new ChatConversation
            {
                ContactId = existingContact.Id,
                ConnectionId = connectionId,
                Channel = MessageChannel.Email,
                UnreadCount = 0
            };

            _dbContext.ChatConversations.Add(conversation);

            try
            {
                await _dbContext.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Lost a race: another thread created it first
                _dbContext.ChangeTracker.Clear();
                var raced = await _dbContext.ChatConversations
                    .Include(c => c.Contact)
                    .FirstOrDefaultAsync(c => c.ContactId == existingContact.Id
                                           && c.ConnectionId == connectionId
                                           && c.Channel == MessageChannel.Email, ct);
                if (raced is not null) return raced;
                throw;
            }

            conversation.Contact = existingContact;
            return conversation;
        }

        // Fully new contact
        var newContact = new Contact
        {
            Name = !string.IsNullOrWhiteSpace(senderName) ? senderName : senderEmail,
            Email = senderEmail,
            Phone = string.Empty,
            Source = "Email",
            IsActive = true
        };

        _dbContext.Contacts.Add(newContact);
        await _dbContext.SaveChangesAsync(ct);

        var newConversation = new ChatConversation
        {
            ContactId = newContact.Id,
            ConnectionId = connectionId,
            Channel = MessageChannel.Email,
            UnreadCount = 0
        };

        _dbContext.ChatConversations.Add(newConversation);
        await _dbContext.SaveChangesAsync(ct);

        newConversation.Contact = newContact;
        return newConversation;
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) ? value
        : value.Length <= maxLength ? value
        : value[..maxLength];
}
