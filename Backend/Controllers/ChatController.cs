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

    private readonly WhatsAppCampaignApi.Services.Chat.IConversationOperations _conversationOps;

    public ChatController(IChatService chatService, IEmailReplyService emailReply, WhatsAppCampaignApi.Services.Chat.IConversationOperations conversationOps)
    {
        _conversationOps = conversationOps;
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
        [FromQuery] string? channel = null,
        [FromQuery] string? cursor = null,
        [FromQuery] int limit = ChatPaging.DefaultConversationPageSize,
        // Status tab (active, open, pending, resolved, closed) and owner (me, unassigned, user id).
        [FromQuery] string? state = null,
        [FromQuery] string? assignee = null)
    {
        var page = await _chatService.GetConversationsAsync(search, filter, connectionId, channel, cursor, limit, state, assignee);

        // The body keeps its original shape (a list) so existing clients are unaffected; paging
        // metadata travels in headers, which CORS exposes.
        WritePageHeaders(page.NextCursor, page.HasMore);
        return Ok(new ApiResponse<List<ChatConversationResponse>> { Success = true, Data = page.Items });
    }

    private void WritePageHeaders(string? nextCursor, bool hasMore)
    {
        Response.Headers["X-Has-More"] = hasMore ? "true" : "false";
        if (nextCursor is not null) Response.Headers["X-Next-Cursor"] = nextCursor;
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

        // The same visibility rule as every other per-conversation action (agent restriction and
        // connection scope): throws 404 when this caller may not see the conversation.
        await _chatService.GetConversationAsync(id);

        var result = await _emailReply.SendAsync(id, new EmailReplyRequest(
            Subject: request.Subject ?? string.Empty,
            BodyHtml: request.BodyHtml ?? string.Empty,
            To: request.To ?? [],
            Cc: request.Cc,
            Bcc: request.Bcc,
            InReplyToMessageId: request.InReplyToMessageId,
            Attachments: attachments.Count > 0 ? attachments : null), ct);

        if (result.Success) await _conversationOps.OnAgentReplyAsync(id, ct);

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

    /// <summary>Agents who can take conversations on a connection, with their open workload.</summary>
    [HttpGet("assignable-agents")]
    [RequiresPermission("Chat.View")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WhatsAppCampaignApi.Services.Chat.AssignableAgent>>>> AssignableAgents(
        [FromQuery] int? connectionId, CancellationToken ct)
    {
        var data = await _conversationOps.GetAssignableAgentsAsync(connectionId, ct);
        return Ok(new ApiResponse<IReadOnlyList<WhatsAppCampaignApi.Services.Chat.AssignableAgent>> { Success = true, Data = data });
    }

    /// <summary>Assigns (or with null, unassigns) a conversation.</summary>
    [HttpPost("conversations/{id}/assign")]
    [RequiresPermission("Chat.Assign")]
    public async Task<ActionResult<ApiResponse<ChatConversationResponse>>> Assign(int id, [FromBody] AssignConversationRequest request, CancellationToken ct)
    {
        await _chatService.GetConversationAsync(id); // visibility: 404 when out of scope
        await _conversationOps.AssignAsync(id, request.UserId, ct);
        return Ok(new ApiResponse<ChatConversationResponse> { Success = true, Data = await _chatService.GetConversationAsync(id) });
    }

    /// <summary>Open, Pending, Resolved or Closed.</summary>
    [HttpPost("conversations/{id}/status")]
    [RequiresPermission("Chat.Send")]
    public async Task<ActionResult<ApiResponse<ChatConversationResponse>>> SetStatus(int id, [FromBody] ConversationStatusRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<WhatsAppCampaignApi.Models.Enums.ConversationStatus>(request.Status, true, out var status))
            throw new ArgumentException("Status must be Open, Pending, Resolved or Closed.");
        await _chatService.GetConversationAsync(id);
        await _conversationOps.SetStatusAsync(id, status, ct);
        return Ok(new ApiResponse<ChatConversationResponse> { Success = true, Data = await _chatService.GetConversationAsync(id) });
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
    public async Task<ActionResult<ApiResponse<List<ChatMessageResponse>>>> GetMessages(
        int id,
        [FromQuery] int? beforeId = null,
        [FromQuery] int? afterId = null,
        [FromQuery] int limit = ChatPaging.DefaultMessagePageSize)
    {
        var page = await _chatService.GetMessagesAsync(id, beforeId, afterId, limit);
        WritePageHeaders(page.NextCursor, page.HasMore);
        return Ok(new ApiResponse<List<ChatMessageResponse>> { Success = true, Data = page.Items });
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
