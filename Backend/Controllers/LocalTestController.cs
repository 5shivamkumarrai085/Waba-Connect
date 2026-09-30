#if DEBUG
// Development harness only: compiled out of Release builds entirely, so no production binary
// carries it, whatever the environment variable says.
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Controllers;

/// <remarks>
/// Local development harness that simulates inbound webhook traffic without Meta.
///
/// <para>
/// Anonymous, and it injects messages straight into the bot router — which is fine on a
/// developer machine and unacceptable anywhere else, since anyone who can reach the host could
/// drive the bots. Every action therefore refuses outside Development. It stays
/// [AllowAnonymous] so it needs no token locally.
/// </para>
/// </remarks>
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class LocalTestController : ControllerBase
{
    private readonly IBotRouterService _routerService;
    private readonly AppDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;

    public LocalTestController(
        IBotRouterService routerService,
        AppDbContext dbContext,
        IWebHostEnvironment environment)
    {
        _routerService = routerService;
        _dbContext = dbContext;
        _environment = environment;
    }

    [HttpPost("simulate-incoming")]
    public async Task<ActionResult<ApiResponse<SimulationResponse>>> SimulateIncoming([FromBody] SimulateIncomingRequest request)
    {
        // 404 rather than 403 outside Development — the endpoint shouldn't announce itself.
        if (!_environment.IsDevelopment()) return NotFound();

        if (string.IsNullOrWhiteSpace(request.PhoneNumber) || string.IsNullOrWhiteSpace(request.MessageText))
        {
            return BadRequest(new ApiResponse<SimulationResponse>
            {
                Success = false,
                Message = "PhoneNumber and MessageText are required."
            });
        }

        string normalizedPhone = request.PhoneNumber.Replace("+", "").Trim();
        string searchPhoneWithPlus = "+" + normalizedPhone;

        try
        {
            // Find or create test contact (ignoring global query filter)
            var contact = await _dbContext.Contacts
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.Phone == normalizedPhone || c.Phone == searchPhoneWithPlus);
            bool isNewContact = false;

            if (contact == null)
            {
                contact = new Contact
                {
                    Phone = searchPhoneWithPlus,
                    Name = "Simulation User",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _dbContext.Contacts.Add(contact);
                await _dbContext.SaveChangesAsync();
                isNewContact = true;
            }
            else if (!contact.IsActive)
            {
                contact.IsActive = true;
                contact.UpdatedAt = DateTime.UtcNow;
                _dbContext.Entry(contact).State = EntityState.Modified;
                await _dbContext.SaveChangesAsync();
            }

            // Create or update conversation state for the simulation contact
            var conversation = await _dbContext.ChatConversations.FirstOrDefaultAsync(c => c.ContactId == contact.Id);
            if (conversation == null)
            {
                conversation = new ChatConversation
                {
                    ContactId = contact.Id,
                    LastMessageText = request.MessageText,
                    LastMessageAt = DateTime.UtcNow,
                    UnreadCount = 1,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _dbContext.ChatConversations.Add(conversation);
                await _dbContext.SaveChangesAsync();
            }
            else
            {
                conversation.LastMessageText = request.MessageText;
                conversation.LastMessageAt = DateTime.UtcNow;
                conversation.UnreadCount += 1;
                _dbContext.Entry(conversation).State = EntityState.Modified;
            }

            // Save incoming message in ChatMessages table
            _dbContext.ChatMessages.Add(new ChatMessage
            {
                ConversationId = conversation.Id,
                ContactId = contact.Id,
                WhatsAppMessageId = "wamid.simulated_" + Guid.NewGuid().ToString(),
                Direction = ChatMessageDirection.Incoming,
                Status = ChatMessageStatus.Received,
                Text = request.MessageText,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await _dbContext.SaveChangesAsync();

            // Route the message through the webhook router pipeline
            bool matched = await _routerService.RouteMessageAsync(normalizedPhone, request.MessageText, contact);

            return Ok(new ApiResponse<SimulationResponse>
            {
                Success = true,
                Data = new SimulationResponse
                {
                    ContactId = contact.Id,
                    IsNewContact = isNewContact,
                    MatchedAnyBot = matched
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiResponse<SimulationResponse>
            {
                Success = false,
                Message = $"Simulation error: {ex.Message}"
            });
        }
    }
}

public class SimulateIncomingRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
    public string MessageText { get; set; } = string.Empty;
}

public class SimulationResponse
{
    public int ContactId { get; set; }
    public bool IsNewContact { get; set; }
    public bool MatchedAnyBot { get; set; }
}

#endif
