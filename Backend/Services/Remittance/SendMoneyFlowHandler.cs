using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Models.Options;
using WhatsAppCampaignApi.Services.WhatsApp.Flows;

namespace WhatsAppCampaignApi.Services.Remittance;

/// <summary>
/// The server side of the Send Money WhatsApp Flow (Resources/WhatsAppFlows/send_money.flow.json).
/// </summary>
/// <remarks>
/// <para>
/// Server-authoritative throughout. The Flow is only a form: it sends what the customer picked,
/// and this class decides what happens next from the session row. Nothing the Flow claims is
/// trusted — being verified, the price, which recipient — so a crafted request cannot skip the
/// OTP, change an amount after quoting, or pay someone else's beneficiary.
/// </para>
/// <para>
/// Every option the screens show (countries, payout methods, banks, beneficiary fields, fees,
/// pick-lists, payment methods) is read from the Send Money configuration here; the Flow JSON holds
/// layout only. Meta forbids empty strings in bound text, so unused text slots carry "-" and are
/// hidden by their visibility flag.
/// </para>
/// </remarks>
public sealed class SendMoneyFlowHandler : IFlowScreenHandler
{
    public const string ScreenVerify = "VERIFY_IDENTITY";
    public const string ScreenTransfer = "TRANSFER_DETAILS";
    public const string ScreenBeneficiary = "BENEFICIARY";
    public const string ScreenCompliance = "COMPLIANCE";
    public const string ScreenReview = "REVIEW_CONFIRM";

    public const string OtpPurpose = "remittance.session";
    private const string NewBeneficiary = "new";
    private const string Blank = "-";

    private readonly AppDbContext _db;
    private readonly IRemittanceSessionService _sessions;
    private readonly IOtpService _otp;
    private readonly IQuoteService _quotes;
    private readonly IBeneficiaryService _beneficiaries;
    private readonly ITransferOrderService _orders;
    private readonly IOptionsMonitor<RemittanceOptions> _options;
    private readonly ILogger<SendMoneyFlowHandler> _logger;

    public SendMoneyFlowHandler(
        AppDbContext db,
        IRemittanceSessionService sessions,
        IOtpService otp,
        IQuoteService quotes,
        IBeneficiaryService beneficiaries,
        ITransferOrderService orders,
        IOptionsMonitor<RemittanceOptions> options,
        ILogger<SendMoneyFlowHandler> logger,
        HotPathCache cache)
    {
        _cache = cache;
        _db = db;
        _sessions = sessions;
        _otp = otp;
        _quotes = quotes;
        _beneficiaries = beneficiaries;
        _orders = orders;
        _options = options;
        _logger = logger;
    }

    private readonly HotPathCache _cache;

    public string TokenPrefix => RemittanceSessionService.TokenPrefix;
    public string Purpose => WhatsAppFlowRegistration.SendMoneyPurpose;

    public async Task<FlowHandlerResult> HandleAsync(FlowRequest request, CancellationToken ct)
    {
        if (!_options.CurrentValue.Enabled) return FlowHandlerResult.TokenInvalid();

        var session = await _sessions.FindByTokenAsync(request.FlowToken, ct);
        if (session is null
            || session.ExpiresAt < DateTime.UtcNow
            || session.Status is RemittanceSessionStatus.Expired or RemittanceSessionStatus.Cancelled or RemittanceSessionStatus.Locked
            // A token is only good on the connection whose Flow it was sent with.
            || (session.ConnectionId is { } connectionId && connectionId != request.ConnectionId))
        {
            return FlowHandlerResult.TokenInvalid();
        }

        var action = request.Action.Equals("data_exchange", StringComparison.OrdinalIgnoreCase)
            // The button's operation travels as "op": WhatsApp drops a payload field named "action"
            // (it clashes with the request's own "action"). "action" is still read for older clients.
            ? Text(request.Data, "op") ?? Text(request.Data, "action") ?? string.Empty
            : request.Action.ToUpperInvariant();

        if (session.Status == RemittanceSessionStatus.OrderCreated)
        {
            // Confirm tapped twice, or the Flow reopened after finishing: the order already exists.
            if (action == "confirm") return await CompleteForExistingOrderAsync(session, request.FlowToken, ct);
            return FlowHandlerResult.TokenInvalid();
        }

        try
        {
            return action switch
            {
                "INIT" => await InitAsync(session, ct),
                "BACK" => await RebuildAsync(session, request.Screen, ct),
                "resend_otp" => await ResendOtpAsync(session, ct),
                "verify_otp" => await VerifyOtpAsync(session, Text(request.Data, "otp"), ct),
                _ when session.OtpVerifiedAt is null => await VerifyScreenAsync(session, null, "Please verify your number first.", ct),
                "select_country" => await SelectCountryAsync(session, request.Data, ct),
                "select_method" => await SelectMethodAsync(session, request.Data, ct),
                "quote_or_continue" => await QuoteOrContinueAsync(session, request.Data, ct),
                "select_beneficiary" => await BeneficiaryScreenAsync(session, Text(request.Data, "beneficiary"), null, ct),
                "save_beneficiary" => await SaveBeneficiaryAsync(session, request.Data, ct),
                "save_compliance" => await SaveComplianceAsync(session, request.Data, ct),
                "confirm" => await ConfirmAsync(session, request.FlowToken, Text(request.Data, "payment_method"), ct),
                _ => await RebuildAsync(session, request.Screen, ct, "Something went wrong. Please try again.")
            };
        }
        catch (SendMoneyUnavailableException ex)
        {
            return await VerifyScreenAsync(session, null, ex.Message, ct);
        }
    }

    // ── Step 1: verification ──────────────────────────────────────────────────────────────────

    private async Task<FlowHandlerResult> InitAsync(RemittanceSession session, CancellationToken ct)
    {
        if (session.OtpVerifiedAt is not null) return await VerifyScreenAsync(session, null, null, ct);

        var issued = await _otp.IssueAsync(OtpPurpose, session.Id.ToString(), session.PhoneNumber, session.ConnectionId, ct);
        // Reopening the Flow inside the resend cooldown keeps the code already sent.
        var error = issued.Issued || issued.RetryAfterSeconds is not null ? null : issued.Error;
        return await VerifyScreenAsync(session, issued.DevCode, error, ct);
    }

    private async Task<FlowHandlerResult> ResendOtpAsync(RemittanceSession session, CancellationToken ct)
    {
        if (session.OtpVerifiedAt is not null) return await VerifyScreenAsync(session, null, null, ct);
        var issued = await _otp.IssueAsync(OtpPurpose, session.Id.ToString(), session.PhoneNumber, session.ConnectionId, ct);
        return await VerifyScreenAsync(session, issued.DevCode, issued.Issued ? "A new code is on its way." : issued.Error, ct);
    }

    private async Task<FlowHandlerResult> VerifyOtpAsync(RemittanceSession session, string? code, CancellationToken ct)
    {
        if (session.OtpVerifiedAt is not null) return await TransferScreenAsync(session, null, null, null, null, null, ct);

        if (string.IsNullOrWhiteSpace(code)) return await VerifyScreenAsync(session, null, "Enter the verification code.", ct);

        var result = await _otp.VerifyAsync(OtpPurpose, session.Id.ToString(), code, ct);
        if (!result.Success)
        {
            if (result.Locked)
            {
                session.Status = RemittanceSessionStatus.Locked;
                await _db.SaveChangesAsync(ct);
            }
            return await VerifyScreenAsync(session, null, result.Error, ct);
        }

        session.OtpVerifiedAt = DateTime.UtcNow;
        session.Status = RemittanceSessionStatus.Verified;
        await _db.SaveChangesAsync(ct);
        return await TransferScreenAsync(session, null, null, null, null, null, ct);
    }

    private Task<FlowHandlerResult> VerifyScreenAsync(RemittanceSession session, string? devCode, string? error, CancellationToken ct)
    {
        var verified = session.OtpVerifiedAt is not null;
        var length = _options.CurrentValue.Otp.Length;

        var data = new JsonObject
        {
            ["phone_display"] = DisplayPhone(session.PhoneNumber),
            ["otp_hint"] = verified
                ? "Your number is verified. Tap Verify & continue."
                : $"Enter the {length}-digit code we sent to your WhatsApp number:",
            ["otp_required"] = !verified,
            ["dev_hint"] = devCode is null ? Blank : $"Development only — your code is {devCode}",
            ["dev_hint_visible"] = devCode is not null
        };
        return Task.FromResult(WithError(ScreenVerify, data, error));
    }

    // ── Step 2: country, method, amount, quote ───────────────────────────────────────────────

    private async Task<FlowHandlerResult> SelectCountryAsync(RemittanceSession session, JsonObject? data, CancellationToken ct)
    {
        var corridorId = Int(data, "country");
        session.CorridorId = corridorId;
        session.PayoutMethodId = null;
        await _db.SaveChangesAsync(ct);
        return await TransferScreenAsync(session, corridorId, null, Text(data, "amount"), null, null, ct);
    }

    private async Task<FlowHandlerResult> SelectMethodAsync(RemittanceSession session, JsonObject? data, CancellationToken ct)
    {
        var corridorId = Int(data, "country");
        var methodId = Int(data, "method");
        session.CorridorId = corridorId;
        session.PayoutMethodId = methodId;
        await _db.SaveChangesAsync(ct);
        return await TransferScreenAsync(session, corridorId, methodId, Text(data, "amount"), null, null, ct);
    }

    private async Task<FlowHandlerResult> QuoteOrContinueAsync(RemittanceSession session, JsonObject? data, CancellationToken ct)
    {
        var corridorId = Int(data, "country");
        var methodId = Int(data, "method");
        var amountText = Text(data, "amount");
        var amount = RemittancePricing.ParseAmount(amountText);

        if (corridorId is null || methodId is null)
            return await TransferScreenAsync(session, corridorId, methodId, amountText, null, "Choose a country and delivery method.", ct);
        if (amount is null)
            return await TransferScreenAsync(session, corridorId, methodId, amountText, null, "Enter the amount to send, e.g. 500.", ct);

        // Continue only on a still-valid quote for exactly what is on screen; otherwise price it.
        var existing = session.QuoteId is { } quoteId
            ? await _db.FxQuotes.AsNoTracking().FirstOrDefaultAsync(q => q.Id == quoteId, ct)
            : null;
        var refreshed = false;
        if (existing is not null && existing.Status == FxQuoteStatus.Active && existing.ExpiresAt > DateTime.UtcNow
            && existing.CorridorId == corridorId && existing.PayoutMethodId == methodId && existing.SendAmount == amount)
        {
            return await BeneficiaryScreenAsync(session, null, null, ct);
        }
        if (existing is not null && existing.CorridorId == corridorId && existing.PayoutMethodId == methodId && existing.SendAmount == amount)
        {
            refreshed = true; // same inputs, the old price lapsed
        }

        var quote = await _quotes.CreateAsync(session, corridorId.Value, methodId.Value, amount.Value, ct);
        if (!quote.Ok) return await TransferScreenAsync(session, corridorId, methodId, amountText, null, quote.Error, ct);

        session.CorridorId = corridorId;
        session.PayoutMethodId = methodId;
        session.SendAmount = amount;
        session.QuoteId = quote.Quote!.Id;
        await _db.SaveChangesAsync(ct);

        return await TransferScreenAsync(session, corridorId, methodId, amountText, quote.Quote,
            refreshed ? "The rate was refreshed. Check it and continue." : null, ct);
    }

    private async Task<FlowHandlerResult> TransferScreenAsync(RemittanceSession session, int? corridorId, int? methodId, string? amountText,
        FxQuote? quote, string? error, CancellationToken ct)
    {
        // The catalogue is the same for every customer and changes only when an admin saves it:
        // cached (read-only; HotPathCache.SendMoney is cleared by those saves). Each screen has
        // about ten seconds to answer WhatsApp, and every query here is a network round trip.
        var corridors = await _cache.GetOrLoadAsync(HotPathCache.SendMoney, "corridors", () =>
            _db.RemittanceCorridors.AsNoTracking()
                .Where(c => c.IsActive && c.PayoutMethods.Any(m => m.IsActive))
                .Include(c => c.PayoutMethods.Where(m => m.IsActive))
                .OrderBy(c => c.SortOrder).ThenBy(c => c.DestinationCountryName)
                .Take(200)
                .ToListAsync(ct));
        if (corridors.Count == 0) throw new SendMoneyUnavailableException("Send Money isn't available right now. Please try again later.");

        var corridor = corridors.FirstOrDefault(c => c.Id == corridorId)
                       ?? corridors.FirstOrDefault(c => c.Id == session.CorridorId)
                       ?? corridors[0];
        var methods = corridor.PayoutMethods.OrderBy(m => m.SortOrder).ThenBy(m => m.Name).ToList();
        var method = methods.FirstOrDefault(m => m.Id == methodId)
                     ?? methods.FirstOrDefault(m => m.Id == session.PayoutMethodId)
                     ?? methods[0];

        var amount = RemittancePricing.ParseAmount(amountText) ?? session.SendAmount ?? corridor.MinSendAmount;
        var showQuote = quote is not null && quote.CorridorId == corridor.Id && quote.PayoutMethodId == method.Id;

        var data = new JsonObject
        {
            ["countries"] = new JsonArray(corridors.Select(c => (JsonNode)Option(
                c.Id.ToString(CultureInfo.InvariantCulture), $"{c.DestinationCountryName} ({c.DestinationCurrency})", $"Send from {c.SourceCurrency}")).ToArray()),
            ["selected_country"] = corridor.Id.ToString(CultureInfo.InvariantCulture),
            ["payout_methods"] = new JsonArray(methods.Select(m => (JsonNode)Option(
                m.Id.ToString(CultureInfo.InvariantCulture), m.Name, m.DeliveryEta)).ToArray()),
            ["selected_method"] = method.Id.ToString(CultureInfo.InvariantCulture),
            ["limits_text"] = $"Send {RemittancePricing.Format(corridor.SourceCurrency, corridor.MinSendAmount)} to {RemittancePricing.Format(corridor.SourceCurrency, corridor.MaxSendAmount)}",
            ["amount"] = amount.ToString("0.##", CultureInfo.InvariantCulture),
            ["amount_helper"] = $"Amount in {corridor.SourceCurrency}",
            ["quote_ready"] = showQuote,
            ["quote_prompt"] = "Tap Calculate to see the guaranteed rate and fees.",
            ["rate_line"] = showQuote ? $"Guaranteed rate: 1 {quote!.SendCurrency} = {RemittancePricing.FormatRate(quote.AppliedRate)} {quote.ReceiveCurrency}" : Blank,
            ["fee_line"] = showQuote ? FeeLine(quote!) : Blank,
            ["receive_line"] = showQuote ? $"Recipient gets: {RemittancePricing.Format(quote!.ReceiveCurrency, quote.ReceiveAmount, corridor.DestinationDecimals)}" : Blank,
            ["total_line"] = showQuote ? $"Total to pay: {RemittancePricing.Format(quote!.SendCurrency, quote.TotalPayable)}" : Blank,
            ["expiry_line"] = showQuote ? ExpiryLine(quote!) : Blank,
            ["footer_label"] = showQuote ? "Select beneficiary" : "Calculate"
        };
        return WithError(ScreenTransfer, data, error);
    }

    // ── Step 3: beneficiary ───────────────────────────────────────────────────────────────────

    private async Task<FlowHandlerResult> BeneficiaryScreenAsync(RemittanceSession session, string? selected, string? error, CancellationToken ct)
    {
        var (corridorId, methodId) = await RequireQuoteSelectionAsync(session, ct);
        var method = await _cache.GetOrLoadAsync(HotPathCache.SendMoney, $"method-form:{methodId}", () =>
            _db.RemittancePayoutMethods.AsNoTracking()
                .Include(m => m.Fields.Where(f => f.IsActive))
                .Include(m => m.Institutions.Where(i => i.IsActive))
                .AsSplitQuery()
                .FirstAsync(m => m.Id == methodId, ct));

        var saved = await _beneficiaries.ListAsync(session.ContactId, corridorId, methodId, ct);
        var institutions = method.Institutions.OrderBy(i => i.SortOrder).ThenBy(i => i.Name).ToList();
        var institutionNames = institutions.ToDictionary(i => i.Id, i => i.Name);
        var relationships = await LookupsAsync(RemittanceLookup.Relationship, ct);

        var choice = selected switch
        {
            NewBeneficiary => NewBeneficiary,
            { } id when saved.Any(b => b.Id.ToString(CultureInfo.InvariantCulture) == id) => id,
            _ => session.BeneficiaryId is { } current && saved.Any(b => b.Id == current)
                ? current.ToString(CultureInfo.InvariantCulture)
                : saved.Count > 0 ? saved[0].Id.ToString(CultureInfo.InvariantCulture) : NewBeneficiary
        };
        var isNew = choice == NewBeneficiary;

        var options = saved.Select(b => (JsonNode)Option(
            b.Id.ToString(CultureInfo.InvariantCulture),
            Truncate(b.DisplayName, 30),
            string.Join(" · ", new[] { b.InstitutionId is { } iid && institutionNames.TryGetValue(iid, out var n) ? n : null, b.MaskedAccount }
                .Where(s => !string.IsNullOrWhiteSpace(s))))).ToList();
        options.Add(Option(NewBeneficiary, "Add a new recipient", null));

        var data = new JsonObject
        {
            ["beneficiaries"] = new JsonArray(options.ToArray()),
            ["selected_beneficiary"] = choice,
            ["new_visible"] = isNew,
            ["institutions"] = institutions.Count > 0
                ? new JsonArray(institutions.Select(i => (JsonNode)Option(i.Code, i.Name, null)).ToArray())
                : new JsonArray(Option("none", "Not required", null)),
            ["institution_visible"] = isNew && institutions.Count > 0,
            ["institution_required"] = isNew && institutions.Count > 0,
            ["relationships"] = relationships.Count > 0
                ? new JsonArray(relationships.Select(r => (JsonNode)Option(r.Code, r.Label, null)).ToArray())
                : new JsonArray(Option("none", "Not required", null)),
            ["relationship_visible"] = isNew && relationships.Count > 0,
            ["relationship_required"] = isNew && relationships.Count > 0
        };

        var slots = BeneficiaryService.OrderedSlots(method.Fields);
        for (var i = 0; i < RemittanceBeneficiaryField.MaxSlots; i++)
        {
            var field = i < slots.Count ? slots[i] : null;
            var n = i + 1;
            data[$"f{n}_label"] = field?.Label ?? $"Field {n}";
            data[$"f{n}_visible"] = isNew && field is not null;
            data[$"f{n}_required"] = isNew && field is { IsRequired: true };
            data[$"f{n}_helper"] = !string.IsNullOrWhiteSpace(field?.HelperText) ? field!.HelperText : field is { IsRequired: true } ? "Required" : "Optional";
            data[$"f{n}_max"] = Math.Clamp(field?.MaxLength ?? 80, 1, 80);
        }

        return WithError(ScreenBeneficiary, data, error);
    }

    private async Task<FlowHandlerResult> SaveBeneficiaryAsync(RemittanceSession session, JsonObject? data, CancellationToken ct)
    {
        var (corridorId, methodId) = await RequireQuoteSelectionAsync(session, ct);
        var choice = Text(data, "beneficiary");

        if (choice == NewBeneficiary || string.IsNullOrEmpty(choice))
        {
            var slots = Enumerable.Range(1, RemittanceBeneficiaryField.MaxSlots).Select(n => Text(data, $"f{n}")).ToList();
            var created = await _beneficiaries.CreateAsync(session.ContactId, corridorId, methodId,
                new NewBeneficiaryInput(Text(data, "full_name"), Text(data, "institution"), Text(data, "relationship"), slots), ct);
            if (!created.Ok) return await BeneficiaryScreenAsync(session, NewBeneficiary, created.Error, ct);
            session.BeneficiaryId = created.Beneficiary!.Id;
        }
        else
        {
            var id = int.TryParse(choice, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
            // Only this customer's own recipient, saved for this exact route and method.
            var owned = await _db.RemittanceBeneficiaries.AsNoTracking().AnyAsync(b =>
                b.Id == id && b.ContactId == session.ContactId && b.CorridorId == corridorId && b.PayoutMethodId == methodId && b.IsActive, ct);
            if (!owned) return await BeneficiaryScreenAsync(session, null, "Choose a recipient from the list.", ct);
            session.BeneficiaryId = id;
        }

        await _db.SaveChangesAsync(ct);
        return await ComplianceScreenAsync(session, null, ct);
    }

    // ── Step 4: regulatory details ───────────────────────────────────────────────────────────

    private async Task<FlowHandlerResult> ComplianceScreenAsync(RemittanceSession session, string? error, CancellationToken ct)
    {
        await RequireQuoteSelectionAsync(session, ct);
        if (session.BeneficiaryId is null) return await BeneficiaryScreenAsync(session, null, "Choose a recipient first.", ct);

        var funds = await LookupsAsync(RemittanceLookup.SourceOfFunds, ct);
        var purposes = await LookupsAsync(RemittanceLookup.Purpose, ct);
        if (funds.Count == 0 || purposes.Count == 0)
            throw new SendMoneyUnavailableException("Send Money isn't fully set up yet. Please try again later.");

        var data = new JsonObject
        {
            ["source_of_funds"] = new JsonArray(funds.Select(l => (JsonNode)Option(l.Code, l.Label, null)).ToArray()),
            ["purposes"] = new JsonArray(purposes.Select(l => (JsonNode)Option(l.Code, l.Label, null)).ToArray())
        };
        return WithError(ScreenCompliance, data, error);
    }

    private async Task<FlowHandlerResult> SaveComplianceAsync(RemittanceSession session, JsonObject? data, CancellationToken ct)
    {
        var funds = Text(data, "source_of_funds");
        var purpose = Text(data, "purpose");
        var remarks = Text(data, "remarks");

        var validFunds = (await LookupsAsync(RemittanceLookup.SourceOfFunds, ct)).Any(l => l.Code == funds);
        var validPurpose = (await LookupsAsync(RemittanceLookup.Purpose, ct)).Any(l => l.Code == purpose);
        if (!validFunds || !validPurpose) return await ComplianceScreenAsync(session, "Choose a source of funds and a purpose.", ct);
        if (remarks is { Length: > 140 }) return await ComplianceScreenAsync(session, "Remarks can be at most 140 characters.", ct);

        session.SourceOfFundsCode = funds;
        session.PurposeCode = purpose;
        session.Remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks;
        await _db.SaveChangesAsync(ct);

        return await ReviewScreenAsync(session, null, null, ct);
    }

    // ── Step 5: review, payment method, confirm ──────────────────────────────────────────────

    private async Task<FlowHandlerResult> ReviewScreenAsync(RemittanceSession session, string? notice, string? error, CancellationToken ct)
    {
        var quote = await CurrentQuoteAsync(session, ct);
        if (quote is null) return await TransferScreenAsync(session, session.CorridorId, session.PayoutMethodId, null, null, "Get a new quote to continue.", ct);

        if (quote.ExpiresAt <= DateTime.UtcNow)
        {
            var renewed = await _quotes.CreateAsync(session, quote.CorridorId, quote.PayoutMethodId, quote.SendAmount, ct);
            if (!renewed.Ok) return await TransferScreenAsync(session, quote.CorridorId, quote.PayoutMethodId, null, null, renewed.Error, ct);
            session.QuoteId = renewed.Quote!.Id;
            await _db.SaveChangesAsync(ct);
            quote = renewed.Quote;
            notice ??= "Your rate expired, so we refreshed it. Please check the new amounts.";
        }

        if (session.BeneficiaryId is null) return await BeneficiaryScreenAsync(session, null, "Choose a recipient first.", ct);
        if (session.SourceOfFundsCode is null || session.PurposeCode is null) return await ComplianceScreenAsync(session, null, ct);

        var corridor = await CorridorAsync(quote.CorridorId, ct);
        var method = await _cache.GetOrLoadAsync(HotPathCache.SendMoney, $"method:{quote.PayoutMethodId}", () =>
            _db.RemittancePayoutMethods.AsNoTracking().FirstAsync(m => m.Id == quote.PayoutMethodId, ct));
        var beneficiary = await _db.RemittanceBeneficiaries.AsNoTracking().FirstAsync(b => b.Id == session.BeneficiaryId, ct);
        var institution = beneficiary.InstitutionId is { } instId
            ? await _cache.GetOrLoadAsync(HotPathCache.SendMoney, $"institution:{instId}", () =>
                _db.RemittanceInstitutions.AsNoTracking().FirstOrDefaultAsync(i => i.Id == instId, ct))
            : null;
        var paymentMethods = await PaymentMethodsAsync(corridor.SourceCountryCode, ct);
        if (paymentMethods.Count == 0) throw new SendMoneyUnavailableException("Payments aren't available right now. Please try again later.");

        var bank = string.Join(" · ", new[] { institution?.Name, beneficiary.MaskedAccount }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var data = new JsonObject
        {
            ["recipient_line"] = $"Recipient: {beneficiary.DisplayName}",
            ["bank_line"] = string.IsNullOrEmpty(bank) ? "Bank: -" : $"Bank & account: {bank}",
            ["service_line"] = $"Service: {method.Name}",
            ["send_line"] = $"You send: {RemittancePricing.Format(quote.SendCurrency, quote.SendAmount)}",
            ["rate_line"] = $"Applied rate: 1 {quote.SendCurrency} = {RemittancePricing.FormatRate(quote.AppliedRate)} {quote.ReceiveCurrency}",
            ["receive_line"] = $"Recipient gets: {RemittancePricing.Format(quote.ReceiveCurrency, quote.ReceiveAmount, corridor.DestinationDecimals)}",
            ["fee_line"] = FeeLine(quote),
            ["total_line"] = $"Total payable: {RemittancePricing.Format(quote.SendCurrency, quote.TotalPayable)}",
            ["expiry_line"] = ExpiryLine(quote),
            ["notice_text"] = notice ?? Blank,
            ["notice_visible"] = notice is not null,
            ["payment_methods"] = new JsonArray(paymentMethods.Select(p =>
            {
                var option = Option(p.Code, p.Label, p.Description);
                if (!string.IsNullOrWhiteSpace(p.Badge)) option["metadata"] = p.Badge;
                return (JsonNode)option;
            }).ToArray()),
            ["selected_payment"] = paymentMethods[0].Code
        };
        return WithError(ScreenReview, data, error);
    }

    private async Task<FlowHandlerResult> ConfirmAsync(RemittanceSession session, string flowToken, string? paymentMethodCode, CancellationToken ct)
    {
        var quote = await CurrentQuoteAsync(session, ct);
        if (quote is null || session.BeneficiaryId is null || session.SourceOfFundsCode is null || session.PurposeCode is null)
        {
            return await ReviewScreenAsync(session, null, "Some details are missing. Please check and try again.", ct);
        }

        // An expired price is never confirmed: re-price and show the customer the new numbers first.
        if (quote.Status != FxQuoteStatus.Active || quote.ExpiresAt <= DateTime.UtcNow)
        {
            return await ReviewScreenAsync(session, null, null, ct);
        }

        var corridor = await CorridorAsync(quote.CorridorId, ct);
        var paymentMethod = (await PaymentMethodsAsync(corridor.SourceCountryCode, ct))
            .FirstOrDefault(p => string.Equals(p.Code, paymentMethodCode, StringComparison.Ordinal));
        if (paymentMethod is null) return await ReviewScreenAsync(session, null, "Choose a payment method.", ct);

        var beneficiary = await _db.RemittanceBeneficiaries.AsNoTracking().FirstOrDefaultAsync(b =>
            b.Id == session.BeneficiaryId && b.ContactId == session.ContactId && b.IsActive, ct);
        if (beneficiary is null) return await BeneficiaryScreenAsync(session, null, "Choose a recipient first.", ct);

        var created = await _orders.CreateFromSessionAsync(session, quote, beneficiary, paymentMethod, ct);
        if (!created.Ok) return await ReviewScreenAsync(session, null, created.Error, ct);

        session.Status = RemittanceSessionStatus.OrderCreated;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Send Money order {Reference} created from session {SessionId}.", created.Order!.Reference, session.Id);
        return FlowHandlerResult.Complete(new JsonObject { ["flow_token"] = flowToken, ["order_reference"] = created.Order.Reference });
    }

    private async Task<FlowHandlerResult> CompleteForExistingOrderAsync(RemittanceSession session, string flowToken, CancellationToken ct)
    {
        var reference = await _db.TransferOrders.AsNoTracking().Where(o => o.SessionId == session.Id).Select(o => o.Reference).FirstOrDefaultAsync(ct);
        return reference is null
            ? FlowHandlerResult.TokenInvalid()
            : FlowHandlerResult.Complete(new JsonObject { ["flow_token"] = flowToken, ["order_reference"] = reference });
    }

    // ── shared ───────────────────────────────────────────────────────────────────────────────

    private async Task<FlowHandlerResult> RebuildAsync(RemittanceSession session, string? screen, CancellationToken ct, string? error = null)
    {
        if (session.OtpVerifiedAt is null) return await VerifyScreenAsync(session, null, error, ct);
        return screen switch
        {
            ScreenBeneficiary => await BeneficiaryScreenAsync(session, null, error, ct),
            ScreenCompliance => await ComplianceScreenAsync(session, error, ct),
            ScreenReview => await ReviewScreenAsync(session, null, error, ct),
            ScreenVerify => await VerifyScreenAsync(session, null, error, ct),
            _ => await TransferScreenAsync(session, session.CorridorId, session.PayoutMethodId, null, await CurrentQuoteAsync(session, ct), error, ct)
        };
    }

    /// <summary>The corridor and method of the session's quote; anything later requires one.</summary>
    private async Task<(int CorridorId, int MethodId)> RequireQuoteSelectionAsync(RemittanceSession session, CancellationToken ct)
    {
        var quote = await CurrentQuoteAsync(session, ct);
        if (quote is null) throw new SendMoneyUnavailableException("Please start again from the amount screen.");
        return (quote.CorridorId, quote.PayoutMethodId);
    }

    private Task<FxQuote?> CurrentQuoteAsync(RemittanceSession session, CancellationToken ct) =>
        session.QuoteId is { } id
            ? _db.FxQuotes.AsNoTracking().FirstOrDefaultAsync(q => q.Id == id && q.SessionId == session.Id, ct)
            : Task.FromResult<FxQuote?>(null);

    private Task<RemittanceCorridor> CorridorAsync(int corridorId, CancellationToken ct) =>
        _cache.GetOrLoadAsync(HotPathCache.SendMoney, $"corridor:{corridorId}", () =>
            _db.RemittanceCorridors.AsNoTracking().FirstAsync(c => c.Id == corridorId, ct));

    private Task<List<RemittanceLookup>> LookupsAsync(string category, CancellationToken ct) =>
        _cache.GetOrLoadAsync(HotPathCache.SendMoney, $"lookups:{category}", () =>
            _db.RemittanceLookups.AsNoTracking()
                .Where(l => l.Category == category && l.IsActive)
                .OrderBy(l => l.SortOrder).ThenBy(l => l.Label)
                .Take(200).ToListAsync(ct));

    private Task<List<RemittancePaymentMethod>> PaymentMethodsAsync(string sourceCountry, CancellationToken ct) =>
        _cache.GetOrLoadAsync(HotPathCache.SendMoney, $"payment-methods:{sourceCountry}", () =>
            _db.RemittancePaymentMethods.AsNoTracking()
                .Where(p => p.SourceCountryCode == sourceCountry && p.IsActive)
                .OrderBy(p => p.SortOrder).ThenBy(p => p.Label)
                .Take(20).ToListAsync(ct));

    private static FlowHandlerResult WithError(string screen, JsonObject data, string? error)
    {
        // Meta shows data.error_message as a snackbar on the returned screen.
        if (!string.IsNullOrWhiteSpace(error)) data["error_message"] = error;
        return FlowHandlerResult.Screen(screen, data);
    }

    private static JsonObject Option(string id, string title, string? description)
    {
        var option = new JsonObject { ["id"] = id, ["title"] = Truncate(title, 30) };
        if (!string.IsNullOrWhiteSpace(description)) option["description"] = Truncate(description, 300);
        return option;
    }

    private static string FeeLine(FxQuote quote) =>
        $"Transfer fee: {RemittancePricing.Format(quote.SendCurrency, quote.FeeAmount)}" + (string.IsNullOrWhiteSpace(quote.FeeLabel) ? string.Empty : $" ({quote.FeeLabel})");

    private static string ExpiryLine(FxQuote quote)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling((quote.ExpiresAt - DateTime.UtcNow).TotalMinutes));
        return minutes == 1 ? "Rate guaranteed for 1 more minute." : $"Rate guaranteed for {minutes} more minutes.";
    }

    private static string DisplayPhone(string phone) => phone.StartsWith('+') ? phone : "+" + phone;

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private static string? Text(JsonObject? data, string key)
    {
        if (data is null || !data.TryGetPropertyValue(key, out var node) || node is null) return null;
        var value = node is JsonValue v && v.TryGetValue<string>(out var s) ? s : node.ToJsonString().Trim('"');
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static int? Int(JsonObject? data, string key) =>
        int.TryParse(Text(data, key), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;
}

/// <summary>Send Money cannot continue (not configured, or the session lost its place); shown on the first screen.</summary>
public sealed class SendMoneyUnavailableException(string message) : Exception(message);
