using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Services.Remittance.Providers;

namespace WhatsAppCampaignApi.Services.Remittance;

// ── DTOs ─────────────────────────────────────────────────────────────────────────────────────

public sealed record CorridorDto(int Id, string SourceCountryCode, string SourceCurrency, string DestinationCountryCode, string DestinationCountryName,
    string DestinationCurrency, int DestinationDecimals, decimal MinSendAmount, decimal MaxSendAmount, int FxMarginBps, bool IsActive, int SortOrder,
    int PayoutMethodCount);

public sealed record SaveCorridorRequest(string SourceCountryCode, string SourceCurrency, string DestinationCountryCode, string DestinationCountryName,
    string DestinationCurrency, int DestinationDecimals, decimal MinSendAmount, decimal MaxSendAmount, int FxMarginBps, bool IsActive, int SortOrder);

public sealed record FeeRuleDto(decimal FixedFee, decimal PercentFee, decimal? MinFee, decimal? MaxFee, decimal? FirstTransferFee, string? PromoLabel);

public sealed record PayoutMethodDto(int Id, int CorridorId, string Code, string Name, string? DeliveryEta, string PayoutProvider, bool IsActive, int SortOrder,
    FeeRuleDto? Fee, int FieldCount, int InstitutionCount);

public sealed record SavePayoutMethodRequest(string Code, string Name, string? DeliveryEta, string PayoutProvider, bool IsActive, int SortOrder, FeeRuleDto? Fee);

public sealed record BeneficiaryFieldDto(int Id, string Key, string Label, RemittanceFieldInputType InputType, bool IsRequired, int? MinLength, int? MaxLength,
    string? Pattern, string? HelperText, bool IsAccountIdentifier, bool IsActive, int SortOrder);

public sealed record SaveBeneficiaryFieldRequest(string Key, string Label, RemittanceFieldInputType InputType, bool IsRequired, int? MinLength, int? MaxLength,
    string? Pattern, string? HelperText, bool IsAccountIdentifier, bool IsActive, int SortOrder);

public sealed record InstitutionDto(int Id, string Code, string Name, bool IsActive, int SortOrder);
public sealed record SaveInstitutionRequest(string Code, string Name, bool IsActive, int SortOrder);

public sealed record LookupDto(int Id, string Category, string Code, string Label, bool IsActive, int SortOrder);
public sealed record SaveLookupRequest(string Code, string Label, bool IsActive, int SortOrder);

public sealed record PaymentMethodDto(int Id, string SourceCountryCode, string Provider, string Code, string Label, string? Description, string? Badge,
    int PaymentWindowMinutes, bool IsActive, int SortOrder);

public sealed record SavePaymentMethodRequest(string SourceCountryCode, string Provider, string Code, string Label, string? Description, string? Badge,
    int PaymentWindowMinutes, bool IsActive, int SortOrder);

public sealed record RatePreviewDto(int CorridorId, string From, string To, bool Ok, decimal? MidRate, decimal? AppliedRate, DateTime? AsOfUtc, string? Provider, string? Error);

public sealed record RemittanceReferenceDto(
    IReadOnlyCollection<string> PaymentProviders,
    IReadOnlyCollection<string> PayoutProviders,
    IReadOnlyCollection<string> FieldInputTypes,
    IReadOnlyCollection<string> LookupCategories,
    IReadOnlyCollection<string> OrderStatuses,
    IReadOnlyCollection<string> PaymentStatuses,
    IReadOnlyCollection<FlowPurposeDto> FlowPurposes,
    int MaxBeneficiaryFields,
    bool Enabled);

public sealed record FlowPurposeDto(string Value, string Label);

public sealed record TransferOrderSummaryDto(int Id, string Reference, string ContactName, string ContactPhone, string BeneficiaryName, string? InstitutionName,
    string PayoutMethodName, string SendCurrency, decimal SendAmount, decimal TotalPayable, string ReceiveCurrency, decimal ReceiveAmount,
    TransferOrderStatus OrderStatus, TransferPaymentStatus PaymentStatus, string PaymentMethodCode, string PaymentProvider, DateTime CreatedAt, DateTime? PaidAt,
    DateTime? CompletedAt);

public sealed record TransferOrderEventDto(long Id, string Kind, string? FromStatus, string? ToStatus, string Source, string? Provider, string? ProviderEventId,
    string? Detail, DateTime OccurredAt);

public sealed record TransferOrderDetailDto(TransferOrderSummaryDto Order, string? MaskedAccount, decimal FeeAmount, decimal AppliedRate,
    string? SourceOfFunds, string? Purpose, string? Remarks, string? ProviderPaymentId, string? PaymentUrl, string? PaymentReference, DateTime? PaymentExpiresAt,
    string PayoutProvider, string? PayoutReference, IReadOnlyList<TransferOrderEventDto> Events);

// ── Service ──────────────────────────────────────────────────────────────────────────────────

public interface IRemittanceAdminService
{
    Task<List<CorridorDto>> ListCorridorsAsync(CancellationToken ct);
    Task<CorridorDto> SaveCorridorAsync(int? id, SaveCorridorRequest request, CancellationToken ct);
    Task DeleteCorridorAsync(int id, CancellationToken ct);

    Task<List<PayoutMethodDto>> ListPayoutMethodsAsync(int corridorId, CancellationToken ct);
    Task<PayoutMethodDto> SavePayoutMethodAsync(int corridorId, int? id, SavePayoutMethodRequest request, CancellationToken ct);
    Task DeletePayoutMethodAsync(int id, CancellationToken ct);

    Task<List<BeneficiaryFieldDto>> ListFieldsAsync(int payoutMethodId, CancellationToken ct);
    Task<BeneficiaryFieldDto> SaveFieldAsync(int payoutMethodId, int? id, SaveBeneficiaryFieldRequest request, CancellationToken ct);
    Task DeleteFieldAsync(int id, CancellationToken ct);

    Task<List<InstitutionDto>> ListInstitutionsAsync(int payoutMethodId, CancellationToken ct);
    Task<InstitutionDto> SaveInstitutionAsync(int payoutMethodId, int? id, SaveInstitutionRequest request, CancellationToken ct);
    Task DeleteInstitutionAsync(int id, CancellationToken ct);

    Task<List<LookupDto>> ListLookupsAsync(string category, CancellationToken ct);
    Task<LookupDto> SaveLookupAsync(string category, int? id, SaveLookupRequest request, CancellationToken ct);
    Task DeleteLookupAsync(int id, CancellationToken ct);

    Task<List<PaymentMethodDto>> ListPaymentMethodsAsync(CancellationToken ct);
    Task<PaymentMethodDto> SavePaymentMethodAsync(int? id, SavePaymentMethodRequest request, CancellationToken ct);
    Task DeletePaymentMethodAsync(int id, CancellationToken ct);

    Task<RatePreviewDto> PreviewRateAsync(int corridorId, CancellationToken ct);
    RemittanceReferenceDto Reference();

    Task<PagedResponse<TransferOrderSummaryDto>> ListOrdersAsync(int page, int pageSize, string? search, TransferOrderStatus? orderStatus,
        TransferPaymentStatus? paymentStatus, CancellationToken ct);
    Task<TransferOrderDetailDto> GetOrderAsync(int id, CancellationToken ct);
}

/// <summary>
/// Setup › Send Money and the Transfers screen. Every save is checked against the limits the
/// WhatsApp Flow can render (label lengths, option counts, six beneficiary-field slots), so
/// configuration that would break on a customer's phone is refused here instead.
/// </summary>
public sealed class RemittanceAdminService : IRemittanceAdminService
{
    private static readonly Regex Country = new("^[A-Z]{2}$");
    private static readonly Regex Currency = new("^[A-Z]{3}$");
    private static readonly Regex UpperCode = new("^[A-Z0-9_]{2,40}$");
    private static readonly Regex LowerCode = new("^[a-z0-9_]{1,40}$");
    private static readonly Regex InstitutionCode = new("^[A-Za-z0-9_\\-]{1,40}$");

    private readonly AppDbContext _db;
    private readonly IRemittanceProviderRegistry _providers;
    private readonly IExchangeRateService _rates;
    private readonly IAuditService _audit;
    private readonly Microsoft.Extensions.Options.IOptionsMonitor<Models.Options.RemittanceOptions> _options;

    public RemittanceAdminService(AppDbContext db, IRemittanceProviderRegistry providers, IExchangeRateService rates, IAuditService audit,
        Microsoft.Extensions.Options.IOptionsMonitor<Models.Options.RemittanceOptions> options)
    {
        _db = db;
        _providers = providers;
        _rates = rates;
        _audit = audit;
        _options = options;
    }

    // ── corridors ─────────────────────────────────────────────────────────────────────────────

    public Task<List<CorridorDto>> ListCorridorsAsync(CancellationToken ct) =>
        _db.RemittanceCorridors.AsNoTracking()
            .OrderBy(c => c.SortOrder).ThenBy(c => c.DestinationCountryName)
            .Select(c => new CorridorDto(c.Id, c.SourceCountryCode, c.SourceCurrency, c.DestinationCountryCode, c.DestinationCountryName,
                c.DestinationCurrency, c.DestinationDecimals, c.MinSendAmount, c.MaxSendAmount, c.FxMarginBps, c.IsActive, c.SortOrder, c.PayoutMethods.Count))
            .ToListAsync(ct);

    public async Task<CorridorDto> SaveCorridorAsync(int? id, SaveCorridorRequest r, CancellationToken ct)
    {
        var source = Upper(r.SourceCountryCode);
        var sourceCurrency = Upper(r.SourceCurrency);
        var destination = Upper(r.DestinationCountryCode);
        var destinationCurrency = Upper(r.DestinationCurrency);
        var name = Clean(r.DestinationCountryName);

        if (!Country.IsMatch(source) || !Country.IsMatch(destination)) throw new ArgumentException("Countries are two-letter ISO codes, e.g. MY, ID.");
        if (!Currency.IsMatch(sourceCurrency) || !Currency.IsMatch(destinationCurrency)) throw new ArgumentException("Currencies are three-letter ISO codes, e.g. MYR, IDR.");
        RequireLength(name, 1, 30, "Country name");
        if (r.DestinationDecimals is < 0 or > 4) throw new ArgumentException("Receive-currency decimals must be between 0 and 4.");
        if (r.MinSendAmount <= 0 || r.MaxSendAmount < r.MinSendAmount) throw new ArgumentException("The minimum must be above zero and not above the maximum.");
        if (r.FxMarginBps is < 0 or > 2000) throw new ArgumentException("The FX margin must be between 0 and 2,000 basis points (20%).");

        var corridor = id is { } existingId
            ? await _db.RemittanceCorridors.FirstOrDefaultAsync(c => c.Id == existingId, ct) ?? throw new KeyNotFoundException("Country route not found.")
            : new RemittanceCorridor();
        corridor.SourceCountryCode = source;
        corridor.SourceCurrency = sourceCurrency;
        corridor.DestinationCountryCode = destination;
        corridor.DestinationCountryName = name;
        corridor.DestinationCurrency = destinationCurrency;
        corridor.DestinationDecimals = r.DestinationDecimals;
        corridor.MinSendAmount = RemittancePricing.Money(r.MinSendAmount);
        corridor.MaxSendAmount = RemittancePricing.Money(r.MaxSendAmount);
        corridor.FxMarginBps = r.FxMarginBps;
        corridor.IsActive = r.IsActive;
        corridor.SortOrder = r.SortOrder;
        if (id is null) _db.RemittanceCorridors.Add(corridor);

        await _db.SaveChangesAsync(ct);
        await AuditAsync(id is null ? "Created" : "Updated", "RemittanceCorridor", corridor.Id, $"{source} → {name} ({destinationCurrency})");
        return (await ListCorridorsAsync(ct)).First(c => c.Id == corridor.Id);
    }

    public async Task DeleteCorridorAsync(int id, CancellationToken ct)
    {
        if (await _db.TransferOrders.AnyAsync(o => o.CorridorId == id, ct))
            throw new InvalidOperationException("This route has transfers on record; deactivate it instead of deleting.");
        var deleted = await _db.RemittanceCorridors.Where(c => c.Id == id).ExecuteDeleteAsync(ct);
        if (deleted == 0) throw new KeyNotFoundException("Country route not found.");
        await AuditAsync("Deleted", "RemittanceCorridor", id, null);
    }

    // ── payout methods ───────────────────────────────────────────────────────────────────────

    public Task<List<PayoutMethodDto>> ListPayoutMethodsAsync(int corridorId, CancellationToken ct) =>
        _db.RemittancePayoutMethods.AsNoTracking()
            .Where(m => m.CorridorId == corridorId)
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Name)
            .Select(m => new PayoutMethodDto(m.Id, m.CorridorId, m.Code, m.Name, m.DeliveryEta, m.PayoutProvider, m.IsActive, m.SortOrder,
                m.FeeRule == null ? null : new FeeRuleDto(m.FeeRule.FixedFee, m.FeeRule.PercentFee, m.FeeRule.MinFee, m.FeeRule.MaxFee, m.FeeRule.FirstTransferFee, m.FeeRule.PromoLabel),
                m.Fields.Count(f => f.IsActive), m.Institutions.Count(i => i.IsActive)))
            .ToListAsync(ct);

    public async Task<PayoutMethodDto> SavePayoutMethodAsync(int corridorId, int? id, SavePayoutMethodRequest r, CancellationToken ct)
    {
        if (!await _db.RemittanceCorridors.AnyAsync(c => c.Id == corridorId, ct)) throw new KeyNotFoundException("Country route not found.");

        var code = Upper(r.Code);
        if (!UpperCode.IsMatch(code)) throw new ArgumentException("The code uses capitals, digits and underscores, e.g. BANK_TRANSFER.");
        var name = Clean(r.Name);
        RequireLength(name, 1, 30, "Name");
        var eta = string.IsNullOrWhiteSpace(r.DeliveryEta) ? null : Clean(r.DeliveryEta);
        if (eta is { Length: > 60 }) throw new ArgumentException("Delivery time can be at most 60 characters.");
        if (!_providers.PayoutProviderNames.Contains(r.PayoutProvider, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Unknown payout provider \"{r.PayoutProvider}\". Available: {string.Join(", ", _providers.PayoutProviderNames)}.");
        if (r.Fee is { } fee) ValidateFee(fee);

        var method = id is { } existingId
            ? await _db.RemittancePayoutMethods.Include(m => m.FeeRule).FirstOrDefaultAsync(m => m.Id == existingId && m.CorridorId == corridorId, ct)
              ?? throw new KeyNotFoundException("Payout method not found.")
            : new RemittancePayoutMethod { CorridorId = corridorId };

        method.Code = code;
        method.Name = name;
        method.DeliveryEta = eta;
        method.PayoutProvider = _providers.PayoutProviderNames.First(n => n.Equals(r.PayoutProvider, StringComparison.OrdinalIgnoreCase));
        method.IsActive = r.IsActive;
        method.SortOrder = r.SortOrder;
        if (r.Fee is { } f)
        {
            method.FeeRule ??= new RemittanceFeeRule();
            method.FeeRule.FixedFee = RemittancePricing.Money(f.FixedFee);
            method.FeeRule.PercentFee = Math.Round(f.PercentFee, 4);
            method.FeeRule.MinFee = f.MinFee is { } min ? RemittancePricing.Money(min) : null;
            method.FeeRule.MaxFee = f.MaxFee is { } max ? RemittancePricing.Money(max) : null;
            method.FeeRule.FirstTransferFee = f.FirstTransferFee is { } first ? RemittancePricing.Money(first) : null;
            method.FeeRule.PromoLabel = string.IsNullOrWhiteSpace(f.PromoLabel) ? null : Clean(f.PromoLabel);
        }
        if (id is null) _db.RemittancePayoutMethods.Add(method);

        await _db.SaveChangesAsync(ct);
        await AuditAsync(id is null ? "Created" : "Updated", "RemittancePayoutMethod", method.Id, $"{code} — {name}");
        return (await ListPayoutMethodsAsync(corridorId, ct)).First(m => m.Id == method.Id);
    }

    public async Task DeletePayoutMethodAsync(int id, CancellationToken ct)
    {
        if (await _db.TransferOrders.AnyAsync(o => o.PayoutMethodId == id, ct))
            throw new InvalidOperationException("This payout method has transfers on record; deactivate it instead of deleting.");
        if (await _db.RemittancePayoutMethods.Where(m => m.Id == id).ExecuteDeleteAsync(ct) == 0) throw new KeyNotFoundException("Payout method not found.");
        await AuditAsync("Deleted", "RemittancePayoutMethod", id, null);
    }

    // ── beneficiary fields ───────────────────────────────────────────────────────────────────

    public Task<List<BeneficiaryFieldDto>> ListFieldsAsync(int payoutMethodId, CancellationToken ct) =>
        _db.RemittanceBeneficiaryFields.AsNoTracking()
            .Where(f => f.PayoutMethodId == payoutMethodId)
            .OrderBy(f => f.SortOrder).ThenBy(f => f.Id)
            .Select(f => new BeneficiaryFieldDto(f.Id, f.Key, f.Label, f.InputType, f.IsRequired, f.MinLength, f.MaxLength, f.Pattern, f.HelperText,
                f.IsAccountIdentifier, f.IsActive, f.SortOrder))
            .ToListAsync(ct);

    public async Task<BeneficiaryFieldDto> SaveFieldAsync(int payoutMethodId, int? id, SaveBeneficiaryFieldRequest r, CancellationToken ct)
    {
        if (!await _db.RemittancePayoutMethods.AnyAsync(m => m.Id == payoutMethodId, ct)) throw new KeyNotFoundException("Payout method not found.");

        var key = (r.Key ?? string.Empty).Trim().ToLowerInvariant();
        if (!LowerCode.IsMatch(key) || key.Length < 2) throw new ArgumentException("The key uses lower-case letters, digits and underscores, e.g. account_number.");
        var label = Clean(r.Label);
        RequireLength(label, 1, 20, "Label (WhatsApp shows at most 20 characters)");
        var helper = string.IsNullOrWhiteSpace(r.HelperText) ? null : Clean(r.HelperText);
        if (helper is { Length: > 80 }) throw new ArgumentException("Help text can be at most 80 characters (WhatsApp's limit).");
        if (r.MinLength is < 0 or > 80 || r.MaxLength is < 1 or > 80) throw new ArgumentException("Lengths must be between 0 and 80 characters.");
        if (r.MinLength is { } min && r.MaxLength is { } max && min > max) throw new ArgumentException("The minimum length is above the maximum.");
        var pattern = string.IsNullOrWhiteSpace(r.Pattern) ? null : r.Pattern.Trim();
        if (pattern is not null)
        {
            if (pattern.Length > 200) throw new ArgumentException("The pattern can be at most 200 characters.");
            try { _ = new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(200)); }
            catch (ArgumentException) { throw new ArgumentException("The pattern is not a valid regular expression."); }
        }

        // The Flow has a fixed number of text slots; an extra active field would never be shown.
        var otherActive = await _db.RemittanceBeneficiaryFields.CountAsync(f => f.PayoutMethodId == payoutMethodId && f.IsActive && f.Id != (id ?? 0), ct);
        if (r.IsActive && otherActive >= RemittanceBeneficiaryField.MaxSlots)
            throw new ArgumentException($"A payout method can have at most {RemittanceBeneficiaryField.MaxSlots} active recipient fields — WhatsApp's form has that many slots.");

        var field = id is { } existingId
            ? await _db.RemittanceBeneficiaryFields.FirstOrDefaultAsync(f => f.Id == existingId && f.PayoutMethodId == payoutMethodId, ct)
              ?? throw new KeyNotFoundException("Field not found.")
            : new RemittanceBeneficiaryField { PayoutMethodId = payoutMethodId };

        field.Key = key;
        field.Label = label;
        field.InputType = r.InputType;
        field.IsRequired = r.IsRequired;
        field.MinLength = r.MinLength;
        field.MaxLength = r.MaxLength;
        field.Pattern = pattern;
        field.HelperText = helper;
        field.IsAccountIdentifier = r.IsAccountIdentifier;
        field.IsActive = r.IsActive;
        field.SortOrder = r.SortOrder;
        if (id is null) _db.RemittanceBeneficiaryFields.Add(field);

        await _db.SaveChangesAsync(ct);
        await AuditAsync(id is null ? "Created" : "Updated", "RemittanceBeneficiaryField", field.Id, $"{key} — {label}");
        return (await ListFieldsAsync(payoutMethodId, ct)).First(f => f.Id == field.Id);
    }

    public async Task DeleteFieldAsync(int id, CancellationToken ct)
    {
        if (await _db.RemittanceBeneficiaryFields.Where(f => f.Id == id).ExecuteDeleteAsync(ct) == 0) throw new KeyNotFoundException("Field not found.");
        await AuditAsync("Deleted", "RemittanceBeneficiaryField", id, null);
    }

    // ── institutions ─────────────────────────────────────────────────────────────────────────

    public Task<List<InstitutionDto>> ListInstitutionsAsync(int payoutMethodId, CancellationToken ct) =>
        _db.RemittanceInstitutions.AsNoTracking()
            .Where(i => i.PayoutMethodId == payoutMethodId)
            .OrderBy(i => i.SortOrder).ThenBy(i => i.Name)
            .Select(i => new InstitutionDto(i.Id, i.Code, i.Name, i.IsActive, i.SortOrder))
            .ToListAsync(ct);

    public async Task<InstitutionDto> SaveInstitutionAsync(int payoutMethodId, int? id, SaveInstitutionRequest r, CancellationToken ct)
    {
        if (!await _db.RemittancePayoutMethods.AnyAsync(m => m.Id == payoutMethodId, ct)) throw new KeyNotFoundException("Payout method not found.");
        var code = (r.Code ?? string.Empty).Trim();
        if (!InstitutionCode.IsMatch(code)) throw new ArgumentException("The code uses letters, digits, '-' and '_' (up to 40).");
        var name = Clean(r.Name);
        RequireLength(name, 1, 30, "Name (WhatsApp shows at most 30 characters)");

        var activeCount = await _db.RemittanceInstitutions.CountAsync(i => i.PayoutMethodId == payoutMethodId && i.IsActive && i.Id != (id ?? 0), ct);
        if (r.IsActive && activeCount >= 200) throw new ArgumentException("A WhatsApp list can show at most 200 banks or providers.");

        var institution = id is { } existingId
            ? await _db.RemittanceInstitutions.FirstOrDefaultAsync(i => i.Id == existingId && i.PayoutMethodId == payoutMethodId, ct)
              ?? throw new KeyNotFoundException("Bank or provider not found.")
            : new RemittanceInstitution { PayoutMethodId = payoutMethodId };
        institution.Code = code;
        institution.Name = name;
        institution.IsActive = r.IsActive;
        institution.SortOrder = r.SortOrder;
        if (id is null) _db.RemittanceInstitutions.Add(institution);

        await _db.SaveChangesAsync(ct);
        await AuditAsync(id is null ? "Created" : "Updated", "RemittanceInstitution", institution.Id, name);
        return new InstitutionDto(institution.Id, institution.Code, institution.Name, institution.IsActive, institution.SortOrder);
    }

    public async Task DeleteInstitutionAsync(int id, CancellationToken ct)
    {
        if (await _db.RemittanceBeneficiaries.AnyAsync(b => b.InstitutionId == id, ct))
            throw new InvalidOperationException("Saved recipients use this bank; hide it instead of deleting.");
        if (await _db.RemittanceInstitutions.Where(i => i.Id == id).ExecuteDeleteAsync(ct) == 0) throw new KeyNotFoundException("Bank or provider not found.");
        await AuditAsync("Deleted", "RemittanceInstitution", id, null);
    }

    // ── pick-lists ───────────────────────────────────────────────────────────────────────────

    public Task<List<LookupDto>> ListLookupsAsync(string category, CancellationToken ct)
    {
        var normalized = NormalizeCategory(category);
        return _db.RemittanceLookups.AsNoTracking()
            .Where(l => l.Category == normalized)
            .OrderBy(l => l.SortOrder).ThenBy(l => l.Label)
            .Select(l => new LookupDto(l.Id, l.Category, l.Code, l.Label, l.IsActive, l.SortOrder))
            .ToListAsync(ct);
    }

    public async Task<LookupDto> SaveLookupAsync(string category, int? id, SaveLookupRequest r, CancellationToken ct)
    {
        var normalized = NormalizeCategory(category);
        var code = (r.Code ?? string.Empty).Trim().ToLowerInvariant();
        if (!LowerCode.IsMatch(code)) throw new ArgumentException("The code uses lower-case letters, digits and underscores.");
        var label = Clean(r.Label);
        RequireLength(label, 1, 30, "Label (WhatsApp shows at most 30 characters)");

        var lookup = id is { } existingId
            ? await _db.RemittanceLookups.FirstOrDefaultAsync(l => l.Id == existingId && l.Category == normalized, ct) ?? throw new KeyNotFoundException("Item not found.")
            : new RemittanceLookup { Category = normalized };
        lookup.Code = code;
        lookup.Label = label;
        lookup.IsActive = r.IsActive;
        lookup.SortOrder = r.SortOrder;
        if (id is null) _db.RemittanceLookups.Add(lookup);

        await _db.SaveChangesAsync(ct);
        await AuditAsync(id is null ? "Created" : "Updated", "RemittanceLookup", lookup.Id, $"{normalized}: {label}");
        return new LookupDto(lookup.Id, lookup.Category, lookup.Code, lookup.Label, lookup.IsActive, lookup.SortOrder);
    }

    public async Task DeleteLookupAsync(int id, CancellationToken ct)
    {
        if (await _db.RemittanceLookups.Where(l => l.Id == id).ExecuteDeleteAsync(ct) == 0) throw new KeyNotFoundException("Item not found.");
        await AuditAsync("Deleted", "RemittanceLookup", id, null);
    }

    // ── payment methods ──────────────────────────────────────────────────────────────────────

    public Task<List<PaymentMethodDto>> ListPaymentMethodsAsync(CancellationToken ct) =>
        _db.RemittancePaymentMethods.AsNoTracking()
            .OrderBy(p => p.SourceCountryCode).ThenBy(p => p.SortOrder).ThenBy(p => p.Label)
            .Select(p => new PaymentMethodDto(p.Id, p.SourceCountryCode, p.Provider, p.Code, p.Label, p.Description, p.Badge, p.PaymentWindowMinutes, p.IsActive, p.SortOrder))
            .ToListAsync(ct);

    public async Task<PaymentMethodDto> SavePaymentMethodAsync(int? id, SavePaymentMethodRequest r, CancellationToken ct)
    {
        var country = Upper(r.SourceCountryCode);
        if (!Country.IsMatch(country)) throw new ArgumentException("The country is a two-letter ISO code, e.g. MY.");
        if (!_providers.PaymentProviderNames.Contains(r.Provider, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Unknown payment provider \"{r.Provider}\". Available: {string.Join(", ", _providers.PaymentProviderNames)}.");
        var code = Upper(r.Code);
        if (!UpperCode.IsMatch(code)) throw new ArgumentException("The code uses capitals, digits and underscores, e.g. FPX.");
        var label = Clean(r.Label);
        RequireLength(label, 1, 30, "Label (WhatsApp shows at most 30 characters)");
        var description = string.IsNullOrWhiteSpace(r.Description) ? null : Clean(r.Description);
        if (description is { Length: > 300 }) throw new ArgumentException("The description can be at most 300 characters.");
        var badge = string.IsNullOrWhiteSpace(r.Badge) ? null : Clean(r.Badge);
        if (badge is { Length: > 20 }) throw new ArgumentException("The badge can be at most 20 characters.");
        if (r.PaymentWindowMinutes is < 1 or > 2880) throw new ArgumentException("The payment window must be between 1 minute and 48 hours.");

        var activeCount = await _db.RemittancePaymentMethods.CountAsync(p => p.SourceCountryCode == country && p.IsActive && p.Id != (id ?? 0), ct);
        if (r.IsActive && activeCount >= 20) throw new ArgumentException("WhatsApp can show at most 20 payment options.");

        var method = id is { } existingId
            ? await _db.RemittancePaymentMethods.FirstOrDefaultAsync(p => p.Id == existingId, ct) ?? throw new KeyNotFoundException("Payment method not found.")
            : new RemittancePaymentMethod();
        method.SourceCountryCode = country;
        method.Provider = _providers.PaymentProviderNames.First(n => n.Equals(r.Provider, StringComparison.OrdinalIgnoreCase));
        method.Code = code;
        method.Label = label;
        method.Description = description;
        method.Badge = badge;
        method.PaymentWindowMinutes = r.PaymentWindowMinutes;
        method.IsActive = r.IsActive;
        method.SortOrder = r.SortOrder;
        if (id is null) _db.RemittancePaymentMethods.Add(method);

        await _db.SaveChangesAsync(ct);
        await AuditAsync(id is null ? "Created" : "Updated", "RemittancePaymentMethod", method.Id, $"{country} {code}");
        return new PaymentMethodDto(method.Id, method.SourceCountryCode, method.Provider, method.Code, method.Label, method.Description, method.Badge,
            method.PaymentWindowMinutes, method.IsActive, method.SortOrder);
    }

    public async Task DeletePaymentMethodAsync(int id, CancellationToken ct)
    {
        if (await _db.RemittancePaymentMethods.Where(p => p.Id == id).ExecuteDeleteAsync(ct) == 0) throw new KeyNotFoundException("Payment method not found.");
        await AuditAsync("Deleted", "RemittancePaymentMethod", id, null);
    }

    // ── rates, reference, orders ─────────────────────────────────────────────────────────────

    public async Task<RatePreviewDto> PreviewRateAsync(int corridorId, CancellationToken ct)
    {
        var corridor = await _db.RemittanceCorridors.AsNoTracking().FirstOrDefaultAsync(c => c.Id == corridorId, ct)
                       ?? throw new KeyNotFoundException("Country route not found.");
        var rate = await _rates.GetRateAsync(corridor.SourceCurrency, corridor.DestinationCurrency, ct);
        return rate.Ok
            ? new RatePreviewDto(corridorId, corridor.SourceCurrency, corridor.DestinationCurrency, true, rate.Rate,
                RemittancePricing.ApplyMargin(rate.Rate, corridor.FxMarginBps), rate.AsOfUtc, rate.Provider, null)
            : new RatePreviewDto(corridorId, corridor.SourceCurrency, corridor.DestinationCurrency, false, null, null, null, null, rate.Error);
    }

    public RemittanceReferenceDto Reference() => new(
        _providers.PaymentProviderNames,
        _providers.PayoutProviderNames,
        Enum.GetNames<RemittanceFieldInputType>(),
        RemittanceLookup.Categories,
        Enum.GetNames<TransferOrderStatus>(),
        Enum.GetNames<TransferPaymentStatus>(),
        [new FlowPurposeDto(WhatsAppFlowRegistration.SendMoneyPurpose, "Send Money")],
        RemittanceBeneficiaryField.MaxSlots,
        _options.CurrentValue.Enabled);

    public async Task<PagedResponse<TransferOrderSummaryDto>> ListOrdersAsync(int page, int pageSize, string? search, TransferOrderStatus? orderStatus,
        TransferPaymentStatus? paymentStatus, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _db.TransferOrders.AsNoTracking();
        if (orderStatus is { } os) query = query.Where(o => o.OrderStatus == os);
        if (paymentStatus is { } ps) query = query.Where(o => o.PaymentStatus == ps);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(o => o.Reference.Contains(term) || o.BeneficiaryName.Contains(term)
                                     || o.Contact.Name.Contains(term) || o.Contact.Phone.Contains(term));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(o => new TransferOrderSummaryDto(o.Id, o.Reference, o.Contact.Name, o.Contact.Phone, o.BeneficiaryName, o.InstitutionName, o.PayoutMethodName,
                o.SendCurrency, o.SendAmount, o.TotalPayable, o.ReceiveCurrency, o.ReceiveAmount, o.OrderStatus, o.PaymentStatus, o.PaymentMethodCode,
                o.PaymentProvider, o.CreatedAt, o.PaidAt, o.CompletedAt))
            .ToListAsync(ct);

        return new PagedResponse<TransferOrderSummaryDto>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<TransferOrderDetailDto> GetOrderAsync(int id, CancellationToken ct)
    {
        var o = await _db.TransferOrders.AsNoTracking().Include(x => x.Contact).FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new KeyNotFoundException("Transfer not found.");
        var events = await _db.TransferOrderEvents.AsNoTracking()
            .Where(e => e.TransferOrderId == id)
            .OrderBy(e => e.OccurredAt).ThenBy(e => e.Id)
            .Select(e => new TransferOrderEventDto(e.Id, e.Kind, e.FromStatus, e.ToStatus, e.Source, e.Provider, e.ProviderEventId, e.Detail, e.OccurredAt))
            .ToListAsync(ct);
        var labels = await _db.RemittanceLookups.AsNoTracking()
            .Where(l => l.Code == o.SourceOfFundsCode || l.Code == o.PurposeCode)
            .Select(l => new { l.Category, l.Code, l.Label }).ToListAsync(ct);

        var summary = new TransferOrderSummaryDto(o.Id, o.Reference, o.Contact.Name, o.Contact.Phone, o.BeneficiaryName, o.InstitutionName, o.PayoutMethodName,
            o.SendCurrency, o.SendAmount, o.TotalPayable, o.ReceiveCurrency, o.ReceiveAmount, o.OrderStatus, o.PaymentStatus, o.PaymentMethodCode,
            o.PaymentProvider, o.CreatedAt, o.PaidAt, o.CompletedAt);

        return new TransferOrderDetailDto(summary, o.MaskedAccount, o.FeeAmount, o.AppliedRate,
            labels.FirstOrDefault(l => l.Category == RemittanceLookup.SourceOfFunds && l.Code == o.SourceOfFundsCode)?.Label ?? o.SourceOfFundsCode,
            labels.FirstOrDefault(l => l.Category == RemittanceLookup.Purpose && l.Code == o.PurposeCode)?.Label ?? o.PurposeCode,
            o.Remarks, o.ProviderPaymentId, o.PaymentUrl, o.PaymentReference, o.PaymentExpiresAt, o.PayoutProvider, o.PayoutReference, events);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private static void ValidateFee(FeeRuleDto fee)
    {
        if (fee.FixedFee < 0 || fee.MinFee < 0 || fee.MaxFee < 0 || fee.FirstTransferFee < 0) throw new ArgumentException("Fees cannot be negative.");
        if (fee.PercentFee is < 0 or > 100) throw new ArgumentException("The percentage fee must be between 0 and 100.");
        if (fee.MinFee is { } min && fee.MaxFee is { } max && min > max) throw new ArgumentException("The minimum fee is above the maximum.");
        if (fee.PromoLabel is { Length: > 20 }) throw new ArgumentException("The promotion label can be at most 20 characters.");
    }

    private static string NormalizeCategory(string category)
    {
        var normalized = (category ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_');
        if (!RemittanceLookup.Categories.Contains(normalized))
            throw new ArgumentException($"Unknown list \"{category}\". Lists: {string.Join(", ", RemittanceLookup.Categories)}.");
        return normalized;
    }

    private static void RequireLength(string value, int min, int max, string what)
    {
        if (value.Length < min) throw new ArgumentException($"{what} is required.");
        if (value.Length > max) throw new ArgumentException($"{what} can be at most {max} characters.");
    }

    private static string Upper(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();
    private static string Clean(string? value) => System.Net.WebUtility.HtmlDecode((value ?? string.Empty).Trim());

    private Task AuditAsync(string verb, string entity, int id, string? description) =>
        _audit.LogAsync($"Remittance.{verb}", "Settings", description is null ? $"{entity} {id} {verb.ToLowerInvariant()}" : $"{entity}: {description}",
            entity, id.ToString());
}
