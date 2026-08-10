using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsAppCampaignApi.Models.DTOs.Chat;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Services.Interfaces;

using WhatsAppCampaignApi.Helpers;

namespace WhatsAppCampaignApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ChatController : ControllerBase
{
    private readonly IChatService _chatService;

    public ChatController(IChatService chatService)
    {
        _chatService = chatService;
    }

    [HttpGet("accounts")]
    [RequiresPermission("Chat.View")]
    public async Task<ActionResult<ApiResponse<List<ChatAccountResponse>>>> GetAccounts([FromQuery] int? connectionId = null)
    {
        var data = await _chatService.GetAccountsAsync(connectionId);
        return Ok(new ApiResponse<List<ChatAccountResponse>> { Success = true, Data = data });
    }

    [HttpGet("conversations")]
    [RequiresPermission("Chat.View")]
    public async Task<ActionResult<ApiResponse<List<ChatConversationResponse>>>> GetConversations(
        [FromQuery] string? search = null,
        [FromQuery] string? filter = null,
        [FromQuery] int? connectionId = null)
    {
        var data = await _chatService.GetConversationsAsync(search, filter, connectionId);
        return Ok(new ApiResponse<List<ChatConversationResponse>> { Success = true, Data = data });
    }

    [HttpGet("conversations/{id}")]
    [RequiresPermission("Chat.View")]
    public async Task<ActionResult<ApiResponse<ChatConversationResponse>>> GetConversation(int id)
    {
        var data = await _chatService.GetConversationAsync(id);
        return Ok(new ApiResponse<ChatConversationResponse> { Success = true, Data = data });
    }

    [HttpGet("conversations/{id}/messages")]
    [RequiresPermission("Chat.View")]
    public async Task<ActionResult<ApiResponse<List<ChatMessageResponse>>>> GetMessages(int id)
    {
        var data = await _chatService.GetMessagesAsync(id);
        return Ok(new ApiResponse<List<ChatMessageResponse>> { Success = true, Data = data });
    }

    [HttpPost("conversations/{id}/messages")]
    [RequiresPermission("Chat.Send")]
    public async Task<ActionResult<ApiResponse<ChatMessageResponse>>> SendMessage(int id, [FromBody] SendChatMessageRequest request)
    {
        var data = await _chatService.SendMessageAsync(id, request);
        return Ok(new ApiResponse<ChatMessageResponse> { Success = true, Data = data });
    }

    [HttpPost("send-template-to-contact")]
    [RequiresPermission("Chat.InitiateChat")]
    public async Task<ActionResult<ApiResponse<ChatMessageResponse>>> SendTemplateToContact([FromBody] SendTemplateToContactRequest request)
    {
        var data = await _chatService.SendTemplateToContactAsync(request);
        return Ok(new ApiResponse<ChatMessageResponse> { Success = true, Data = data });
    }

    /// <summary>
    /// Marks a conversation read. Separate from fetching its messages so that polling for new
    /// messages stays a pure read — it used to issue a write on every poll.
    /// </summary>
    [HttpPost("conversations/{id}/read")]
    [RequiresPermission("Chat.View")]
    public async Task<ActionResult<ApiResponse>> MarkRead(int id)
    {
        await _chatService.MarkConversationReadAsync(id);
        return Ok(new ApiResponse { Success = true });
    }

    [HttpDelete("conversations/{id}")]
    [RequiresPermission("Chat.Delete")]
    public async Task<ActionResult<ApiResponse>> DeleteConversation(int id)
    {
        await _chatService.DeleteConversationAsync(id);
        return Ok(new ApiResponse { Success = true, Message = "Conversation deleted successfully." });
    }

    /// <summary>
    /// Removes one or more messages from a conversation.
    ///
    /// <para>
    /// One endpoint for both the single right-click delete and the multi-select delete — the
    /// former is just a list of one. This is a soft delete: the message stops appearing in
    /// OmniConnect, but WhatsApp offers no way to unsend from the recipient's device, and the
    /// UI says so before confirming.
    /// </para>
    /// </summary>
    [HttpPost("conversations/{id}/messages/delete")]
    [RequiresPermission("Chat.Delete")]
    public async Task<ActionResult<ApiResponse>> DeleteMessages(int id, [FromBody] DeleteChatMessagesRequest request)
    {
        if (request.MessageIds is null || request.MessageIds.Count == 0)
            return BadRequest(new ApiResponse { Success = false, Message = "No messages selected." });

        var deleted = await _chatService.DeleteMessagesAsync(id, request.MessageIds);

        if (deleted == 0)
        {
            return NotFound(new ApiResponse
            {
                Success = false,
                Message = "Those messages were not found in this conversation, or were already deleted."
            });
        }

        return Ok(new ApiResponse
        {
            Success = true,
            Message = deleted == 1 ? "Message deleted." : $"{deleted} messages deleted."
        });
    }
}

public class DeleteChatMessagesRequest
{
    public List<int> MessageIds { get; set; } = new();
}
