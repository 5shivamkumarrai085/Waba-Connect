using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Queue;
using WhatsAppCampaignApi.Services.Remittance.Providers;

namespace WhatsAppCampaignApi.Services.Remittance;

public sealed record OrderCreationResult(TransferOrder? Order, string? Error)
{
    public bool Ok => Order is not null && Error is null;
}

/// <summary>What happened when a provider callback was applied.</summary>
public enum CallbackOutcome { Applied, Duplicate, Ignored, UnknownOrder }

public interface ITransferOrderService
{
    /// <summary>
    /// Creates the order for a confirmed session — or returns the one already created, so a second
    /// tap on "Confirm" never makes a second transfer — and starts the payment with the provider.
    /// </summary>
    Task<OrderCreationResult> CreateFromSessionAsync(RemittanceSession session, FxQuote quote, RemittanceBeneficiary beneficiary,
        RemittancePaymentMethod paymentMethod, CancellationToken ct);

    /// <summary>Applies a payment callback whose signature the provider adapter has already verified.</summary>
    Task<CallbackOutcome> ApplyPaymentEventAsync(string provider, PaymentWebhookEvent evt, CancellationToken ct);

    /// <summary>Applies a payout callback whose signature the provider adapter has already verified.</summary>
    Task<CallbackOutcome> ApplyPayoutEventAsync(string provider, PayoutWebhookEvent evt, CancellationToken ct);

    /// <summary>Hands a paid order to its payout provider. Safe to call more than once, from any instance.</summary>
    Task<PayoutSubmitResult> SubmitPayoutAsync(int orderId, CancellationToken ct);

    /// <summary>Puts a paid order that could not be paid out after every retry in front of a person.</summary>
    Task MarkNeedsReviewAsync(int orderId, string reason, CancellationToken ct);

    /// <summary>Expires unpaid orders whose payment window has passed. Returns how many.</summary>
    Task<int> ExpireOverdueAsync(CancellationToken ct);

    /// <summary>
    /// Repairs orders a crash left between steps: a paid order whose payout job was never queued,
    /// and a payout submission that never reported back. Returns how many were touched.
    /// </summary>
    Task<int> ReconcileAsync(CancellationToken ct);
}

/// <summary>
/// The transfer state machine. Two statuses move independently:
/// <list type="bullet">
/// <item><b>Payment</b> (Pending → Succeeded/Failed/Expired → Refunded) changes only on a verified
/// provider callback, or expiry when the window passes unpaid.</item>
/// <item><b>Order</b> (Created → AwaitingPayment → Paid → Submitting → Processing → Completed; or
/// Expired, Failed, Cancelled, NeedsReview) follows from payment and payout events.</item>
/// </list>
/// Every transition is a conditional UPDATE on the expected current status, so a duplicate or
/// out-of-order callback changes nothing, and each is recorded in TransferOrderEvents. A callback's
/// event record and the transitions it causes commit together, so a crash part-way never leaves a
/// callback "seen" but not applied.
/// </summary>
public sealed class TransferOrderService : ITransferOrderService
{
    /// <summary>An order whose payment never started is closed after this long.</summary>
    private static readonly TimeSpan UnstartedOrderLifetime = TimeSpan.FromMinutes(30);

    /// <summary>A paid order with no payout progress for this long gets its payout job queued again.</summary>
    private static readonly TimeSpan PayoutQueueGrace = TimeSpan.FromMinutes(2);

    /// <summary>A submission that never reported back is handed to a person rather than retried (it may have gone through).</summary>
    private static readonly TimeSpan SubmissionTimeout = TimeSpan.FromMinutes(15);

    private readonly AppDbContext _db;
    private readonly IRemittanceProviderRegistry _providers;
    private readonly IBeneficiaryService _beneficiaries;
    private readonly IRemittanceNotifier _notifier;
    private readonly IJobQueue _queue;
    private readonly ILogger<TransferOrderService> _logger;

    public TransferOrderService(
        AppDbContext db,
        IRemittanceProviderRegistry providers,
        IBeneficiaryService beneficiaries,
        IRemittanceNotifier notifier,
        IJobQueue queue,
        ILogger<TransferOrderService> logger)
    {
        _db = db;
        _providers = providers;
        _beneficiaries = beneficiaries;
        _notifier = notifier;
        _queue = queue;
        _logger = logger;
    }

    public async Task<OrderCreationResult> CreateFromSessionAsync(RemittanceSession session, FxQuote quote, RemittanceBeneficiary beneficiary,
        RemittancePaymentMethod paymentMethod, CancellationToken ct)
    {
        var order = await _db.TransferOrders.FirstOrDefaultAsync(o => o.SessionId == session.Id, ct);

        if (order is null)
        {
            var method = await _db.RemittancePayoutMethods.AsNoTracking().Include(m => m.Corridor)
                .FirstAsync(m => m.Id == quote.PayoutMethodId, ct);
            var institution = beneficiary.InstitutionId is { } instId
                ? await _db.RemittanceInstitutions.AsNoTracking().FirstOrDefaultAsync(i => i.Id == instId, ct)
                : null;

            for (var attempt = 0; attempt < 5 && order is null; attempt++)
            {
                var candidate = new TransferOrder
                {
                    Reference = NewReference(method.Corridor.SourceCountryCode),
                    SessionId = session.Id,
                    QuoteId = quote.Id,
                    ContactId = session.ContactId,
                    ConnectionId = session.ConnectionId,
                    CorridorId = quote.CorridorId,
                    PayoutMethodId = quote.PayoutMethodId,
                    BeneficiaryId = beneficiary.Id,
                    BeneficiaryName = beneficiary.DisplayName,
                    InstitutionName = institution?.Name,
                    MaskedAccount = beneficiary.MaskedAccount,
                    PayoutMethodName = method.Name,
                    SendCurrency = quote.SendCurrency,
                    SendAmount = quote.SendAmount,
                    FeeAmount = quote.FeeAmount,
                    TotalPayable = quote.TotalPayable,
                    ReceiveCurrency = quote.ReceiveCurrency,
                    ReceiveAmount = quote.ReceiveAmount,
                    AppliedRate = quote.AppliedRate,
                    SourceOfFundsCode = session.SourceOfFundsCode,
                    PurposeCode = session.PurposeCode,
                    Remarks = session.Remarks,
                    PaymentMethodCode = paymentMethod.Code,
                    PaymentProvider = paymentMethod.Provider,
                    PayoutProvider = method.PayoutProvider,
                    OrderStatus = TransferOrderStatus.Created,
                    PaymentStatus = TransferPaymentStatus.Pending
                };

                _db.TransferOrders.Add(candidate);
                try
                {
                    await _db.SaveChangesAsync(ct);
                    order = candidate;
                }
                catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                {
                    _db.Entry(candidate).State = EntityState.Detached;

                    // Either a concurrent confirm created the session's order, or the random
                    // reference collided; the first means use theirs, the second means retry.
                    order = await _db.TransferOrders.FirstOrDefaultAsync(o => o.SessionId == session.Id, ct);
                }
            }

            if (order is null) return new(null, "We couldn't create your order. Please try again.");

            await RecordAsync(order.Id, "OrderStatus", null, nameof(TransferOrderStatus.Created), "Customer", null, null,
                $"Confirmed in WhatsApp: {RemittancePricing.Format(order.SendCurrency, order.TotalPayable)} via {paymentMethod.Code}.", ct);
        }
        else if (order.ProviderPaymentId is null && order.OrderStatus == TransferOrderStatus.Created
                 && (order.QuoteId != quote.Id || order.PaymentMethodCode != paymentMethod.Code))
        {
            // The payment never started and the customer has since approved a renewed quote (or
            // chose another way to pay). The order must charge what they approved now, never the
            // price from before.
            var repriced = await _db.TransferOrders
                .Where(o => o.Id == order.Id && o.ProviderPaymentId == null && o.OrderStatus == TransferOrderStatus.Created)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(o => o.QuoteId, quote.Id)
                    .SetProperty(o => o.SendAmount, quote.SendAmount)
                    .SetProperty(o => o.FeeAmount, quote.FeeAmount)
                    .SetProperty(o => o.TotalPayable, quote.TotalPayable)
                    .SetProperty(o => o.ReceiveAmount, quote.ReceiveAmount)
                    .SetProperty(o => o.AppliedRate, quote.AppliedRate)
                    .SetProperty(o => o.PaymentMethodCode, paymentMethod.Code)
                    .SetProperty(o => o.PaymentProvider, paymentMethod.Provider)
                    .SetProperty(o => o.UpdatedAt, DateTime.UtcNow), ct);
            await _db.Entry(order).ReloadAsync(ct);
            if (repriced == 1)
            {
                await RecordAsync(order.Id, "OrderStatus", null, null, "Customer", null, null,
                    $"Re-confirmed at the renewed quote: {RemittancePricing.Format(order.SendCurrency, order.TotalPayable)} via {paymentMethod.Code}.", ct);
            }
        }

        // Payment already started (a repeated confirm): hand back what exists.
        if (order.ProviderPaymentId is not null) return new(order, null);

        if (order.OrderStatus != TransferOrderStatus.Created)
        {
            return new(null, "This order can no longer be paid. Type \"send money\" to start a new transfer.");
        }

        var provider = _providers.Payment(order.PaymentProvider);
        var expiresAt = DateTime.UtcNow.AddMinutes(Math.Max(1, paymentMethod.PaymentWindowMinutes));
        var phone = await _db.Contacts.AsNoTracking().Where(c => c.Id == order.ContactId).Select(c => c.Phone).FirstAsync(ct);

        var payment = await provider.CreatePaymentAsync(
            new PaymentRequest(order.Reference, order.PaymentMethodCode, order.TotalPayable, order.SendCurrency, expiresAt, phone), ct);

        if (!payment.Success || string.IsNullOrWhiteSpace(payment.ProviderPaymentId))
        {
            _logger.LogWarning("Payment provider {Provider} could not start payment for order {Reference}: {Error}",
                order.PaymentProvider, order.Reference, payment.Error);
            await RecordAsync(order.Id, "PaymentStatus", null, null, "System", order.PaymentProvider, null,
                $"Could not start payment: {payment.Error}", ct);
            // The order stays Created and its quote stays usable, so confirming again retries the
            // payment start at the same price.
            return new(order, "We couldn't start the payment. Please tap Confirm again.");
        }

        var updated = await _db.TransferOrders
            .Where(o => o.Id == order.Id && o.ProviderPaymentId == null && o.OrderStatus == TransferOrderStatus.Created)
            .ExecuteUpdateAsync(u => u
                .SetProperty(o => o.ProviderPaymentId, payment.ProviderPaymentId)
                .SetProperty(o => o.PaymentUrl, payment.PaymentUrl)
                .SetProperty(o => o.PaymentReference, payment.PaymentReference)
                .SetProperty(o => o.PaymentExpiresAt, expiresAt)
                .SetProperty(o => o.OrderStatus, TransferOrderStatus.AwaitingPayment)
                .SetProperty(o => o.UpdatedAt, DateTime.UtcNow), ct);

        if (updated == 1)
        {
            // Only now is the quote spent: until the payment exists, a retry must be able to use it.
            await _db.FxQuotes.Where(q => q.Id == order.QuoteId).ExecuteUpdateAsync(u => u.SetProperty(q => q.Status, FxQuoteStatus.Used), ct);
            await RecordAsync(order.Id, "OrderStatus", nameof(TransferOrderStatus.Created), nameof(TransferOrderStatus.AwaitingPayment), "System",
                order.PaymentProvider, null, $"Payment {payment.ProviderPaymentId} started; window closes {expiresAt:u}.", ct);
        }

        await _db.Entry(order).ReloadAsync(ct);
        return new(order, null);
    }

    /// <summary>What to do once a callback's transaction has committed.</summary>
    private enum FollowUp { None, PaidQueuePayout, PaymentUnsuccessful, PaymentWindowClosed, Refunded, TransferCompleted, TransferUndeliverable }

    public async Task<CallbackOutcome> ApplyPaymentEventAsync(string provider, PaymentWebhookEvent evt, CancellationToken ct)
    {
        var order = await _db.TransferOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.PaymentProvider == provider && o.ProviderPaymentId == evt.ProviderPaymentId, ct);
        if (order is null)
        {
            _logger.LogWarning("Payment callback {EventId} from {Provider} names unknown payment {PaymentId}.", evt.ProviderEventId, provider, evt.ProviderPaymentId);
            return CallbackOutcome.UnknownOrder;
        }

        var (outcome, followUp) = await InCallbackTransactionAsync(async () =>
        {
            // Recorded inside the transaction: a redelivery is a no-op, and a crash before commit
            // leaves nothing recorded, so the retry applies the callback in full.
            if (!await TryRecordCallbackAsync(order.Id, "PaymentCallback", provider, evt.ProviderEventId,
                    $"{evt.Kind} for {evt.Currency} {evt.Amount}" + (evt.Reason is null ? string.Empty : $" ({evt.Reason})"), ct))
            {
                return (CallbackOutcome.Duplicate, FollowUp.None);
            }

            switch (evt.Kind)
            {
                case PaymentEventKind.Succeeded:
                {
                    if (evt.Amount is not { } paid || paid != order.TotalPayable
                        || !string.Equals(evt.Currency, order.SendCurrency, StringComparison.OrdinalIgnoreCase))
                    {
                        await RecordAsync(order.Id, "PaymentStatus", null, null, "ProviderWebhook", provider, null,
                            evt.Amount is null
                                ? "Provider reported success without an amount. Not marked paid; needs review."
                                : $"Amount mismatch: provider reported {evt.Currency} {evt.Amount}, order total is {order.SendCurrency} {order.TotalPayable}. Not marked paid; needs review.", ct);
                        return (CallbackOutcome.Ignored, FollowUp.None);
                    }

                    var moved = await MovePaymentAsync(order.Id, [TransferPaymentStatus.Pending, TransferPaymentStatus.Expired, TransferPaymentStatus.Failed],
                        TransferPaymentStatus.Succeeded, provider, ct);
                    if (!moved) return (CallbackOutcome.Ignored, FollowUp.None);

                    // A payment that lands after the order lapsed is still honoured — the money is ours.
                    var paidNow = await _db.TransferOrders
                        .Where(o => o.Id == order.Id && (o.OrderStatus == TransferOrderStatus.Created
                                                         || o.OrderStatus == TransferOrderStatus.AwaitingPayment
                                                         || o.OrderStatus == TransferOrderStatus.Expired
                                                         || o.OrderStatus == TransferOrderStatus.Failed))
                        .ExecuteUpdateAsync(u => u
                            .SetProperty(o => o.OrderStatus, TransferOrderStatus.Paid)
                            .SetProperty(o => o.PaidAt, evt.OccurredAt)
                            .SetProperty(o => o.UpdatedAt, DateTime.UtcNow), ct);

                    if (paidNow == 1)
                    {
                        await RecordAsync(order.Id, "OrderStatus", order.OrderStatus.ToString(), nameof(TransferOrderStatus.Paid), "ProviderWebhook", provider, null, null, ct);
                        return (CallbackOutcome.Applied, FollowUp.PaidQueuePayout);
                    }

                    await RecordAsync(order.Id, "OrderStatus", null, null, "ProviderWebhook", provider, null,
                        $"Payment succeeded while the order was {order.OrderStatus}; order left unchanged — needs review (refund).", ct);
                    return (CallbackOutcome.Applied, FollowUp.None);
                }

                case PaymentEventKind.Failed:
                case PaymentEventKind.Expired:
                {
                    var target = evt.Kind == PaymentEventKind.Failed ? TransferPaymentStatus.Failed : TransferPaymentStatus.Expired;
                    // Never downgrades a payment that already succeeded: an out-of-order "failed" is ignored.
                    if (!await MovePaymentAsync(order.Id, [TransferPaymentStatus.Pending], target, provider, ct)) return (CallbackOutcome.Ignored, FollowUp.None);

                    var orderTarget = evt.Kind == PaymentEventKind.Failed ? TransferOrderStatus.Failed : TransferOrderStatus.Expired;
                    var closed = await MoveOrderAsync(order.Id, [TransferOrderStatus.Created, TransferOrderStatus.AwaitingPayment], orderTarget, "ProviderWebhook", provider, ct);
                    return (CallbackOutcome.Applied, !closed ? FollowUp.None
                        : evt.Kind == PaymentEventKind.Failed ? FollowUp.PaymentUnsuccessful : FollowUp.PaymentWindowClosed);
                }

                case PaymentEventKind.Refunded:
                {
                    if (!await MovePaymentAsync(order.Id, [TransferPaymentStatus.Succeeded], TransferPaymentStatus.Refunded, provider, ct)) return (CallbackOutcome.Ignored, FollowUp.None);
                    await MoveOrderAsync(order.Id, [TransferOrderStatus.Paid, TransferOrderStatus.Processing, TransferOrderStatus.Failed, TransferOrderStatus.NeedsReview],
                        TransferOrderStatus.Cancelled, "ProviderWebhook", provider, ct);
                    return (CallbackOutcome.Applied, FollowUp.Refunded);
                }
            }

            return (CallbackOutcome.Ignored, FollowUp.None);
        }, ct);

        await RunFollowUpAsync(order, followUp, ct);
        return outcome;
    }

    public async Task<PayoutSubmitResult> SubmitPayoutAsync(int orderId, CancellationToken ct)
    {
        var order = await _db.TransferOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null) return new(false, Error: "Order not found.");

        // Claim the submission first. Only the caller whose UPDATE wins talks to the payout
        // provider, so two workers (or a retry racing an expired lease) can never send the money
        // twice. An order stuck in Submitting is resolved by ReconcileAsync, never re-sent blindly.
        var claimed = await _db.TransferOrders
            .Where(o => o.Id == orderId && o.OrderStatus == TransferOrderStatus.Paid && o.PaymentStatus == TransferPaymentStatus.Succeeded)
            .ExecuteUpdateAsync(u => u.SetProperty(o => o.OrderStatus, TransferOrderStatus.Submitting).SetProperty(o => o.UpdatedAt, DateTime.UtcNow), ct);
        if (claimed == 0)
        {
            // Already submitted (Processing), in flight elsewhere, or no longer eligible.
            return new(order.PayoutReference is not null, order.PayoutReference);
        }
        await RecordAsync(orderId, "OrderStatus", nameof(TransferOrderStatus.Paid), nameof(TransferOrderStatus.Submitting), "System", order.PayoutProvider, null, null, ct);

        PayoutSubmitResult result;
        try
        {
            var beneficiary = await _db.RemittanceBeneficiaries.AsNoTracking().FirstOrDefaultAsync(b => b.Id == order.BeneficiaryId, ct);
            var corridor = await _db.RemittanceCorridors.AsNoTracking().FirstOrDefaultAsync(c => c.Id == order.CorridorId, ct);
            var method = await _db.RemittancePayoutMethods.AsNoTracking().FirstOrDefaultAsync(m => m.Id == order.PayoutMethodId, ct);
            var institution = beneficiary?.InstitutionId is { } instId
                ? await _db.RemittanceInstitutions.AsNoTracking().FirstOrDefaultAsync(i => i.Id == instId, ct)
                : null;

            result = beneficiary is null || corridor is null || method is null
                ? new(false, Error: "Order details are incomplete.")
                // The order reference is the provider's idempotency key: a resubmission after a
                // transient failure must not create a second payout.
                : await _providers.Payout(order.PayoutProvider).SubmitAsync(new PayoutRequest(
                    order.Reference, order.ReceiveAmount, order.ReceiveCurrency, corridor.DestinationCountryCode, method.Code,
                    order.BeneficiaryName, institution?.Code, _beneficiaries.ReadDetails(beneficiary)), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Payout submission for order {Reference} threw; releasing it for a retry.", order.Reference);
            result = new(false, Error: ex.Message, IsTransient: true);
        }

        if (result.Accepted)
        {
            var moved = await _db.TransferOrders
                .Where(o => o.Id == orderId && o.OrderStatus == TransferOrderStatus.Submitting)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(o => o.OrderStatus, TransferOrderStatus.Processing)
                    .SetProperty(o => o.PayoutReference, result.PayoutReference)
                    .SetProperty(o => o.UpdatedAt, DateTime.UtcNow), ct);
            if (moved == 1)
            {
                await RecordAsync(orderId, "OrderStatus", nameof(TransferOrderStatus.Submitting), nameof(TransferOrderStatus.Processing), "System",
                    order.PayoutProvider, null, $"Payout {result.PayoutReference} accepted.", ct);
            }
        }
        else if (result.IsTransient)
        {
            // Released for the queue's retry. Safe because providers de-duplicate on the reference.
            await MoveOrderAsync(orderId, [TransferOrderStatus.Submitting], TransferOrderStatus.Paid, "System", order.PayoutProvider, CancellationToken.None,
                $"Payout provider unavailable: {result.Error}");
        }
        else if (await MoveOrderAsync(orderId, [TransferOrderStatus.Submitting], TransferOrderStatus.Failed, "System", order.PayoutProvider, ct, result.Error))
        {
            await _notifier.SendStatusAsync(orderId, "Transfer could not be sent",
                "Our payout partner could not deliver this transfer. Our team will contact you about a refund.", ct);
        }

        return result;
    }

    public async Task MarkNeedsReviewAsync(int orderId, string reason, CancellationToken ct)
    {
        if (await MoveOrderAsync(orderId, [TransferOrderStatus.Paid, TransferOrderStatus.Submitting], TransferOrderStatus.NeedsReview, "System", null, ct, reason))
        {
            _logger.LogError("Order {OrderId} needs review: {Reason}", orderId, reason);
            await _notifier.SendStatusAsync(orderId, "Your transfer is taking longer",
                "We received your payment and our team is checking your transfer. We'll update you here.", ct);
        }
    }

    public async Task<CallbackOutcome> ApplyPayoutEventAsync(string provider, PayoutWebhookEvent evt, CancellationToken ct)
    {
        var order = await _db.TransferOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.PayoutProvider == provider && o.PayoutReference == evt.PayoutReference, ct);
        if (order is null) return CallbackOutcome.UnknownOrder;

        var (outcome, followUp) = await InCallbackTransactionAsync(async () =>
        {
            if (!await TryRecordCallbackAsync(order.Id, "PayoutCallback", provider, evt.ProviderEventId, $"{evt.Kind}" + (evt.Reason is null ? string.Empty : $" ({evt.Reason})"), ct))
            {
                return (CallbackOutcome.Duplicate, FollowUp.None);
            }

            if (evt.Kind == PayoutEventKind.Completed)
            {
                var moved = await _db.TransferOrders
                    .Where(o => o.Id == order.Id && (o.OrderStatus == TransferOrderStatus.Processing || o.OrderStatus == TransferOrderStatus.NeedsReview))
                    .ExecuteUpdateAsync(u => u
                        .SetProperty(o => o.OrderStatus, TransferOrderStatus.Completed)
                        .SetProperty(o => o.CompletedAt, evt.OccurredAt)
                        .SetProperty(o => o.UpdatedAt, DateTime.UtcNow), ct);
                if (moved == 0) return (CallbackOutcome.Ignored, FollowUp.None);

                await RecordAsync(order.Id, "OrderStatus", order.OrderStatus.ToString(), nameof(TransferOrderStatus.Completed), "ProviderWebhook", provider, null, null, ct);
                return (CallbackOutcome.Applied, FollowUp.TransferCompleted);
            }

            if (!await MoveOrderAsync(order.Id, [TransferOrderStatus.Processing, TransferOrderStatus.NeedsReview], TransferOrderStatus.Failed, "ProviderWebhook", provider, ct, evt.Reason))
            {
                return (CallbackOutcome.Ignored, FollowUp.None);
            }
            return (CallbackOutcome.Applied, FollowUp.TransferUndeliverable);
        }, ct);

        await RunFollowUpAsync(order, followUp, ct);
        return outcome;
    }

    public async Task<int> ExpireOverdueAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var count = 0;

        // Batches until done (bounded per cycle), so a backlog after downtime clears quickly.
        for (var batch = 0; batch < 10; batch++)
        {
            var overdue = await _db.TransferOrders.AsNoTracking()
                .Where(o => o.OrderStatus == TransferOrderStatus.AwaitingPayment
                            && o.PaymentStatus == TransferPaymentStatus.Pending
                            && o.PaymentExpiresAt != null && o.PaymentExpiresAt < now)
                .OrderBy(o => o.PaymentExpiresAt)
                .Select(o => o.Id)
                .Take(50)
                .ToListAsync(ct);
            if (overdue.Count == 0) break;

            foreach (var id in overdue)
            {
                if (!await MovePaymentAsync(id, [TransferPaymentStatus.Pending], TransferPaymentStatus.Expired, "System", ct)) continue;
                if (await MoveOrderAsync(id, [TransferOrderStatus.AwaitingPayment], TransferOrderStatus.Expired, "System", null, ct, "Payment window passed."))
                {
                    count++;
                    await _notifier.SendStatusAsync(id, "Payment window closed",
                        "We didn't receive payment in time, so this order has expired. Type \"send money\" to start again.", ct);
                }
            }
        }

        // Orders whose payment never started (the provider refused and the customer gave up).
        // Nothing was charged, so they are closed quietly.
        var unstartedCutoff = now - UnstartedOrderLifetime;
        var unstarted = await _db.TransferOrders.AsNoTracking()
            .Where(o => o.OrderStatus == TransferOrderStatus.Created && o.ProviderPaymentId == null && o.CreatedAt < unstartedCutoff)
            .Select(o => o.Id).Take(200).ToListAsync(ct);
        foreach (var id in unstarted)
        {
            await MovePaymentAsync(id, [TransferPaymentStatus.Pending], TransferPaymentStatus.Expired, "System", ct);
            if (await MoveOrderAsync(id, [TransferOrderStatus.Created], TransferOrderStatus.Expired, "System", null, ct, "Payment never started.")) count++;
        }

        return count;
    }

    public async Task<int> ReconcileAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var touched = 0;

        // Paid, but the payout job may never have been queued (a crash right after the payment
        // callback committed). Re-queueing is safe: the idempotency key makes it a no-op when the
        // job exists, and the Submitting claim stops a second submission.
        var payCutoff = now - PayoutQueueGrace;
        var stalledPaid = await _db.TransferOrders.AsNoTracking()
            .Where(o => o.OrderStatus == TransferOrderStatus.Paid && o.PaymentStatus == TransferPaymentStatus.Succeeded && o.UpdatedAt < payCutoff)
            .Select(o => o.Id).Take(100).ToListAsync(ct);
        foreach (var id in stalledPaid)
        {
            var result = await QueuePayoutAsync(id, ct);
            touched += result.Enqueued;
        }

        // A submission that never reported back may or may not have reached the provider:
        // re-sending could pay twice, so a person decides.
        var submitCutoff = now - SubmissionTimeout;
        var stuck = await _db.TransferOrders.AsNoTracking()
            .Where(o => o.OrderStatus == TransferOrderStatus.Submitting && o.UpdatedAt < submitCutoff)
            .Select(o => o.Id).Take(100).ToListAsync(ct);
        foreach (var id in stuck)
        {
            await MarkNeedsReviewAsync(id, "Payout submission did not report back; check with the payout provider before retrying.", ct);
            touched++;
        }

        return touched;
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs a callback's record-and-transition steps in one transaction, under the context's retry
    /// strategy (a dropped connection retries the whole unit, never half of it).
    /// </summary>
    private async Task<(CallbackOutcome Outcome, FollowUp FollowUp)> InCallbackTransactionAsync(
        Func<Task<(CallbackOutcome, FollowUp)>> work, CancellationToken ct)
    {
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            _db.ChangeTracker.Clear();
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            var result = await work();
            if (result.Item1 == CallbackOutcome.Duplicate)
            {
                await tx.RollbackAsync(ct);
            }
            else
            {
                await tx.CommitAsync(ct);
            }
            return result;
        });
    }

    /// <summary>Messages and jobs that follow a committed callback. Never inside the transaction: a WhatsApp send cannot be rolled back.</summary>
    private async Task RunFollowUpAsync(TransferOrder order, FollowUp followUp, CancellationToken ct)
    {
        switch (followUp)
        {
            case FollowUp.PaidQueuePayout:
                await QueuePayoutAsync(order.Id, ct);
                await _notifier.SendStatusAsync(order.Id, "Payment received",
                    $"We received {RemittancePricing.Format(order.SendCurrency, order.TotalPayable)}. Your transfer to {order.BeneficiaryName} is being processed.", ct);
                break;
            case FollowUp.PaymentUnsuccessful:
            case FollowUp.PaymentWindowClosed:
                await _notifier.SendStatusAsync(order.Id,
                    followUp == FollowUp.PaymentUnsuccessful ? "Payment unsuccessful" : "Payment window closed",
                    "No money was taken for this order. Type \"send money\" to start a new transfer.", ct);
                break;
            case FollowUp.Refunded:
                await _notifier.SendStatusAsync(order.Id, "Payment refunded",
                    $"{RemittancePricing.Format(order.SendCurrency, order.TotalPayable)} has been refunded to you.", ct);
                break;
            case FollowUp.TransferCompleted:
                var corridor = await _db.RemittanceCorridors.AsNoTracking().FirstOrDefaultAsync(c => c.Id == order.CorridorId, ct);
                await _notifier.SendStatusAsync(order.Id, "Transfer completed",
                    $"{order.BeneficiaryName} has received {RemittancePricing.Format(order.ReceiveCurrency, order.ReceiveAmount, corridor?.DestinationDecimals ?? 2)}.", ct);
                break;
            case FollowUp.TransferUndeliverable:
                await _notifier.SendStatusAsync(order.Id, "Transfer could not be delivered",
                    "The recipient's bank could not accept this transfer. Our team will contact you about a refund.", ct);
                break;
        }
    }

    private Task<QueueEnqueueResult> QueuePayoutAsync(int orderId, CancellationToken ct) =>
        _queue.EnqueueAsync(new[]
        {
            new QueueMessage
            {
                QueueName = QueueNames.RemittancePayout,
                Payload = JsonSerializer.Serialize(new RemittancePayoutJob(orderId)),
                IdempotencyKey = $"payout:{orderId}",
                MaxAttempts = 6
            }
        }, ct);

    private async Task<bool> MovePaymentAsync(int orderId, TransferPaymentStatus[] from, TransferPaymentStatus to, string source, CancellationToken ct)
    {
        var current = await _db.TransferOrders.AsNoTracking().Where(o => o.Id == orderId).Select(o => o.PaymentStatus).FirstAsync(ct);
        var moved = await _db.TransferOrders
            .Where(o => o.Id == orderId && from.Contains(o.PaymentStatus))
            .ExecuteUpdateAsync(u => u.SetProperty(o => o.PaymentStatus, to).SetProperty(o => o.UpdatedAt, DateTime.UtcNow), ct);
        if (moved == 1)
        {
            await RecordAsync(orderId, "PaymentStatus", current.ToString(), to.ToString(), source == "System" ? "System" : "ProviderWebhook",
                source == "System" ? null : source, null, null, ct);
        }
        return moved == 1;
    }

    private async Task<bool> MoveOrderAsync(int orderId, TransferOrderStatus[] from, TransferOrderStatus to, string source, string? provider, CancellationToken ct, string? detail = null)
    {
        var current = await _db.TransferOrders.AsNoTracking().Where(o => o.Id == orderId).Select(o => o.OrderStatus).FirstAsync(ct);
        var moved = await _db.TransferOrders
            .Where(o => o.Id == orderId && from.Contains(o.OrderStatus))
            .ExecuteUpdateAsync(u => u.SetProperty(o => o.OrderStatus, to).SetProperty(o => o.UpdatedAt, DateTime.UtcNow), ct);
        if (moved == 1) await RecordAsync(orderId, "OrderStatus", current.ToString(), to.ToString(), source, provider, null, detail, ct);
        return moved == 1;
    }

    /// <summary>False when this provider event was already recorded (a redelivery).</summary>
    private async Task<bool> TryRecordCallbackAsync(int orderId, string kind, string provider, string providerEventId, string detail, CancellationToken ct)
    {
        // Providers redeliver routinely; answer the common case without tripping the unique index
        // (and EF's error log). The index still settles two deliveries racing each other.
        if (await _db.TransferOrderEvents.AsNoTracking().AnyAsync(e => e.Provider == provider && e.ProviderEventId == providerEventId, ct))
        {
            return false;
        }

        try
        {
            await RecordAsync(orderId, kind, null, null, "ProviderWebhook", provider, providerEventId, detail, ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The racing delivery won. The transaction is aborted; the caller rolls it back.
            _db.ChangeTracker.Clear();
            return false;
        }
    }

    private async Task RecordAsync(int orderId, string kind, string? from, string? to, string source, string? provider,
        string? providerEventId, string? detail, CancellationToken ct)
    {
        _db.TransferOrderEvents.Add(new TransferOrderEvent
        {
            TransferOrderId = orderId,
            Kind = kind,
            FromStatus = from,
            ToStatus = to,
            Source = source,
            Provider = provider,
            ProviderEventId = providerEventId,
            Detail = detail is { Length: > 500 } ? detail[..500] : detail
        });
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Crockford base32: no I, L, O or U, so a reference read aloud is not misheard.</summary>
    private const string ReferenceAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>
    /// IR-MY26101407H5K2QX: "IR", the sending country, the date (yyMMdd) and eight random base32
    /// characters — about a trillion references per country per day, so collisions stay rare at
    /// any volume (the old six random digits gave 900,000 per country, ever). 19 characters.
    /// </summary>
    private static string NewReference(string sourceCountry)
    {
        Span<char> random = stackalloc char[8];
        for (var i = 0; i < random.Length; i++) random[i] = ReferenceAlphabet[RandomNumberGenerator.GetInt32(ReferenceAlphabet.Length)];
        return $"IR-{sourceCountry.ToUpperInvariant()}{DateTime.UtcNow:yyMMdd}{random}";
    }
}

/// <summary>Queue payload: submit this order's payout.</summary>
public sealed record RemittancePayoutJob(int OrderId);
