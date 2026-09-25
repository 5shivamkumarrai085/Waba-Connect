using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.Email;
using WhatsAppCampaignApi.Services.Email;

namespace WhatsAppCampaignApi.Controllers;

/// <summary>
/// Email-channel connections: provider credentials, sender identities and connectivity tests.
///
/// <para>
/// Credentials are write-only across this whole controller. Responses carry <c>hasSecretAccessKey</c>
/// and <c>hasSmtpPassword</c> booleans and never the values, so a working credential is never
/// available to anyone who can open the page, read a response body or take a screenshot.
/// </para>
/// </summary>
[ApiController]
[Route("api/email/connections")]
[Authorize]
public class EmailConnectionsController : ControllerBase
{
    private readonly IEmailConnectionService _service;
    private readonly IEmailDomainService _domainService;

    public EmailConnectionsController(
        IEmailConnectionService service,
        IEmailDomainService domainService)
    {
        _service = service;
        _domainService = domainService;
    }

    [HttpGet]
    [RequiresPermission("EmailConnection.View")]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var data = await _service.GetAllAsync(ct);
        return Ok(new ApiResponse<List<EmailConnectionResponse>> { Success = true, Data = data });
    }

    [HttpGet("{id:int}")]
    [RequiresPermission("EmailConnection.View")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var data = await _service.GetByIdAsync(id, ct);
        return Ok(new ApiResponse<EmailConnectionResponse> { Success = true, Data = data });
    }

    /// <summary>Step 1 of the wizard: name the connection and its first sender.</summary>
    [HttpPost]
    [RequiresPermission("EmailConnection.Connect")]
    public async Task<IActionResult> Create([FromBody] CreateEmailConnectionRequest request, CancellationToken ct)
    {
        try
        {
            var data = await _service.CreateAsync(request, ct);
            return Ok(new ApiResponse<EmailConnectionResponse>
            {
                Success = true,
                Message = "Email connection created. Configure a provider to start sending.",
                Data = data
            });
        }
        catch (InvalidOperationException ex)
        {
            // Duplicate connection name — 409 Conflict so the client can distinguish it from
            // a 500 and show a targeted inline message rather than a generic failure toast.
            return Conflict(new ApiResponse<object>
            {
                Success = false,
                Message = ex.Message
            });
        }
    }

    /// <summary>Step 2: store provider credentials. Blank secrets keep their stored values.</summary>
    [HttpPut("{id:int}/provider")]
    [RequiresPermission("EmailConnection.Edit")]
    public async Task<IActionResult> SaveProvider(
        int id,
        [FromBody] SaveEmailProviderRequest request,
        CancellationToken ct)
    {
        var data = await _service.SaveProviderAsync(id, request, ct);
        return Ok(new ApiResponse<EmailConnectionResponse>
        {
            Success = true,
            Message = "Provider configuration saved.",
            Data = data
        });
    }

    /// <summary>Verifies stored credentials. Sends nothing, so it costs no quota.</summary>
    [HttpPost("{id:int}/test-connection")]
    [RequiresPermission("EmailConnection.Test")]
    public async Task<IActionResult> TestConnection(int id, CancellationToken ct)
    {
        var result = await _service.TestConnectionAsync(id, ct);

        // Deliberately 200 with success:false rather than a 4xx. A failed credential test is a
        // successful diagnostic, and the wizard renders the message either way — an error status
        // would make the frontend's generic error handling swallow the detail that explains what
        // to fix.
        return Ok(new ApiResponse<EmailProviderTestResponse>
        {
            Success = result.Success,
            Message = result.Message,
            Data = result
        });
    }

    /// <summary>
    /// Verifies credentials supplied in the body, so the wizard's Test Connection button works
    /// before Save. Without it, an operator would have to store a possibly-wrong secret to find
    /// out that it is wrong.
    /// </summary>
    [HttpPost("test-connection")]
    [RequiresPermission("EmailConnection.Test")]
    public async Task<IActionResult> TestUnsaved([FromBody] TestEmailProviderRequest request, CancellationToken ct)
    {
        var result = await _service.TestUnsavedAsync(request, ct);
        return Ok(new ApiResponse<EmailProviderTestResponse>
        {
            Success = result.Success,
            Message = result.Message,
            Data = result
        });
    }

    /// <summary>
    /// Sends one real email. Separate from the credential test because this one costs send quota
    /// and counts against the account's reputation.
    /// </summary>
    [HttpPost("{id:int}/test-email")]
    [RequiresPermission("EmailConnection.Test")]
    public async Task<IActionResult> SendTestEmail(
        int id,
        [FromBody] SendTestEmailRequest request,
        CancellationToken ct)
    {
        var result = await _service.SendTestEmailAsync(id, request, ct);
        return Ok(new ApiResponse<EmailProviderTestResponse>
        {
            Success = result.Success,
            Message = result.Message,
            Data = result
        });
    }

    [HttpGet("{id:int}/senders")]
    [RequiresPermission("EmailConnection.View")]
    public async Task<IActionResult> GetSenders(int id, CancellationToken ct)
    {
        var data = await _service.GetSendersAsync(id, ct);
        return Ok(new ApiResponse<List<EmailSenderIdentityResponse>> { Success = true, Data = data });
    }

    [HttpPost("{id:int}/senders")]
    [RequiresPermission("EmailConnection.Edit")]
    public async Task<IActionResult> AddSender(
        int id,
        [FromBody] SaveEmailSenderIdentityRequest request,
        CancellationToken ct)
    {
        var data = await _service.AddSenderAsync(id, request, ct);
        return Ok(new ApiResponse<EmailSenderIdentityResponse>
        {
            Success = true,
            Message = "Sender added.",
            Data = data
        });
    }

    [HttpPut("{id:int}/senders/{senderId:int}")]
    [RequiresPermission("EmailConnection.Edit")]
    public async Task<IActionResult> UpdateSender(
        int id,
        int senderId,
        [FromBody] SaveEmailSenderIdentityRequest request,
        CancellationToken ct)
    {
        var data = await _service.UpdateSenderAsync(id, senderId, request, ct);
        return Ok(new ApiResponse<EmailSenderIdentityResponse>
        {
            Success = true,
            Message = "Sender updated.",
            Data = data
        });
    }

    /// <summary>
    /// Re-reads one sender address's verification state from the provider.
    ///
    /// <para>
    /// The send gate also re-checks a stale status on its own, so this is not the only way a
    /// newly verified address becomes usable — it is the way to make it usable *now*, for an
    /// operator who has just verified in the SES console and does not want to guess whether the
    /// product has noticed.
    /// </para>
    /// </summary>
    [HttpPost("{id:int}/senders/{senderId:int}/refresh")]
    [RequiresPermission("EmailConnection.Edit")]
    public async Task<IActionResult> RefreshSender(int id, int senderId, CancellationToken ct)
    {
        var (status, reason) = await _domainService.RefreshSenderStatusAsync(senderId, ct);

        // The whole sender list comes back, not just the status, so the caller's dropdown reflects
        // the change without a second round trip to work out what CanSend is now.
        var senders = await _service.GetSendersAsync(id, ct);

        return Ok(new ApiResponse<List<EmailSenderIdentityResponse>>
        {
            Success = true,
            Message = reason ?? $"Sender is {status}.",
            Data = senders
        });
    }

    [HttpDelete("{id:int}/senders/{senderId:int}")]
    [RequiresPermission("EmailConnection.Edit")]
    public async Task<IActionResult> DeleteSender(int id, int senderId, CancellationToken ct)
    {
        await _service.DeleteSenderAsync(id, senderId, ct);
        return Ok(new ApiResponse { Success = true, Message = "Sender removed." });
    }

    /// <summary>Deactivates the connection and clears its stored credentials.</summary>
    [HttpPost("{id:int}/disconnect")]
    [RequiresPermission("EmailConnection.Disconnect")]
    public async Task<IActionResult> Disconnect(int id, CancellationToken ct)
    {
        await _service.DisconnectAsync(id, ct);
        return Ok(new ApiResponse { Success = true, Message = "Email connection disconnected." });
    }

    /// <summary>
    /// Saves IMAP credentials for inbound reply polling. Blank password keeps the stored value,
    /// so the operator can update the host without re-typing the credential.
    /// </summary>
    [HttpPut("{id:int}/imap")]
    [RequiresPermission("EmailConnection.Edit")]
    public async Task<IActionResult> SaveImapSettings(
        int id,
        [FromBody] SaveImapSettingsRequest request,
        CancellationToken ct)
    {
        var data = await _service.SaveImapSettingsAsync(id, request, ct);
        return Ok(new ApiResponse<EmailConnectionResponse>
        {
            Success = true,
            Message = "IMAP settings saved. Use POST /test-imap to verify connectivity.",
            Data = data
        });
    }

    /// <summary>
    /// Opens a real IMAP connection to verify that the stored credentials work.
    /// Sends nothing; safe to call at any time.
    /// </summary>
    [HttpPost("{id:int}/test-imap")]
    [RequiresPermission("EmailConnection.Test")]
    public async Task<IActionResult> TestImapConnection(int id, CancellationToken ct)
    {
        var result = await _service.TestImapConnectionAsync(id, ct);
        return Ok(new ApiResponse<EmailProviderTestResponse>
        {
            Success = result.Success,
            Message = result.Message,
            Data = result
        });
    }

    [HttpDelete("{id:int}")]
    [RequiresPermission("EmailConnection.Delete")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _service.DeleteAsync(id, ct);
        return Ok(new ApiResponse { Success = true, Message = "Email connection deleted." });
    }
}
