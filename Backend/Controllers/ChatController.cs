using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsAppCampaignApi.Models.DTOs.Chat;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Services.Email;
using WhatsAppCampaignApi.Services.Interfaces;

using WhatsAppCampaignApi.Helpers;

namespace WhatsAppCampaignApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ChatController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly IEmailReplyService _emailReply;

    public ChatController(IChatService chatService, IEmailReplyService emailReply)
    {
        _chatService = chatService;
        _emailReply = emailReply;
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
        [FromQuery] int? connectionId = null,
        // Absent means every channel, so a caller written before the email channel existed still
        // gets exactly what it got before.
        [FromQuery] string? channel = null)
    {
        var data = await _chatService.GetConversationsAsync(search, filter, connectionId, channel);
        return Ok(new ApiResponse<List<ChatConversationResponse>> { Success = true, Data = data });
    }

    /// <summary>
    /// Sends one email from an email thread — a reply, a reply-all, or a forward.
    /// </summary>
    /// <remarks>
    /// The three are the same operation with different recipients and subject, decided by the
    /// composer: Reply addresses the sender, Reply All adds the other correspondents, Forward
    /// takes a fresh address list. Three endpoints would be three ways to write the same row.
    /// </remarks>
    [HttpPost("conversations/{id:int}/email-reply")]
    [RequiresPermission("Chat.Send")]
    public async Task<IActionResult> SendEmailReply(
        int id,
        [FromBody] SendEmailReplyRequest request,
        CancellationToken ct)
    {
        // Decode any base64 attachments from the composer before handing off to the service.
        var attachments = (request.Attachments ?? [])
            .Where(a => !string.IsNullOrWhiteSpace(a.Base64Data))
            .Select(a =>
            {
                byte[] bytes;
                try { bytes = Convert.FromBase64String(a.Base64Data); }
                catch { bytes = []; }
                return new EmailReplyAttachment(
                    FileName: string.IsNullOrWhiteSpace(a.FileName) ? "attachment" : a.FileName,
                    ContentType: string.IsNullOrWhiteSpace(a.ContentType) ? "application/octet-stream" : a.ContentType,
                    Content: bytes);
            })
            .ToList();

        var result = await _emailReply.SendAsync(id, new EmailReplyRequest(
            Subject: request.Subject ?? string.Empty,
            BodyHtml: request.BodyHtml ?? string.Empty,
            To: request.To ?? [],
            Cc: request.Cc,
            Bcc: request.Bcc,
            InReplyToMessageId: request.InReplyToMessageId,
            Attachments: attachments.Count > 0 ? attachments : null), ct);

        // A refused send is a 200 with Success=false, matching how the rest of this controller
        // reports an outcome the caller asked for and did not get. The composer needs the reason
        // to show, not an exception to catch.
        return Ok(new ApiResponse<int?>
        {
            Success = result.Success,
            Message = result.Message,
            Data = result.ChatMessageId
        });
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
