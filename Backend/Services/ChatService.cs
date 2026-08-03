using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
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

    public ChatService(AppDbContext dbContext, IWhatsAppService whatsAppService, ITemplateService templateService)
    {
        _dbContext = dbContext;
        _whatsAppService = whatsAppService;
        _templateService = templateService;
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

    public async Task<List<ChatConversationResponse>> GetConversationsAsync(string? search = null, string? filter = null, int? connectionId = null)
    {
        await EnsureConversationsForActiveContactsAsync();

        var query = _dbContext.ChatConversations
            .AsNoTracking()
            .Include(c => c.Connection)
            .Include(c => c.Contact)
                .ThenInclude(contact => contact.GroupMemberships)
                    .ThenInclude(membership => membership.Group)
            .Include(c => c.WabaPhoneNumber)
            .AsQueryable();

        if (connectionId.HasValue)
        {
            query = query.Where(c => c.ConnectionId == connectionId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLower();
            query = query.Where(c =>
                c.Contact.Name.ToLower().Contains(normalizedSearch)
                || c.Contact.Phone.Contains(normalizedSearch));
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
        var conversation = await _dbContext.ChatConversations
            .AsNoTracking()
            .Include(c => c.Contact)
                .ThenInclude(contact => contact.GroupMemberships)
                    .ThenInclude(membership => membership.Group)
            .Include(c => c.WabaPhoneNumber)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (conversation == null)
            throw new KeyNotFoundException($"Chat conversation with ID {id} not found.");

        return MapConversation(conversation);
    }

    public async Task<List<ChatMessageResponse>> GetMessagesAsync(int conversationId)
    {
        var conversation = await _dbContext.ChatConversations
            .FirstOrDefaultAsync(c => c.Id == conversationId);

        if (conversation == null)
            throw new KeyNotFoundException($"Chat conversation with ID {conversationId} not found.");

        conversation.UnreadCount = 0;
        await _dbContext.SaveChangesAsync();

        var messages = await _dbContext.ChatMessages
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        return messages.Select(MapMessage).ToList();
    }

    public async Task<ChatMessageResponse> SendMessageAsync(int conversationId, SendChatMessageRequest request)
    {
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
            request.ConnectionId);

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

    private async Task EnsureConversationsForActiveContactsAsync()
    {
        // Get all contacts (including inactive for viewing history)
        var allContactIds = await _dbContext.Contacts
            .IgnoreQueryFilters()
            .Where(c => !c.IsDeleted)
            .Select(c => c.Id)
            .ToListAsync();

        // Get the default account for legacy migration
        var defaultAccount = await _dbContext.WabaPhoneNumbers.OrderBy(x => x.Id).FirstOrDefaultAsync();

        // MIGRATION: Fix old conversations with null ConnectionId
        if (defaultAccount?.ConnectionId != null)
        {
            var nullConnConversations = await _dbContext.ChatConversations
                .Where(c => c.ConnectionId == null)
                .ToListAsync();

            foreach (var conv in nullConnConversations)
            {
                conv.ConnectionId = defaultAccount.ConnectionId;
                if (conv.WabaPhoneNumberId == null)
                    conv.WabaPhoneNumberId = defaultAccount.Id;
            }

            if (nullConnConversations.Count > 0)
                await _dbContext.SaveChangesAsync();
        }

        // Gather all connectionIds that have phone numbers
        var phonesByConnection = await _dbContext.WabaPhoneNumbers
            .Where(p => p.ConnectionId.HasValue)
            .GroupBy(p => p.ConnectionId!.Value)
            .Select(g => new { ConnectionId = g.Key, PhoneId = g.Min(p => p.Id) })
            .ToListAsync();

        // Get existing conversation (contactId, connectionId) pairs
        var existingPairs = await _dbContext.ChatConversations
            .Select(c => new { c.ContactId, c.ConnectionId })
            .ToListAsync();

        var existingSet = new HashSet<string>(
            existingPairs.Select(p => $"{p.ContactId}_{p.ConnectionId ?? 0}"));

        var newConversations = new List<ChatConversation>();

        // For each phone-connected connection, ensure every contact has a conversation
        foreach (var pc in phonesByConnection)
        {
            foreach (var contactId in allContactIds)
            {
                var key = $"{contactId}_{pc.ConnectionId}";
                if (!existingSet.Contains(key))
                {
                    newConversations.Add(new ChatConversation
                    {
                        ContactId = contactId,
                        ConnectionId = pc.ConnectionId,
                        WabaPhoneNumberId = pc.PhoneId
                    });
                    existingSet.Add(key);
                }
            }
        }

        // Also ensure contacts with NO conversation at all get a default one
        var contactsWithAnyConv = existingPairs.Select(p => p.ContactId).Distinct().ToHashSet();
        foreach (var contactId in allContactIds)
        {
            if (!contactsWithAnyConv.Contains(contactId) && !newConversations.Any(c => c.ContactId == contactId))
            {
                newConversations.Add(new ChatConversation
                {
                    ContactId = contactId,
                    ConnectionId = defaultAccount?.ConnectionId,
                    WabaPhoneNumberId = defaultAccount?.Id
                });
            }
        }

        if (newConversations.Count > 0)
        {
            _dbContext.ChatConversations.AddRange(newConversations);
            await _dbContext.SaveChangesAsync();
        }
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
            Status = conversation.Contact.Type.ToString().ToLowerInvariant(),
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
        var conversation = await _dbContext.ChatConversations
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.Id == conversationId);

        if (conversation == null)
            return;

        _dbContext.ChatMessages.RemoveRange(conversation.Messages);
        _dbContext.ChatConversations.Remove(conversation);
        await _dbContext.SaveChangesAsync();
    }
}
