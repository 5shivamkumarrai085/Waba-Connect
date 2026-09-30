using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.Options;
using WhatsAppCampaignApi.Services.Email;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Controllers;

/// <summary>
/// The platform SMTP account used for system mail (<see cref="SmtpOptions"/>): whether it is
/// configured, and a test send to the signed-in administrator. It is configured through the
/// <c>Smtp</c> settings (environment variables or the local secrets file), never through the API,
/// so the password never passes through a browser.
/// </summary>
[ApiController]
[Route("api/system-mail")]
[Authorize]
public class SystemMailController(IEmailSender sender, IOptions<SmtpOptions> smtp, ICurrentUserService currentUser) : ControllerBase
{
    public sealed record SystemMailStatus(bool Enabled, string? Host, int Port, string? FromAddress, string FromName, string Security);

    [HttpGet("status")]
    [RequiresPermission("OmniSettings.View")]
    public ActionResult<ApiResponse<SystemMailStatus>> Status()
    {
        var o = smtp.Value;
        return Ok(new ApiResponse<SystemMailStatus>
        {
            Success = true,
            Data = new SystemMailStatus(
                sender.IsEnabled,
                string.IsNullOrWhiteSpace(o.Host) ? null : o.Host,
                o.Port,
                string.IsNullOrWhiteSpace(o.ResolvedFromAddress) ? null : o.ResolvedFromAddress,
                o.FromName,
                o.Port == 465 ? "Implicit TLS (SSL on connect)" : "STARTTLS (required)")
        });
    }

    /// <summary>Sends a short test message to the signed-in user's own address.</summary>
    [HttpPost("test")]
    [RequiresPermission("OmniSettings.Edit")]
    public async Task<ActionResult<ApiResponse>> Test(CancellationToken ct)
    {
        if (!sender.IsEnabled)
            return Ok(new ApiResponse { Success = false, Message = "Platform SMTP is not configured. Set the Smtp settings (Smtp__Host, Smtp__FromAddress…) and restart the API." });

        var to = currentUser.Email;
        if (string.IsNullOrWhiteSpace(to))
            return Ok(new ApiResponse { Success = false, Message = "Your account has no email address to send the test to." });

        var name = smtp.Value.FromName;
        var sent = await sender.SendAsync(
            to, currentUser.UserName ?? to,
            $"{name}: system mail test",
            $"<p>This test confirms that {System.Net.WebUtility.HtmlEncode(name)} can send system mail (welcome emails and notifications).</p>",
            $"This test confirms that {name} can send system mail (welcome emails and notifications).",
            ct);

        return Ok(new ApiResponse
        {
            Success = sent,
            Message = sent ? $"Test email sent to {to}." : "The mail server did not accept the test email. The API log has the reason."
        });
    }
}
