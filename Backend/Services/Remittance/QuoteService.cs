using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Models.Options;

namespace WhatsAppCampaignApi.Services.Remittance;

public sealed record QuoteResult(FxQuote? Quote, string? Error)
{
    public bool Ok => Quote is not null;
}

public interface IQuoteService
{
    /// <summary>
    /// Prices a transfer and locks the price for Remittance:QuoteTtlMinutes. Every number the
    /// customer later confirms comes from the stored quote.
    /// </summary>
    Task<QuoteResult> CreateAsync(RemittanceSession session, int corridorId, int payoutMethodId, decimal sendAmount, CancellationToken ct);
}

public sealed class QuoteService : IQuoteService
{
    private readonly AppDbContext _db;
    private readonly IExchangeRateService _rates;
    private readonly IOptionsMonitor<RemittanceOptions> _options;
    private readonly HotPathCache _cache;

    public QuoteService(AppDbContext db, IExchangeRateService rates, IOptionsMonitor<RemittanceOptions> options, HotPathCache cache)
    {
        _cache = cache;
        _db = db;
        _rates = rates;
        _options = options;
    }

    public async Task<QuoteResult> CreateAsync(RemittanceSession session, int corridorId, int payoutMethodId, decimal sendAmount, CancellationToken ct)
    {
        // Catalogue rows, cached until an admin saves the catalogue (HotPathCache.SendMoney).
        var method = await _cache.GetOrLoadAsync(HotPathCache.SendMoney, $"quote-method:{corridorId}:{payoutMethodId}", () =>
            _db.RemittancePayoutMethods.AsNoTracking()
                .Include(m => m.Corridor)
                .Include(m => m.FeeRule)
                .FirstOrDefaultAsync(m => m.Id == payoutMethodId && m.CorridorId == corridorId && m.IsActive && m.Corridor.IsActive, ct));
        if (method is null) return new(null, "That delivery method is no longer available. Please choose another.");

        var corridor = method.Corridor;
        if (sendAmount < corridor.MinSendAmount || sendAmount > corridor.MaxSendAmount)
        {
            return new(null, $"Send between {RemittancePricing.Format(corridor.SourceCurrency, corridor.MinSendAmount)} and {RemittancePricing.Format(corridor.SourceCurrency, corridor.MaxSendAmount)}.");
        }

        var rate = await _rates.GetRateAsync(corridor.SourceCurrency, corridor.DestinationCurrency, ct);
        if (!rate.Ok) return new(null, rate.Error);

        var applied = RemittancePricing.ApplyMargin(rate.Rate, corridor.FxMarginBps);
        var receive = RemittancePricing.ReceiveAmount(sendAmount, applied, corridor.DestinationDecimals);
        if (receive <= 0) return new(null, "That amount is too small to send.");

        // "First transfer" means the customer has never had a payment go through.
        var isFirst = !await _db.TransferOrders.AsNoTracking()
            .AnyAsync(o => o.ContactId == session.ContactId && o.PaymentStatus == TransferPaymentStatus.Succeeded, ct);
        var (fee, label) = RemittancePricing.Fee(method.FeeRule, sendAmount, isFirst);

        var now = DateTime.UtcNow;
        var quote = new FxQuote
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            CorridorId = corridor.Id,
            PayoutMethodId = method.Id,
            SendCurrency = corridor.SourceCurrency,
            SendAmount = sendAmount,
            FeeAmount = fee,
            FeeLabel = label,
            TotalPayable = RemittancePricing.Money(sendAmount + fee),
            ReceiveCurrency = corridor.DestinationCurrency,
            ReceiveAmount = receive,
            MidRate = rate.Rate,
            AppliedRate = applied,
            MarginBps = corridor.FxMarginBps,
            RateProvider = rate.Provider,
            RateAsOf = rate.AsOfUtc,
            Status = FxQuoteStatus.Active,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(_options.CurrentValue.QuoteTtlMinutes)
        };

        // Earlier quotes in this session are superseded, so only one can ever be confirmed.
        await _db.FxQuotes
            .Where(q => q.SessionId == session.Id && q.Status == FxQuoteStatus.Active)
            .ExecuteUpdateAsync(u => u.SetProperty(q => q.Status, FxQuoteStatus.Expired), ct);

        _db.FxQuotes.Add(quote);
        await _db.SaveChangesAsync(ct);
        return new(quote, null);
    }
}
