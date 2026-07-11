using Microsoft.AspNetCore.Mvc;
using WhatsAppCampaignApi.Models.DTOs.Webhook;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Controllers;

[ApiController]
[Route("api/webhook/whatsapp")]
public class WebhookController : ControllerBase
{
    private readonly IWhatsAppService _whatsAppService;

    public WebhookController(IWhatsAppService whatsAppService)
    {
        _whatsAppService = whatsAppService;
    }

    /// <summary>
    /// Verification challenge from Meta
    /// </summary>
    [HttpGet]
    public IActionResult VerifyWebhook(
        [FromQuery(Name = "hub.mode")] string mode,
        [FromQuery(Name = "hub.challenge")] string challenge,
        [FromQuery(Name = "hub.verify_token")] string token)
    {
        var isValid = _whatsAppService.VerifyWebhook(mode, token, challenge);
        
        if (isValid)
            return Ok(challenge); // Meta expects the raw challenge string back
            
        return Forbid();
    }

    /// <summary>
    /// Event notifications (message status updates) from Meta
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ReceiveWebhook([FromBody] WhatsAppWebhookPayload payload)
    {
        // Process inside the request scope so DbContext-backed webhook updates are reliable.
        await _whatsAppService.ProcessWebhookAsync(payload);
        return Ok();
    }
}
