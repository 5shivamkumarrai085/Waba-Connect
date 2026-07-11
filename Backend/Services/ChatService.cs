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

    public ChatService(AppDbContext dbContext, IWhatsAppService whatsAppService)
    {
        _dbContext = dbContext;
        _whatsAppService = whatsAppService;
    }

    public async Task<List<ChatAccountResponse>> GetAccountsAsync()
    {
        var accounts = await _dbContext.WabaPhoneNumbers
            .OrderBy(p => p.Id)
            .ToListAsync();

        return accounts.Select(MapAccount).ToList();
    }

    public async Task<List<ChatConversationResponse>> GetConversationsAsync(string? search = null, string? filter = null)
    {
        await EnsureConversationsForActiveContactsAsync();

        var query = _dbContext.ChatConversations
            .Include(c => c.Contact)
            .Include(c => c.WabaPhoneNumber)
            .AsQueryable();

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
            .Include(c => c.Contact)
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

        var text = request.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Message text is required.");

        var account = await ResolveAccountAsync(request.FromPhoneNumberId, conversation);
        if (account != null)
        {
            conversation.WabaPhoneNumberId = account.Id;
        }

        var message = new ChatMessage
        {
            ConversationId = conversation.Id,
            ContactId = conversation.ContactId,
            Direction = ChatMessageDirection.Outgoing,
            Status = ChatMessageStatus.Pending,
            Text = text,
            IsTemplate = false
        };

        _dbContext.ChatMessages.Add(message);
        UpdateConversationPreview(conversation, text);
        await _dbContext.SaveChangesAsync();

        var result = await _whatsAppService.SendTextMessageAsync(
            conversation.Contact.Phone,
            text,
            account?.PhoneNumberId);

        if (result.Success)
        {
            message.WhatsAppMessageId = result.MessageId;
            message.Status = ChatMessageStatus.Pending;
        }
        else
        {
            message.Status = ChatMessageStatus.Failed;
            message.ErrorMessage = result.ErrorMessage ?? "Failed to send via WhatsApp Cloud API.";
        }

        await _dbContext.SaveChangesAsync();
        return MapMessage(message);
    }

    public async Task<ChatMessage> CreateOrUpdateCampaignMessageAsync(Campaign campaign, CampaignContact campaignContact, string text)
    {
        var conversation = await GetOrCreateConversationAsync(campaignContact.ContactId);

        var existing = campaignContact.Id > 0
            ? await _dbContext.ChatMessages.FirstOrDefaultAsync(m => m.CampaignContactId == campaignContact.Id)
            : null;

        if (existing != null)
        {
            existing.Text = text;
            existing.Status = ChatMessageStatus.Pending;
            existing.ErrorMessage = null;
            existing.IsTemplate = true;
            UpdateConversationPreview(conversation, text);
            await _dbContext.SaveChangesAsync();
            return existing;
        }

        var message = new ChatMessage
        {
            ConversationId = conversation.Id,
            ContactId = campaignContact.ContactId,
            CampaignId = campaign.Id,
            CampaignContactId = campaignContact.Id,
            Direction = ChatMessageDirection.Outgoing,
            Status = ChatMessageStatus.Pending,
            Text = text,
            IsTemplate = true
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
        message.Status = ChatMessageStatus.Pending;
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
        var existingContactIds = await _dbContext.ChatConversations
            .Select(c => c.ContactId)
            .ToListAsync();

        var account = await _dbContext.WabaPhoneNumbers.FirstOrDefaultAsync();
        var missingContacts = await _dbContext.Contacts
            .Where(c => !existingContactIds.Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync();

        if (missingContacts.Count == 0) return;

        foreach (var contactId in missingContacts)
        {
            _dbContext.ChatConversations.Add(new ChatConversation
            {
                ContactId = contactId,
                WabaPhoneNumberId = account?.Id
            });
        }

        await _dbContext.SaveChangesAsync();
    }

    private async Task<ChatConversation> GetOrCreateConversationAsync(int contactId)
    {
        var conversation = await _dbContext.ChatConversations
            .FirstOrDefaultAsync(c => c.ContactId == contactId);

        if (conversation != null) return conversation;

        var account = await _dbContext.WabaPhoneNumbers.FirstOrDefaultAsync();
        conversation = new ChatConversation
        {
            ContactId = contactId,
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
                .FirstOrDefaultAsync(p => p.PhoneNumberId == phoneNumberId);

            if (selected == null)
                throw new KeyNotFoundException($"WABA phone number with ID {phoneNumberId} not found.");

            return selected;
        }

        if (conversation.WabaPhoneNumber != null)
            return conversation.WabaPhoneNumber;

        return await _dbContext.WabaPhoneNumbers.FirstOrDefaultAsync();
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
            Name = conversation.Contact.Name,
            Status = conversation.Contact.Type.ToString().ToLowerInvariant(),
            Phone = conversation.Contact.Phone,
            LastMessage = conversation.LastMessageText ?? string.Empty,
            UnreadCount = conversation.UnreadCount,
            LastMessageAt = conversation.LastMessageAt,
            LastMessageTime = FormatConversationTime(conversation.LastMessageAt),
            FromPhoneNumber = conversation.WabaPhoneNumber?.PhoneNumber,
            FromPhoneNumberId = conversation.WabaPhoneNumber?.PhoneNumberId
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
}
