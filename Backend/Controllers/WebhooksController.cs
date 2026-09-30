using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Services.Integrations;

namespace WhatsAppCampaignApi.Controllers;

/// <summary>Outbound, signed webhooks: subscriptions, their delivery log, test and replay.</summary>
[ApiController]
[Authorize]
[Route("api/webhooks")]
public class WebhooksController : ControllerBase
{
    private readonly IWebhookSubscriptionService _webhooks;

    public WebhooksController(IWebhookSubscriptionService webhooks) => _webhooks = webhooks;

    [HttpGet]
    [RequiresPermission("Webhook.View")]
    public async Task<ActionResult<ApiResponse<List<WebhookSubscriptionDto>>>> List(CancellationToken ct) =>
        Ok(new ApiResponse<List<WebhookSubscriptionDto>> { Success = true, Data = await _webhooks.ListAsync(ct) });

    /// <summary>The events a webhook can subscribe to.</summary>
    [HttpGet("event-types")]
    [RequiresPermission("Webhook.View")]
    public ActionResult<ApiResponse<IReadOnlyList<WebhookEventCatalog.EventType>>> EventTypes() =>
        Ok(new ApiResponse<IReadOnlyList<WebhookEventCatalog.EventType>> { Success = true, Data = WebhookEventCatalog.All });

    /// <summary>Creates a webhook. The response carries the signing secret, shown this once.</summary>
    [HttpPost]
    [RequiresPermission("Webhook.Manage")]
    public async Task<ActionResult<ApiResponse<WebhookSubscriptionDto>>> Create([FromBody] SaveWebhookRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<WebhookSubscriptionDto> { Success = true, Data = await _webhooks.CreateAsync(request, ct), Message = "Webhook created." });

    [HttpPut("{id:int}")]
    [RequiresPermission("Webhook.Manage")]
    public async Task<ActionResult<ApiResponse<WebhookSubscriptionDto>>> Update(int id, [FromBody] SaveWebhookRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<WebhookSubscriptionDto> { Success = true, Data = await _webhooks.UpdateAsync(id, request, ct), Message = "Webhook saved." });

    [HttpDelete("{id:int}")]
    [RequiresPermission("Webhook.Manage")]
    public async Task<ActionResult<ApiResponse>> Delete(int id, CancellationToken ct)
    {
        await _webhooks.DeleteAsync(id, ct);
        return Ok(new ApiResponse { Success = true, Message = "Webhook deleted." });
    }

    /// <summary>Issues a new signing secret; the old one stops working at once.</summary>
    [HttpPost("{id:int}/rotate-secret")]
    [RequiresPermission("Webhook.Manage")]
    public async Task<ActionResult<ApiResponse<WebhookSubscriptionDto>>> RotateSecret(int id, CancellationToken ct) =>
        Ok(new ApiResponse<WebhookSubscriptionDto> { Success = true, Data = await _webhooks.RotateSecretAsync(id, ct), Message = "New secret issued." });

    /// <summary>Sends a test event now and returns what the receiver answered.</summary>
    [HttpPost("{id:int}/test")]
    [RequiresPermission("Webhook.Manage")]
    public async Task<ActionResult<ApiResponse<WebhookDeliveryDto>>> Test(int id, CancellationToken ct)
    {
        var delivery = await _webhooks.SendTestAsync(id, ct);
        return Ok(new ApiResponse<WebhookDeliveryDto>
        {
            Success = delivery.Status == "Delivered",
            Data = delivery,
            Message = delivery.Status == "Delivered" ? $"Delivered — the receiver answered {delivery.ResponseCode}." : delivery.Error ?? "The test failed."
        });
    }

    [HttpGet("{id:int}/deliveries")]
    [RequiresPermission("Webhook.View")]
    public async Task<ActionResult<ApiResponse<PagedResponse<WebhookDeliveryDto>>>> Deliveries(
        int id, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, [FromQuery] string? status = null, CancellationToken ct = default) =>
        Ok(new ApiResponse<PagedResponse<WebhookDeliveryDto>> { Success = true, Data = await _webhooks.GetDeliveriesAsync(id, page, pageSize, status, ct) });

    /// <summary>Sends a delivery again, with the same event id.</summary>
    [HttpPost("deliveries/{deliveryId:long}/replay")]
    [RequiresPermission("Webhook.Manage")]
    public async Task<ActionResult<ApiResponse<WebhookDeliveryDto>>> Replay(long deliveryId, CancellationToken ct) =>
        Ok(new ApiResponse<WebhookDeliveryDto> { Success = true, Data = await _webhooks.ReplayAsync(deliveryId, ct), Message = "Queued to send again." });
}
