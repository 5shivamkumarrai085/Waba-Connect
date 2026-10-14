using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Remittance;
using WhatsAppCampaignApi.Services.WhatsApp.Flows;

namespace WhatsAppCampaignApi.Controllers;

/// <summary>Setup › Send Money: countries, payout methods, recipient fields, banks, fees, pick-lists and payment methods.</summary>
[ApiController]
[Authorize]
[Route("api/remittance")]
public class RemittanceController : ControllerBase
{
    private readonly IRemittanceAdminService _admin;

    public RemittanceController(IRemittanceAdminService admin) => _admin = admin;

    private ActionResult<ApiResponse<T>> Data<T>(T data, string? message = null) => Ok(new ApiResponse<T> { Success = true, Data = data, Message = message });

    /// <summary>Providers, field types, lists and limits the settings screens need.</summary>
    [HttpGet("reference")]
    [RequiresPermission("Remittance.View")]
    public ActionResult<ApiResponse<RemittanceReferenceDto>> Reference() => Data(_admin.Reference());

    // corridors
    [HttpGet("corridors")]
    [RequiresPermission("Remittance.View")]
    public async Task<ActionResult<ApiResponse<List<CorridorDto>>>> Corridors(CancellationToken ct) => Data(await _admin.ListCorridorsAsync(ct));

    [HttpPost("corridors")]
    [RequiresPermission("Remittance.Manage")]
    public async Task<ActionResult<ApiResponse<CorridorDto>>> CreateCorridor([FromBody] SaveCorridorRequest request, CancellationToken ct) =>
        Data(await _admin.SaveCorridorAsync(null, request, ct), "Country route added.");

    [HttpPut("corridors/{id:int}")]
    [RequiresPermission("Remittance.Manage")]
    public async Task<ActionResult<ApiResponse<CorridorDto>>> UpdateCorridor(int id, [FromBody] SaveCorridorRequest request, CancellationToken ct) =>
        Data(await _admin.SaveCorridorAsync(id, request, ct), "Country route saved.");

    [HttpDelete("corridors/{id:int}")]
    [RequiresPermission("Remittance.Manage")]
    public async Task<ActionResult<ApiResponse>> DeleteCorridor(int id, CancellationToken ct)
    {
        await _admin.DeleteCorridorAsync(id, ct);
        return Ok(new ApiResponse { Success = true, Message = "Country route deleted." });
    }

    [HttpGet("corridors/{id:int}/rate")]
    [RequiresPermission("Remittance.View")]
    public async Task<ActionResult<ApiResponse<RatePreviewDto>>> Rate(int id, CancellationToken ct) => Data(await _admin.PreviewRateAsync(id, ct));

    // payout methods
    [HttpGet("corridors/{corridorId:int}/payout-methods")]
    [RequiresPermission("Remittance.View")]
    public async Task<ActionResult<ApiResponse<List<PayoutMethodDto>>>> PayoutMethods(int corridorId, CancellationToken ct) =>
        Data(await _admin.ListPayoutMethodsAsync(corridorId, ct));

    [HttpPost("corridors/{corridorId:int}/payout-methods")]
    [RequiresPermission("Remittance.Manage")]
    public async Task<ActionResult<ApiResponse<PayoutMethodDto>>> CreatePayoutMethod(int corridorId, [FromBody] SavePayoutMethodRequest request, CancellationToken ct) =>
        Data(await _admin.SavePayoutMethodAsync(corridorId, null, request, ct), "Payout method added.");

    [HttpPut("corridors/{corridorId:int}/payout-methods/{id:int}")]
    [RequiresPermission("Remittance.Manage")]
    public async Task<ActionResult<ApiResponse<PayoutMethodDto>>> UpdatePayoutMethod(int corridorId, int id, [FromBody] SavePayoutMethodRequest request, CancellationToken ct) =>
        Data(await _admin.SavePayoutMethodAsync(corridorId, id, request, ct), "Payout method saved.");

    [HttpDelete("payout-methods/{id:int}")]
    [RequiresPermission("Remittance.Manage")]
    public async Task<ActionResult<ApiResponse>> DeletePayoutMethod(int id, CancellationToken ct)
    {
        await _admin.DeletePayoutMethodAsync(id, ct);
        return Ok(new ApiResponse { Success = true, Message = "Payout method deleted." });
    }

    // recipient fields
    [HttpGet("payout-methods/{methodId:int}/fields")]
    [RequiresPermission("Remittance.View")]
    public async Task<ActionResult<ApiResponse<List<BeneficiaryFieldDto>>>> Fields(int methodId, CancellationToken ct) => Data(await _admin.ListFieldsAsync(methodId, ct));

    [HttpPost("payout-methods/{methodId:int}/fields")]
    [RequiresPermission("Remittance.Manage")]
    public async Task<ActionResult<ApiResponse<BeneficiaryFieldDto>>> CreateField(int methodId, [FromBody] SaveBeneficiaryFieldRequest request, CancellationToken ct) =>
        Data(await _admin.SaveFieldAsync(methodId, null, request, ct), "Field added.");

    [HttpPut("payout-methods/{methodId:int}/fields/{id:int}")]
    [RequiresPermission("Remittance.Manage")]
    public async Task<ActionResult<ApiResponse<BeneficiaryFieldDto>>> UpdateField(int methodId, int id, [FromBody] SaveBeneficiaryFieldRequest request, CancellationToken ct) =>
        Data(await _admin.SaveFieldAsync(methodId, id, request, ct), "Field saved.");

    [HttpDelete("fields/{id:int}")]
    [RequiresPermission("Remittance.Manage")]
    public async Task<ActionResult<ApiResponse>> DeleteField(int id, CancellationToken ct)
    {
        await _admin.DeleteFieldAsync(id, ct);
        return Ok(new ApiResponse { Success = true, Message = "Field deleted." });
    }

    // banks / providers
    [HttpGet("payout-methods/{methodId:int}/institutions")]
    [RequiresPermission("Remittance.View")]
    public async Task<ActionResult<ApiResponse<List<InstitutionDto>>>> Institutions(int methodId, CancellationToken ct) => Data(await _admin.ListInstitutionsAsync(methodId, ct));

    [HttpPost("payout-methods/{methodId:int}/institutions")]
    [RequiresPermission("Remittance.Manage")]
    public async Task<ActionResult<ApiResponse<InstitutionDto>>> CreateInstitution(int methodId, [FromBody] SaveInstitutionRequest request, CancellationToken ct) =>
        Data(await _admin.SaveInstitutionAsync(methodId, null, request, ct), "Bank added.");

    [HttpPut("payout-methods/{methodId:int}/institutions/{id:int}")]
    [RequiresPermission("Remittance.Manage")]
    public async Task<ActionResult<ApiResponse<InstitutionDto>>> UpdateInstitution(int methodId, int id, [FromBody] SaveInstitutionRequest request, CancellationToken ct) =>
        Data(await _admin.SaveInstitutionAsync(methodId, id, request, ct), "Bank saved.");

    [HttpDelete("institutions/{id:int}")]
    [RequiresPermission("Remittance.Manage")]
    public async Task<ActionResult<ApiResponse>> DeleteInstitution(int id, CancellationToken ct)
    {
        await _admin.DeleteInstitutionAsync(id, ct);
        return Ok(new ApiResponse { Success = true, Message = "Bank deleted." });
    }

    // pick-lists: purpose, source_of_funds, relationship
    [HttpGet("lookups/{category}")]
    [RequiresPermission("Remittance.View")]
    public async Task<ActionResult<ApiResponse<List<LookupDto>>>> Lookups(string category, CancellationToken ct) => Data(await _admin.ListLookupsAsync(category, ct));

    [HttpPost("lookups/{category}")]
    [RequiresPermission("Remittance.Manage")]
    public async Task<ActionResult<ApiResponse<LookupDto>>> CreateLookup(string category, [FromBody] SaveLookupRequest request, CancellationToken ct) =>
        Data(await _admin.SaveLookupAsync(category, null, request, ct), "Item added.");

    [HttpPut("lookups/{category}/{id:int}")]
    [RequiresPermission("Remittance.Manage")]
    public async Task<ActionResult<ApiResponse<LookupDto>>> UpdateLookup(string category, int id, [FromBody] SaveLookupRequest request, CancellationToken ct) =>
        Data(await _admin.SaveLookupAsync(category, id, request, ct), "Item saved.");

    [HttpDelete("lookups/{id:int}")]
    [RequiresPermission("Remittance.Manage")]
    public async Task<ActionResult<ApiResponse>> DeleteLookup(int id, CancellationToken ct)
    {
        await _admin.DeleteLookupAsync(id, ct);
        return Ok(new ApiResponse { Success = true, Message = "Item deleted." });
    }

    // payment methods
    [HttpGet("payment-methods")]
    [RequiresPermission("Remittance.View")]
    public async Task<ActionResult<ApiResponse<List<PaymentMethodDto>>>> PaymentMethods(CancellationToken ct) => Data(await _admin.ListPaymentMethodsAsync(ct));

    [HttpPost("payment-methods")]
    [RequiresPermission("Remittance.Manage")]
    public async Task<ActionResult<ApiResponse<PaymentMethodDto>>> CreatePaymentMethod([FromBody] SavePaymentMethodRequest request, CancellationToken ct) =>
        Data(await _admin.SavePaymentMethodAsync(null, request, ct), "Payment method added.");

    [HttpPut("payment-methods/{id:int}")]
    [RequiresPermission("Remittance.Manage")]
    public async Task<ActionResult<ApiResponse<PaymentMethodDto>>> UpdatePaymentMethod(int id, [FromBody] SavePaymentMethodRequest request, CancellationToken ct) =>
        Data(await _admin.SavePaymentMethodAsync(id, request, ct), "Payment method saved.");

    [HttpDelete("payment-methods/{id:int}")]
    [RequiresPermission("Remittance.Manage")]
    public async Task<ActionResult<ApiResponse>> DeletePaymentMethod(int id, CancellationToken ct)
    {
        await _admin.DeletePaymentMethodAsync(id, ct);
        return Ok(new ApiResponse { Success = true, Message = "Payment method deleted." });
    }

    // transfers
    [HttpGet("orders")]
    [RequiresPermission("Transfer.View")]
    public async Task<ActionResult<ApiResponse<PagedResponse<TransferOrderSummaryDto>>>> Orders(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, [FromQuery] string? search = null,
        [FromQuery] TransferOrderStatus? orderStatus = null, [FromQuery] TransferPaymentStatus? paymentStatus = null, CancellationToken ct = default) =>
        Data(await _admin.ListOrdersAsync(page, pageSize, search, orderStatus, paymentStatus, ct));

    [HttpGet("orders/{id:int}")]
    [RequiresPermission("Transfer.View")]
    public async Task<ActionResult<ApiResponse<TransferOrderDetailDto>>> Order(int id, CancellationToken ct) => Data(await _admin.GetOrderAsync(id, ct));
}

/// <summary>Per-connection WhatsApp Flows set-up: the encryption key, the Send Money Flow, publishing and a test send.</summary>
[ApiController]
[Authorize]
[Route("api/connections/{connectionId:int}/whatsapp-flows")]
public class WhatsAppFlowsController : ControllerBase
{
    private readonly IWhatsAppFlowsService _flows;
    private readonly IWhatsAppFlowLauncher _launcher;
    private readonly Data.AppDbContext _db;

    public WhatsAppFlowsController(IWhatsAppFlowsService flows, IWhatsAppFlowLauncher launcher, Data.AppDbContext db)
    {
        _flows = flows;
        _launcher = launcher;
        _db = db;
    }

    private ActionResult<ApiResponse<WhatsAppFlowsStatusDto>> Status(WhatsAppFlowsStatusDto status, string? message = null) =>
        Ok(new ApiResponse<WhatsAppFlowsStatusDto> { Success = true, Data = status, Message = message });

    [HttpGet]
    [RequiresPermission("ConnectAccount.View")]
    public async Task<ActionResult<ApiResponse<WhatsAppFlowsStatusDto>>> Get(int connectionId, CancellationToken ct) =>
        Status(await _flows.GetStatusAsync(connectionId, ct));

    /// <summary>Creates the key pair if there is none (or a fresh one with regenerate=true) and uploads the public key to Meta.</summary>
    [HttpPost("encryption-key")]
    [RequiresPermission("ConnectAccount.Connect")]
    public async Task<ActionResult<ApiResponse<WhatsAppFlowsStatusDto>>> Key(int connectionId, [FromQuery] bool regenerate = false, CancellationToken ct = default) =>
        Status(await _flows.EnsureKeyAsync(connectionId, regenerate, ct), "Encryption key uploaded to WhatsApp.");

    /// <summary>Creates or updates the Send Money Flow on Meta (createNew=true makes a fresh draft).</summary>
    [HttpPost("send-money/sync")]
    [RequiresPermission("ConnectAccount.Connect")]
    public async Task<ActionResult<ApiResponse<WhatsAppFlowsStatusDto>>> Sync(int connectionId, [FromQuery] bool createNew = false, CancellationToken ct = default) =>
        Status(await _flows.SyncSendMoneyAsync(connectionId, createNew, ct), "Send Money Flow synced with WhatsApp.");

    [HttpPost("send-money/publish")]
    [RequiresPermission("ConnectAccount.Connect")]
    public async Task<ActionResult<ApiResponse<WhatsAppFlowsStatusDto>>> Publish(int connectionId, CancellationToken ct) =>
        Status(await _flows.PublishSendMoneyAsync(connectionId, ct), "Send Money Flow published.");

    public sealed record LinkFlowRequest(string FlowId);

    /// <summary>Uses a Flow built in WhatsApp Manager (same screens as send_money.flow.json) as this connection's Send Money Flow.</summary>
    [HttpPost("send-money/link")]
    [RequiresPermission("ConnectAccount.Connect")]
    public async Task<ActionResult<ApiResponse<WhatsAppFlowsStatusDto>>> Link(int connectionId, [FromBody] LinkFlowRequest request, CancellationToken ct) =>
        Status(await _flows.LinkSendMoneyAsync(connectionId, request.FlowId, ct), "Flow linked to this connection.");

    /// <summary>The exact Flow JSON uploaded to Meta.</summary>
    [HttpGet("send-money/flow-json")]
    [RequiresPermission("ConnectAccount.View")]
    public IActionResult FlowJson(int connectionId) => Content(_flows.SendMoneyFlowJson(), "application/json");

    public sealed record TestSendRequest(string Phone);

    /// <summary>Sends the Send Money Flow to an existing contact's WhatsApp number.</summary>
    [HttpPost("send-money/test-send")]
    [RequiresPermission("ConnectAccount.Connect")]
    public async Task<ActionResult<ApiResponse<FlowLaunchResult>>> TestSend(int connectionId, [FromBody] TestSendRequest request, CancellationToken ct)
    {
        if (!PhoneNumberHelper.TryNormalize(request.Phone, out var phone, out var problem)) throw new ArgumentException(problem ?? "Enter a valid phone number.");
        var bare = PhoneNumberHelper.FormatForWhatsApp(phone);
        var contact = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
            _db.Contacts, c => c.Phone == phone || c.Phone == bare, ct)
            ?? throw new KeyNotFoundException("No contact has that number. Add the contact first, or message the business from that phone.");

        var result = await _launcher.LaunchSendMoneyAsync(contact.Id, connectionId,
            new FlowInvitation("International Remittance", "Send money abroad in a few taps, with a guaranteed rate.", "Powered by WhatsApp Flows", "Send money"), ct);
        return Ok(new ApiResponse<FlowLaunchResult> { Success = result.Sent, Data = result, Message = result.Sent ? "Flow sent." : result.Error });
    }
}
