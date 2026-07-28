using Microsoft.AspNetCore.Mvc;
using WhatsAppCampaignApi.Models.DTOs.Chat;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly IChatService _chatService;

    public ChatController(IChatService chatService)
    {
        _chatService = chatService;
    }

    [HttpGet("accounts")]
    public async Task<ActionResult<ApiResponse<List<ChatAccountResponse>>>> GetAccounts([FromQuery] int? connectionId = null)
    {
        var data = await _chatService.GetAccountsAsync(connectionId);
        return Ok(new ApiResponse<List<ChatAccountResponse>> { Success = true, Data = data });
    }

    [HttpGet("conversations")]
    public async Task<ActionResult<ApiResponse<List<ChatConversationResponse>>>> GetConversations(
        [FromQuery] string? search = null,
        [FromQuery] string? filter = null,
        [FromQuery] int? connectionId = null)
    {
        var data = await _chatService.GetConversationsAsync(search, filter, connectionId);
        return Ok(new ApiResponse<List<ChatConversationResponse>> { Success = true, Data = data });
    }

    [HttpGet("conversations/{id}")]
    public async Task<ActionResult<ApiResponse<ChatConversationResponse>>> GetConversation(int id)
    {
        var data = await _chatService.GetConversationAsync(id);
        return Ok(new ApiResponse<ChatConversationResponse> { Success = true, Data = data });
    }

    [HttpGet("conversations/{id}/messages")]
    public async Task<ActionResult<ApiResponse<List<ChatMessageResponse>>>> GetMessages(int id)
    {
        var data = await _chatService.GetMessagesAsync(id);
        return Ok(new ApiResponse<List<ChatMessageResponse>> { Success = true, Data = data });
    }

    [HttpPost("conversations/{id}/messages")]
    public async Task<ActionResult<ApiResponse<ChatMessageResponse>>> SendMessage(int id, [FromBody] SendChatMessageRequest request)
    {
        var data = await _chatService.SendMessageAsync(id, request);
        return Ok(new ApiResponse<ChatMessageResponse> { Success = true, Data = data });
    }

    [HttpPost("send-template-to-contact")]
    public async Task<ActionResult<ApiResponse<ChatMessageResponse>>> SendTemplateToContact([FromBody] SendTemplateToContactRequest request)
    {
        var data = await _chatService.SendTemplateToContactAsync(request);
        return Ok(new ApiResponse<ChatMessageResponse> { Success = true, Data = data });
    }

    [HttpDelete("conversations/{id}")]
    public async Task<ActionResult<ApiResponse>> DeleteConversation(int id)
    {
        await _chatService.DeleteConversationAsync(id);
        return Ok(new ApiResponse { Success = true, Message = "Conversation deleted successfully." });
    }
}
