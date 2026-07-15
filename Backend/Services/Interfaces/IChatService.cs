using WhatsAppCampaignApi.Models.DTOs.Chat;
using WhatsAppCampaignApi.Models.Entities;

namespace WhatsAppCampaignApi.Services.Interfaces;

public interface IChatService
{
    Task<List<ChatAccountResponse>> GetAccountsAsync();
    Task<List<ChatConversationResponse>> GetConversationsAsync(string? search = null, string? filter = null);
    Task<ChatConversationResponse> GetConversationAsync(int id);
    Task<List<ChatMessageResponse>> GetMessagesAsync(int conversationId);
    Task<ChatMessageResponse> SendMessageAsync(int conversationId, SendChatMessageRequest request);
    Task<ChatMessageResponse> SendTemplateToContactAsync(SendTemplateToContactRequest request);
    Task DeleteConversationAsync(int conversationId);
    Task<ChatConversation> GetOrCreateConversationAsync(int contactId);
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
