using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.Email;
using WhatsAppCampaignApi.Services.Email;

namespace WhatsAppCampaignApi.Controllers;

/// <summary>
/// Sending-domain authentication: SPF, DKIM and DMARC.
///
/// <para>
/// A separate permission from the email connection itself, because publishing DNS records is
/// usually somebody else's job — and whoever does it should not also need access to the sending
/// credentials.
/// </para>
/// </summary>
[ApiController]
[Route("api/email/domains")]
[Authorize]
public class EmailDomainsController : ControllerBase
{
    private readonly IEmailDomainService _service;

    public EmailDomainsController(IEmailDomainService service)
    {
        _service = service;
    }

    [HttpGet]
    [RequiresPermission("EmailDomain.View")]
    public async Task<IActionResult> GetAll([FromQuery] int emailConfigurationId, CancellationToken ct)
    {
        var data = await _service.GetAllAsync(emailConfigurationId, ct);
        return Ok(new ApiResponse<List<EmailSendingDomainResponse>> { Success = true, Data = data });
    }

    [HttpGet("{id:int}")]
    [RequiresPermission("EmailDomain.View")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var data = await _service.GetByIdAsync(id, ct);
        return Ok(new ApiResponse<EmailSendingDomainResponse> { Success = true, Data = data });
    }

    /// <summary>
    /// Registers the domain with the provider. The response carries the DNS records that must be
    /// published before verification can complete.
    /// </summary>
    [HttpPost]
    [RequiresPermission("EmailDomain.Manage")]
    public async Task<IActionResult> Provision([FromBody] ProvisionDomainRequest request, CancellationToken ct)
    {
        var data = await _service.ProvisionAsync(request.EmailConfigurationId, request.DomainName, ct);
        return Ok(new ApiResponse<EmailSendingDomainResponse>
        {
            Success = true,
            Message = "Domain added. Publish the DNS records below, then refresh to check verification.",
            Data = data
        });
    }

    /// <summary>
    /// Re-reads verification state from the provider. This is what the setup screen polls — DNS
    /// propagation takes minutes to hours, so the status cannot simply be assumed after saving.
    /// </summary>
    [HttpPost("{id:int}/refresh")]
    [RequiresPermission("EmailDomain.View")]
    public async Task<IActionResult> Refresh(int id, CancellationToken ct)
    {
        var data = await _service.RefreshStatusAsync(id, ct);
        return Ok(new ApiResponse<EmailSendingDomainResponse>
        {
            Success = true,
            Message = data.VerificationStatus == "Verified"
                ? $"{data.DomainName} is verified and ready to send."
                : $"{data.DomainName} is {data.VerificationStatus}. DNS changes can take a while to propagate.",
            Data = data
        });
    }

    [HttpDelete("{id:int}")]
    [RequiresPermission("EmailDomain.Manage")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _service.DeleteAsync(id, ct);
        return Ok(new ApiResponse
        {
            Success = true,
            Message = "Domain removed. The provider-side identity was left in place."
        });
    }
}

public class ProvisionDomainRequest
{
    [Required]
    public int EmailConfigurationId { get; set; }

    [Required, MaxLength(255)]
    public string DomainName { get; set; } = string.Empty;
}
