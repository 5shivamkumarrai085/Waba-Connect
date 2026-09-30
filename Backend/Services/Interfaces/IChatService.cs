using WhatsAppCampaignApi.Models.DTOs.Chat;
using WhatsAppCampaignApi.Models.Entities;

namespace WhatsAppCampaignApi.Services.Interfaces;

public interface IChatService
{
    Task<List<ChatAccountResponse>> GetAccountsAsync(int? connectionId = null);
    /// <param name="channel">
    /// "WhatsApp" or "Email". Null or unrecognised means every channel — filtering server-side
    /// rather than in the client is what lets the inbox scope the list to one channel at all,
    /// since the client only ever received one connection's worth of rows.
    /// </param>
    /// <remarks>
    /// Keyset-paged, newest activity first. <paramref name="cursor"/> is the opaque value returned
    /// as <see cref="ChatPage{T}.NextCursor"/> by the previous page; null starts from the top.
    /// </remarks>
    Task<ChatPage<ChatConversationResponse>> GetConversationsAsync(
        string? search = null,
        string? filter = null,
        int? connectionId = null,
        string? channel = null,
        string? cursor = null,
        int limit = ChatPaging.DefaultConversationPageSize,
        string? state = null,
        string? assignee = null);
    Task<ChatConversationResponse> GetConversationAsync(int id);
    /// <remarks>
    /// Returns messages in chronological order. With neither bound, the newest
    /// <paramref name="limit"/>; with <paramref name="beforeId"/>, the page just older than it (for
    /// scrolling back); with <paramref name="afterId"/>, only what arrived since (for a live thread).
    /// </remarks>
    Task<ChatPage<ChatMessageResponse>> GetMessagesAsync(
        int conversationId,
        int? beforeId = null,
        int? afterId = null,
        int limit = ChatPaging.DefaultMessagePageSize);
    Task<ChatMessageResponse> SendMessageAsync(int conversationId, SendChatMessageRequest request);
    Task<ChatMessageResponse> SendTemplateToContactAsync(SendTemplateToContactRequest request);
    Task DeleteConversationAsync(int conversationId);

    /// <summary>Clears a conversation's unread count. Called once when the user opens it.</summary>
    Task MarkConversationReadAsync(int conversationId);

    /// <summary>Creates any missing conversation rows for a newly created contact.</summary>
    Task EnsureConversationsForContactAsync(int contactId);

    /// <summary>Batched form for imports — one insert regardless of the number of contacts.</summary>
    Task EnsureConversationsForContactsAsync(IReadOnlyCollection<int> contactIds);

    /// <summary>Soft-deletes the given messages; returns how many were actually removed.</summary>
    Task<int> DeleteMessagesAsync(int conversationId, IReadOnlyCollection<int> messageIds);
    Task<ChatConversation> GetOrCreateConversationAsync(int contactId, int? connectionId = null);
    Task<ChatMessage> CreateOrUpdateCampaignMessageAsync(
        Campaign campaign, 
        CampaignContact campaignContact, 
        string text, 
        string? mediaUrl = null, 
        string? mediaType = null, 
        string? mediaFileName = null);
    Task MarkCampaignMessageSentAsync(int chatMessageId, string whatsAppMessageId);
    Task MarkCampaignMessageFailedAsync(int chatMessageId, string errorMessage);
}
