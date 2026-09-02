using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Activity;
using WhatsAppCampaignApi.Models.DTOs.Chat;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services;

public class ChatService : IChatService
{
    private readonly AppDbContext _dbContext;
    private readonly IWhatsAppService _whatsAppService;
    private readonly ITemplateService _templateService;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _auditService;
    private readonly IOmniSettingsService _settings;

    public ChatService(
        AppDbContext dbContext,
        IWhatsAppService whatsAppService,
        ITemplateService templateService,
        ICurrentUserService currentUser,
        IAuditService auditService,
        IOmniSettingsService settings)
    {
        _dbContext = dbContext;
        _whatsAppService = whatsAppService;
        _templateService = templateService;
        _currentUser = currentUser;
        _auditService = auditService;
        _settings = settings;
    }

    public async Task<List<ChatAccountResponse>> GetAccountsAsync(int? connectionId = null)
    {
        var query = _dbContext.WabaPhoneNumbers
            .AsNoTracking()
            .Include(p => p.Connection)
            .AsQueryable();

        if (connectionId.HasValue)
        {
            query = query.Where(p => p.ConnectionId == connectionId.Value);
        }

        var accounts = await query
            .OrderBy(p => p.Id)
            .ToListAsync();

        return accounts.Select(MapAccount).ToList();
    }

    /// <summary>
    /// The contact-owner name this caller is limited to, or null when they may see everything.
    ///
    /// <para>
    /// Enforced here rather than in the UI. Hiding rows in the browser while the API still returns
    /// them is not a restriction — anyone can read the response — so every chat read path asks this
    /// question and applies the answer to its query.
    /// </para>
    /// <para>
    /// Administrators are exempt: the setting exists to keep agents in their own lane, not to lock
    /// the account's owner out of their own inbox.
    /// </para>
    /// <para>
    /// The comparison is by display name because that is what <see cref="Contact.AssignedTo"/>
    /// holds — the contact form writes the user's name into it, not their id. An agent whose
    /// account has no name claim gets an empty restriction, which matches nothing: failing closed
    /// is the only safe direction for a visibility rule.
    /// </para>
    /// </summary>
    private async Task<string?> GetAgentRestrictionAsync()
    {
        if (!await _settings.GetFlagAsync("supportAgent.restrictChatAccess")) return null;
        if (_currentUser.IsAdministrator) return null;
        if (!_currentUser.IsAuthenticated) return null;

        return _currentUser.UserName ?? string.Empty;
    }

    /// <summary>
    /// Throws unless this caller may see the given conversation.
    ///
    /// Used by the endpoints that take a conversation id and act on it — reading its messages,
    /// marking it read, sending into it, deleting from it. Each of those is a way to reach a
    /// conversation without going through the list, so each needs the check the list already has.
    /// </summary>
    private async Task EnsureConversationVisibleAsync(int conversationId)
    {
        var restriction = await GetAgentRestrictionAsync();
        if (restriction is null) return;

        var visible = await RestrictToAgent(_dbContext.ChatConversations.AsNoTracking(), restriction)
            .AnyAsync(c => c.Id == conversationId);

        if (!visible)
            throw new KeyNotFoundException($"Chat conversation with ID {conversationId} not found.");
    }

    /// <summary>Applies <see cref="GetAgentRestrictionAsync"/> to any conversation query.</summary>
    private static IQueryable<ChatConversation> RestrictToAgent(
        IQueryable<ChatConversation> query,
        string? assignedToName)
    {
        if (assignedToName is null) return query;

        return query.Where(c => c.Contact.AssignedTo != null && c.Contact.AssignedTo == assignedToName);
    }

    public async Task<List<ChatConversationResponse>> GetConversationsAsync(string? search = null, string? filter = null, int? connectionId = null)
    {
        // No reconciliation here. This is polled by every open Chat tab, and rebuilding the
        // contact × connection cross-product (with writes) on each poll was the single largest
        // source of database load in the application. Rows are created when a contact is
        // created; ChatConversationReconcilerService repairs anything that slips through.
        var query = _dbContext.ChatConversations
            .AsNoTracking()
            .Include(c => c.Connection)
            .Include(c => c.Contact)
                .ThenInclude(contact => contact.GroupMemberships)
                    .ThenInclude(membership => membership.Group)
            .Include(c => c.WabaPhoneNumber)
            .AsQueryable();

        // Applied before search, filter and paging, so counts and results are all consistent with
        // what this caller is allowed to see.
        query = RestrictToAgent(query, await GetAgentRestrictionAsync());

        if (connectionId.HasValue)
        {
            query = query.Where(c => c.ConnectionId == connectionId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLower();

            // The conversation list shows the contact, the last message and the connection. Only
            // the first was searchable, so looking for a phrase you remembered from a conversation
            // -- the obvious way to find it again -- found nothing.
            query = query.Where(c =>
                c.Contact.Name.ToLower().Contains(normalizedSearch)
                || c.Contact.Phone.ToLower().Contains(normalizedSearch)
                || (c.Contact.Email != null && c.Contact.Email.ToLower().Contains(normalizedSearch))
                || (c.Contact.Company != null && c.Contact.Company.ToLower().Contains(normalizedSearch))
                || (c.LastMessageText != null && c.LastMessageText.ToLower().Contains(normalizedSearch))
                || (c.Connection != null && c.Connection.Name.ToLower().Contains(normalizedSearch))
                || c.Contact.GroupMemberships.Any(m =>
                       m.Group != null && m.Group.Name.ToLower().Contains(normalizedSearch)));
        }

        if (string.Equals(filter, "Unread Chats", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(c => c.UnreadCount > 0);
        }

        var conversations = await query
            .OrderByDescending(c => c.LastMessageAt ?? c.UpdatedAt)
            .ThenBy(c => c.Contact.Name)
            .ToListAsync();

        return conversations.Select(MapConversation).ToList();
    }

    public async Task<ChatConversationResponse> GetConversationAsync(int id)
    {
        var restriction = await GetAgentRestrictionAsync();

        var conversation = await RestrictToAgent(
                _dbContext.ChatConversations
                    .AsNoTracking()
                    .Include(c => c.Contact)
                        .ThenInclude(contact => contact.GroupMemberships)
                            .ThenInclude(membership => membership.Group)
                    .Include(c => c.WabaPhoneNumber),
                restriction)
            .FirstOrDefaultAsync(c => c.Id == id);

        // Deliberately the same "not found" a genuinely missing id produces. Telling an agent that
        // a conversation exists but is not theirs leaks the existence of every other agent's
        // customers, one id at a time.
        if (conversation == null)
            throw new KeyNotFoundException($"Chat conversation with ID {id} not found.");

        return MapConversation(conversation);
    }

    /// <summary>
    /// Reads a conversation's messages. Deliberately read-only.
    ///
    /// <para>
    /// This used to load the conversation, zero its UnreadCount and SaveChanges before reading —
    /// so the client's message poll issued a write transaction every few seconds, against a
    /// remote database. Marking a conversation read is a separate, explicit action
    /// (<see cref="MarkConversationReadAsync"/>) that the client calls once when the user opens
    /// it, which is also the only moment it is actually true.
    /// </para>
    /// </summary>
    public async Task<List<ChatMessageResponse>> GetMessagesAsync(int conversationId)
    {
        // The conversation guard has to be repeated here: the messages endpoint takes an id
        // directly, so without this an agent could read any thread by guessing a number.
        await EnsureConversationVisibleAsync(conversationId);

        var messages = await _dbContext.ChatMessages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId && !m.IsDeleted)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        return messages.Select(MapMessage).ToList();
    }

    /// <summary>
    /// Clears a conversation's unread count. One statement, no entity materialised, and a no-op
    /// at the database when the count is already zero — so repeat calls cost nothing.
    /// </summary>
    public async Task MarkConversationReadAsync(int conversationId)
    {
        await EnsureConversationVisibleAsync(conversationId);

        await _dbContext.ChatConversations
            .Where(c => c.Id == conversationId && c.UnreadCount > 0)
            .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.UnreadCount, 0));
    }

    /// <summary>
    /// Creates any missing conversation rows for one contact — one per connection that has a
    /// phone number attached.
    ///
    /// <para>
    /// This work used to run on every conversation-list read as a full contact × connection
    /// cross-product with a write at the end. It belongs at write time: a contact needs its
    /// conversation rows exactly once, when it is created.
    /// </para>
    /// </summary>
    public Task EnsureConversationsForContactAsync(int contactId) =>
        EnsureConversationsForContactsAsync(new[] { contactId });

    /// <summary>
    /// Batched form, for CSV import. Two queries and one insert regardless of how many contacts
    /// are passed — never call the single-contact overload in a loop.
    /// </summary>
    public async Task EnsureConversationsForContactsAsync(IReadOnlyCollection<int> contactIds)
    {
        if (contactIds.Count == 0) return;

        var phonesByConnection = await _dbContext.WabaPhoneNumbers
            .AsNoTracking()
            .Where(p => p.ConnectionId.HasValue)
            .GroupBy(p => p.ConnectionId!.Value)
            .Select(g => new { ConnectionId = g.Key, PhoneId = g.Min(p => p.Id) })
            .ToListAsync();

        if (phonesByConnection.Count == 0) return;

        var existing = await _dbContext.ChatConversations
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(c => contactIds.Contains(c.ContactId))
            .Select(c => new { c.ContactId, c.ConnectionId })
            .ToListAsync();

        var existingKeys = existing
            .Select(p => $"{p.ContactId}_{p.ConnectionId ?? 0}")
            .ToHashSet();

        var toCreate = new List<ChatConversation>();
        foreach (var phone in phonesByConnection)
        {
            foreach (var contactId in contactIds)
            {
                if (existingKeys.Add($"{contactId}_{phone.ConnectionId}"))
                {
                    toCreate.Add(new ChatConversation
                    {
                        ContactId = contactId,
                        ConnectionId = phone.ConnectionId,
                        WabaPhoneNumberId = phone.PhoneId
                    });
                }
            }
        }

        if (toCreate.Count == 0) return;

        _dbContext.ChatConversations.AddRange(toCreate);
        await _dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Soft-deletes messages from a conversation, and returns how many were actually removed.
    ///
    /// <para>
    /// Handles the single-message and multi-select cases with one method: the UI's right-click
    /// "Delete" is just a selection of one, and having two code paths would mean two places to
    /// keep the audit wording and the already-deleted guard in step.
    /// </para>
    /// <para>
    /// Ids that don't exist, belong to another conversation, or are already deleted are simply
    /// not counted — a partial selection deletes what it can rather than failing wholesale.
    /// </para>
    /// </summary>
    public async Task<int> DeleteMessagesAsync(int conversationId, IReadOnlyCollection<int> messageIds)
    {
        await EnsureConversationVisibleAsync(conversationId);

        if (messageIds.Count == 0) return 0;

        var messages = await _dbContext.ChatMessages
            .Where(m => m.ConversationId == conversationId
                        && messageIds.Contains(m.Id)
                        && !m.IsDeleted)
            .ToListAsync();

        if (messages.Count == 0) return 0;

        var now = DateTime.UtcNow;
        foreach (var message in messages)
        {
            message.IsDeleted = true;
            message.DeletedAt = now;
            message.DeletedByUserId = _currentUser.UserId;
            message.UpdatedAt = now;
        }

        await _dbContext.SaveChangesAsync();

        var contactLabel = await _dbContext.ChatConversations
            .Where(c => c.Id == conversationId)
            .Select(c => c.Contact!.Name ?? c.Contact!.Phone)
            .FirstOrDefaultAsync() ?? $"conversation #{conversationId}";

        // The description names what was removed, not just how many; the metadata carries every
        // message in full so the details panel can show the deletion as a transcript.
        //
        // EntityId is the conversation, not the messages. It briefly held a comma-joined list of
        // message ids, which overflowed the column's 100 characters on any sizeable delete and
        // silently cost the entry altogether. The conversation is also the thing that still exists
        // afterwards, and the thing the reader recognises.
        await _auditService.LogAsync(
            "Chat.MessagesDeleted", "Data",
            $"Deleted {messages.Count} message(s) from the conversation with {contactLabel}: " +
            $"{DescribeMessages(messages)}. " +
            "Removed from OmniConnect only — the recipient still has their copy.",
            "ChatConversation", conversationId.ToString(),
            metadata: BuildDeletedMessageMetadata(messages));

        return messages.Count;
    }

    /// <summary>
    /// Longest run of messages recorded individually in an audit entry.
    ///
    /// A "select all, delete" over a busy conversation is unbounded, and one audit row holding
    /// thousands of message bodies would be a liability rather than a record. Past this the entry
    /// keeps the true count and marks itself truncated, so the panel says so instead of quietly
    /// showing a short list as if it were complete.
    /// </summary>
    private const int MaxAuditedDeletedMessages = 200;

    /// <summary>
    /// Snapshots deleted messages for the audit trail — direction, body, media details and the
    /// message's own timestamp.
    ///
    /// Written at deletion time on purpose: for a conversation delete the rows are gone once the
    /// save completes, so this is the only surviving record of what they said.
    /// </summary>
    private static AuditMetadata BuildDeletedMessageMetadata(IReadOnlyList<ChatMessage> messages)
    {
        var recorded = messages
            .OrderBy(m => m.CreatedAt)
            .Take(MaxAuditedDeletedMessages)
            .Select(m => new AuditDeletedMessage
            {
                Id = m.Id,
                // Stringified rather than left as an enum: the audit trail is a historical record
                // and must stay readable if these enums are ever renumbered.
                Direction = m.Direction.ToString(),
                // Whitespace-only text is not a message body; leaving it null lets the client fall
                // back to the media details rather than rendering a blank line.
                Text = string.IsNullOrWhiteSpace(m.Text) ? null : m.Text.Trim(),
                MediaType = m.MediaType,
                MediaFileName = m.MediaFileName,
                SentAt = m.CreatedAt,
                Status = m.Status.ToString()
            })
            .ToList();

        return new AuditMetadata
        {
            DeletedMessages = recorded,
            DeletedMessageCount = messages.Count,
            DeletedMessagesTruncated = messages.Count > recorded.Count
        };
    }

    /// <summary>
    /// Renders deleted messages for the audit description — direction, and the text or the media
    /// filename when there is no text. Capped so a bulk delete cannot overflow the 1000-character
    /// Description column; the full per-message detail lives in the entry's change set.
    /// </summary>
    private static string DescribeMessages(IReadOnlyList<ChatMessage> messages)
    {
        const int maxListed = 5;
        const int maxTextLength = 80;

        string Describe(ChatMessage message)
        {
            var body = !string.IsNullOrWhiteSpace(message.Text)
                ? message.Text.Trim()
                : !string.IsNullOrWhiteSpace(message.MediaFileName)
                    ? $"[{message.MediaType ?? "media"}: {message.MediaFileName}]"
                    : "[no text]";

            if (body.Length > maxTextLength) body = body[..maxTextLength] + "…";
            return $"{message.Direction} \"{body}\"";
        }

        var listed = messages.Take(maxListed).Select(Describe);
        var summary = string.Join("; ", listed);

        return messages.Count > maxListed
            ? $"{summary}; and {messages.Count - maxListed} more"
            : summary;
    }

    public async Task<ChatMessageResponse> SendMessageAsync(int conversationId, SendChatMessageRequest request)
    {
        await EnsureConversationVisibleAsync(conversationId);

        var conversation = await _dbContext.ChatConversations
            .Include(c => c.Contact)
            .Include(c => c.WabaPhoneNumber)
            .FirstOrDefaultAsync(c => c.Id == conversationId);

        if (conversation == null)
            throw new KeyNotFoundException($"Chat conversation with ID {conversationId} not found.");

        if (conversation.Contact == null || !conversation.Contact.IsActive)
            throw new InvalidOperationException("Cannot send message to an inactive contact.");

        var text = request.Text?.Trim() ?? string.Empty;
        var isMedia = !string.IsNullOrWhiteSpace(request.MediaUrl) && !string.IsNullOrWhiteSpace(request.MediaType);

        if (string.IsNullOrWhiteSpace(text) && !isMedia)
            throw new ArgumentException("Message text is required.");

        var dbText = text;
        if (isMedia)
        {
            var fileName = request.MediaFileName ?? (!string.IsNullOrEmpty(request.MediaUrl) ? Path.GetFileName(request.MediaUrl) : "file");
            dbText = string.IsNullOrWhiteSpace(text) ? $"[Attachment: {fileName}]" : $"[Attachment: {fileName}]\n\n{text}";
        }

        // A conversation's ConnectionId is fixed at creation (unique index on ContactId+ConnectionId) —
        // never reassign it here, or it can collide with another pre-existing conversation for the same
        // contact on a different connection and throw a DbUpdateException on SaveChanges.
        var effectiveConnectionId = conversation.ConnectionId ?? request.ConnectionId;

        var account = await ResolveAccountAsync(request.FromPhoneNumberId, conversation);
        if (account != null)
        {
            conversation.WabaPhoneNumberId = account.Id;
        }

        var message = new ChatMessage
        {
            ConversationId = conversation.Id,
            ContactId = conversation.ContactId,
            ConnectionId = effectiveConnectionId,
            Direction = ChatMessageDirection.Outgoing,
            Status = ChatMessageStatus.Pending,
            Text = dbText,
            IsTemplate = false,
            MediaUrl = request.MediaUrl,
            MediaType = request.MediaType,
            MediaFileName = request.MediaFileName
        };

        _dbContext.ChatMessages.Add(message);
        UpdateConversationPreview(conversation, dbText);
        await _dbContext.SaveChangesAsync();

        WhatsAppSendResult result;
        if (isMedia)
        {
            result = await _whatsAppService.SendMediaMessageAsync(
                conversation.Contact.Phone,
                request.MediaUrl!,
                request.MediaType!,
                request.MediaFileName,
                string.IsNullOrWhiteSpace(text) ? null : text,
                account?.PhoneNumberId,
                effectiveConnectionId);
        }
        else
        {
            result = await _whatsAppService.SendTextMessageAsync(
                conversation.Contact.Phone,
                text,
                account?.PhoneNumberId,
                effectiveConnectionId);
        }

        if (result.Success)
        {
            message.WhatsAppMessageId = result.MessageId;
            message.Status = ChatMessageStatus.Sent;
        }
        else
        {
            message.Status = ChatMessageStatus.Failed;
            message.ErrorMessage = result.ErrorMessage ?? "Failed to send via WhatsApp Cloud API.";
        }

        await _dbContext.SaveChangesAsync();
        return MapMessage(message);
    }

    public async Task<ChatMessageResponse> SendTemplateToContactAsync(SendTemplateToContactRequest request)
    {
        var contact = await _dbContext.Contacts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == request.ContactId);
        if (contact == null)
            throw new KeyNotFoundException($"Contact with ID {request.ContactId} not found.");

        if (!contact.IsActive)
            throw new InvalidOperationException("Cannot send message to an inactive contact.");

        var template = await _dbContext.Templates
            .FirstOrDefaultAsync(t => t.Id == request.TemplateId);
        if (template == null)
            throw new KeyNotFoundException($"Template with ID {request.TemplateId} not found.");

        var preview = await _templateService.GetPreviewAsync(request.TemplateId, request.Variables);
        var messageText = preview.PreviewText;

        await CheckDailyMessageLimitAsync(request.ConnectionId);

        var result = await _whatsAppService.SendTemplateMessageWithResultAsync(
            contact.Phone,
            template.Name,
            template.Language,
            request.Variables,
            request.ConnectionId,
            // The one path with a real signed-in user behind it.
            new MessageSendContext
            {
                Category = "InitiateChat",
                SourceName = template.Name,
                ContactId = contact.Id,
                RelationType = contact.Type.ToString(),
                TriggeredBy = "User",
                PerformedByUserId = _currentUser?.UserId,
                IpAddress = _currentUser?.IpAddress
            });

        var conversation = await GetOrCreateConversationAsync(contact.Id, request.ConnectionId);

        var message = new ChatMessage
        {
            ConversationId = conversation.Id,
            ContactId = contact.Id,
            ConnectionId = request.ConnectionId,
            Direction = ChatMessageDirection.Outgoing,
            Status = result.Success ? ChatMessageStatus.Sent : ChatMessageStatus.Failed,
            Text = messageText,
            IsTemplate = true,
            WhatsAppMessageId = result.Success ? result.MessageId : null,
            ErrorMessage = result.Success ? null : (result.ErrorMessage ?? "Failed to send template message via WhatsApp Cloud API.")
        };

        _dbContext.ChatMessages.Add(message);
        UpdateConversationPreview(conversation, messageText);
        await _dbContext.SaveChangesAsync();

        return MapMessage(message);
    }

    public async Task<ChatMessage> CreateOrUpdateCampaignMessageAsync(
        Campaign campaign, 
        CampaignContact campaignContact, 
        string text, 
        string? mediaUrl = null, 
        string? mediaType = null, 
        string? mediaFileName = null)
    {
        var conversation = await GetOrCreateConversationAsync(campaignContact.ContactId, campaign.ConnectionId);

        var existing = campaignContact.Id > 0
            ? await _dbContext.ChatMessages.FirstOrDefaultAsync(m => m.CampaignContactId == campaignContact.Id)
            : null;

        if (existing != null)
        {
            existing.Text = text;
            existing.Status = ChatMessageStatus.Pending;
            existing.ErrorMessage = null;
            existing.IsTemplate = true;
            existing.MediaUrl = mediaUrl;
            existing.MediaType = mediaType;
            existing.MediaFileName = mediaFileName;
            UpdateConversationPreview(conversation, text);
            await _dbContext.SaveChangesAsync();
            return existing;
        }

        var message = new ChatMessage
        {
            ConversationId = conversation.Id,
            ContactId = campaignContact.ContactId,
            ConnectionId = campaign.ConnectionId,
            CampaignId = campaign.Id,
            CampaignContactId = campaignContact.Id,
            Direction = ChatMessageDirection.Outgoing,
            Status = ChatMessageStatus.Pending,
            Text = text,
            IsTemplate = true,
            MediaUrl = mediaUrl,
            MediaType = mediaType,
            MediaFileName = mediaFileName
        };

        _dbContext.ChatMessages.Add(message);
        UpdateConversationPreview(conversation, text);
        await _dbContext.SaveChangesAsync();
        return message;
    }

    public async Task MarkCampaignMessageSentAsync(int chatMessageId, string whatsAppMessageId)
    {
        var message = await _dbContext.ChatMessages.FindAsync(chatMessageId);
        if (message == null) return;

        message.WhatsAppMessageId = whatsAppMessageId;
        message.Status = ChatMessageStatus.Sent;
        await _dbContext.SaveChangesAsync();
    }

    public async Task MarkCampaignMessageFailedAsync(int chatMessageId, string errorMessage)
    {
        var message = await _dbContext.ChatMessages.FindAsync(chatMessageId);
        if (message == null) return;

        message.Status = ChatMessageStatus.Failed;
        message.ErrorMessage = errorMessage;
        await _dbContext.SaveChangesAsync();
    }

    public async Task<ChatConversation> GetOrCreateConversationAsync(int contactId, int? connectionId = null)
    {
        var conversation = await _dbContext.ChatConversations
            .FirstOrDefaultAsync(c => c.ContactId == contactId && (connectionId == null || c.ConnectionId == connectionId));

        if (conversation != null) return conversation;

        var account = connectionId.HasValue
            ? await _dbContext.WabaPhoneNumbers.FirstOrDefaultAsync(p => p.ConnectionId == connectionId.Value)
            : await _dbContext.WabaPhoneNumbers.OrderBy(x => x.Id).FirstOrDefaultAsync();

        conversation = new ChatConversation
        {
            ContactId = contactId,
            ConnectionId = connectionId ?? account?.ConnectionId,
            WabaPhoneNumberId = account?.Id
        };

        _dbContext.ChatConversations.Add(conversation);
        await _dbContext.SaveChangesAsync();
        return conversation;
    }

    private async Task<WabaPhoneNumber?> ResolveAccountAsync(string? phoneNumberId, ChatConversation conversation)
    {
        if (!string.IsNullOrWhiteSpace(phoneNumberId))
        {
            var selected = await _dbContext.WabaPhoneNumbers
                .FirstOrDefaultAsync(p => p.PhoneNumberId == phoneNumberId && (!conversation.ConnectionId.HasValue || p.ConnectionId == conversation.ConnectionId));

            if (selected != null)
                return selected;
        }

        if (conversation.WabaPhoneNumber != null && (!conversation.ConnectionId.HasValue || conversation.WabaPhoneNumber.ConnectionId == conversation.ConnectionId.Value))
            return conversation.WabaPhoneNumber;

        if (conversation.ConnectionId.HasValue)
        {
            var connPhone = await _dbContext.WabaPhoneNumbers
                .FirstOrDefaultAsync(p => p.ConnectionId == conversation.ConnectionId.Value);

            if (connPhone != null) return connPhone;
        }

        return await _dbContext.WabaPhoneNumbers.OrderBy(x => x.Id).FirstOrDefaultAsync();
    }

    private static void UpdateConversationPreview(ChatConversation conversation, string text)
    {
        conversation.LastMessageText = text;
        conversation.LastMessageAt = DateTime.UtcNow;
    }

    private static ChatAccountResponse MapAccount(WabaPhoneNumber account)
    {
        return new ChatAccountResponse
        {
            Id = account.Id,
            ConnectionId = account.ConnectionId,
            ConnectionName = account.Connection?.Name,
            PhoneNumber = account.PhoneNumber,
            PhoneNumberId = account.PhoneNumberId,
            DisplayName = account.DisplayName,
            VerifiedName = account.VerifiedName,
            Quality = account.Quality,
            Status = account.Status
        };
    }

    private static ChatConversationResponse MapConversation(ChatConversation conversation)
    {
        return new ChatConversationResponse
        {
            Id = conversation.Id,
            ContactId = conversation.ContactId,
            ConnectionId = conversation.ConnectionId,
            ConnectionName = conversation.Connection?.Name,
            Name = conversation.Contact.Name,
            // The contact's Type, sent verbatim. It used to be lower-cased here, which meant the
            // client could not match it against the ContactTypes lookup to find its label and
            // colour — so every admin-created type fell into a hardcoded "guest" bucket.
            Status = conversation.Contact.Type,
            Phone = conversation.Contact.Phone,
            LastMessage = conversation.LastMessageText ?? string.Empty,
            UnreadCount = conversation.UnreadCount,
            LastMessageAt = conversation.LastMessageAt,
            LastMessageTime = FormatConversationTime(conversation.LastMessageAt),
            FromPhoneNumber = conversation.WabaPhoneNumber?.PhoneNumber,
            FromPhoneNumberId = conversation.WabaPhoneNumber?.PhoneNumberId,
            AssignedTo = conversation.Contact.AssignedTo,
            Source = conversation.Contact.Source.ToString().ToLowerInvariant(),
            ContactCreatedAt = conversation.Contact.CreatedAt,
            ContactGroups = conversation.Contact.GroupMemberships?
                .Select(gm => gm.Group?.Name ?? "")
                .Where(name => !string.IsNullOrEmpty(name))
                .ToList() ?? new List<string>(),
            ContactIsActive = conversation.Contact.IsActive
        };
    }

    private static ChatMessageResponse MapMessage(ChatMessage message)
    {
        return new ChatMessageResponse
        {
            Id = message.Id,
            Type = message.Direction.ToString().ToLowerInvariant(),
            Text = message.Text,
            Time = message.CreatedAt.ToLocalTime().ToString("hh:mm tt"),
            CreatedAt = message.CreatedAt,
            Status = message.Status.ToString().ToLowerInvariant(),
            IsTemplate = message.IsTemplate,
            ErrorMessage = message.ErrorMessage,
            MediaUrl = message.MediaUrl,
            MediaType = message.MediaType,
            MediaFileName = message.MediaFileName,
            SentAt = message.SentAt,
            DeliveredAt = message.DeliveredAt,
            ReadAt = message.ReadAt,
            CampaignId = message.CampaignId,
            WhatsAppMessageId = message.WhatsAppMessageId
        };
    }

    private static string FormatConversationTime(DateTime? dateTime)
    {
        if (!dateTime.HasValue) return string.Empty;

        var local = dateTime.Value.ToLocalTime();
        var today = DateTime.Now.Date;

        if (local.Date == today)
            return local.ToString("hh:mm tt");

        if (local.Date.Year == today.Year)
            return local.ToString("MMM d");

        return local.ToString("MMM d, yyyy");
    }

    private async Task CheckDailyMessageLimitAsync(int? connectionId)
    {
        if (!connectionId.HasValue) return;

        var todayUtc = DateTime.UtcNow.Date;
        int sentToday = await _dbContext.ChatMessages
            .CountAsync(m => m.ConnectionId == connectionId.Value && m.Direction == ChatMessageDirection.Outgoing && (m.IsTemplate || m.CampaignContactId != null) && m.CreatedAt >= todayUtc);

        var phone = await _dbContext.WabaPhoneNumbers.FirstOrDefaultAsync(p => p.ConnectionId == connectionId.Value);
        int limit = 1000;
        if (phone != null && int.TryParse(phone.MessageLimit, out int customLimit) && customLimit > 0)
        {
            limit = customLimit;
        }

        if (sentToday >= limit)
        {
            throw new InvalidOperationException($"Daily message limit reached ({sentToday}/{limit}) for this connection.");
        }
    }

    public async Task DeleteConversationAsync(int conversationId)
    {
        await EnsureConversationVisibleAsync(conversationId);

        var conversation = await _dbContext.ChatConversations
            .Include(c => c.Messages)
            .Include(c => c.Contact)
            .FirstOrDefaultAsync(c => c.Id == conversationId);

        if (conversation == null)
            return;

        // Captured before the delete: the entities are detached once SaveChanges runs.
        var contactLabel = conversation.Contact?.Name ?? conversation.Contact?.Phone ?? $"contact #{conversation.ContactId}";
        var messages = conversation.Messages.ToList();
        var messageCount = messages.Count;
        // Unlike a message delete, this is a hard delete — after the save the text exists nowhere
        // else, so the audit entry is the only remaining record of what the conversation held.
        // Both the one-line summary and the full snapshot are built before the save for that
        // reason: afterwards these entities are detached and the rows are gone.
        var messageSummary = messageCount > 0
            ? $" Messages: {DescribeMessages(messages)}."
            : string.Empty;
        var metadata = BuildDeletedMessageMetadata(messages);

        _dbContext.ChatMessages.RemoveRange(conversation.Messages);
        _dbContext.ChatConversations.Remove(conversation);
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            "Chat.ConversationDeleted", "Data",
            $"Deleted the conversation with {contactLabel} ({messageCount} message(s)).{messageSummary} Removed from OmniConnect only — the recipient still has their copy.",
            "ChatConversation", conversationId.ToString(),
            metadata: metadata);
    }
}
