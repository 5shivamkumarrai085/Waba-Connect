using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsAppCampaignApi.Services.Interfaces;

using WhatsAppCampaignApi.Helpers;

namespace WhatsAppCampaignApi.Controllers;

[ApiController]
[Route("api/bot")]
[Authorize]
public class BotController : ControllerBase
{
    private readonly IFlowExecutionService _flowExecutionService;

    public BotController(IFlowExecutionService flowExecutionService)
    {
        _flowExecutionService = flowExecutionService;
    }

    [HttpPost("test")]
    [RequiresPermission("BotFlow.Edit")]
    public async Task<IActionResult> TestBotFlow([FromBody] TestBotRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.PhoneNumber) || string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new { success = false, message = "PhoneNumber and Message are required." });
        }

        await _flowExecutionService.ExecuteFlowStepAsync(request.PhoneNumber, request.Message);
        return Ok(new { success = true, message = "Flow executed successfully." });
    }
}

public class TestBotRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
