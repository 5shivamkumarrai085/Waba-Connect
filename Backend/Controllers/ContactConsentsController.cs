using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Compliance;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Controllers;

/// <summary>
/// A contact's consent: the current answer per channel and topic, its full history, and an
/// agent recording a change (with a note, which is kept as the evidence).
/// </summary>
[ApiController]
[Authorize]
[Route("api/contacts/{contactId:int}/consents")]
public class ContactConsentsController : ControllerBase
{
    private readonly IConsentService _consent;
    private readonly ICurrentUserService _currentUser;
    private readonly AppDbContext _db;

    public ContactConsentsController(IConsentService consent, ICurrentUserService currentUser, AppDbContext db)
    {
        _consent = consent;
        _currentUser = currentUser;
        _db = db;
    }

    [HttpGet]
    [RequiresPermission("Consent.View")]
    public async Task<ActionResult<ApiResponse<object>>> Get(int contactId, CancellationToken ct)
    {
        await EnsureContactAsync(contactId, ct);

        var current = await _consent.GetForContactAsync(contactId, ct);
        var history = await _consent.GetHistoryAsync(contactId, 100, ct);
        return Ok(new ApiResponse<object> { Success = true, Data = new { current, history } });
    }

    [HttpPost]
    [RequiresPermission("Consent.Manage")]
    public async Task<ActionResult<ApiResponse<object>>> Set(int contactId, [FromBody] SetConsentRequest request, CancellationToken ct)
    {
        await EnsureContactAsync(contactId, ct);

        if (!Enum.TryParse<MessageChannel>(request.Channel, true, out var channel))
            throw new ArgumentException("Choose a channel: Email or WhatsApp.");
        if (!Enum.TryParse<ConsentStatus>(request.Status, true, out var status))
            throw new ArgumentException("Choose OptedIn or OptedOut.");
        if (request.Topic is { Length: > 64 })
            throw new ArgumentException("The topic can be at most 64 characters.");

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note is { Length: > 500 }) note = note[..500];

        var changed = await _consent.SetAsync(
            contactId, channel, request.Topic, status, "agent",
            proof: new { note, recordedBy = _currentUser.UserName ?? _currentUser.Email },
            actorUserId: _currentUser.UserId,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
            ct: ct);

        return Ok(new ApiResponse<object>
        {
            Success = true,
            Data = new { changed },
            Message = changed ? "Consent recorded." : "No change: the contact already has that answer."
        });
    }

    private async Task EnsureContactAsync(int contactId, CancellationToken ct)
    {
        if (!await _db.Contacts.AsNoTracking().AnyAsync(c => c.Id == contactId, ct))
            throw new KeyNotFoundException("Contact not found.");
    }
}
