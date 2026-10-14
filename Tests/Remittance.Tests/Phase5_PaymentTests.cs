using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Queue;
using WhatsAppCampaignApi.Services.Remittance;
using WhatsAppCampaignApi.Services.Remittance.Providers;

namespace Remittance.Tests;

/// <summary>
/// Payment and payout status: only a verified provider callback moves money states; duplicates,
/// out-of-order and mismatched callbacks change nothing; unpaid orders expire.
/// </summary>
public static class Phase5_PaymentTests
{
    public static async Task RunAsync(RemittanceHarness harness, TestRun run)
    {
        run.BeginPhase("Phase 5 — Payment and payout status");

        try
        {
            var (_, sessionId, _, reference) = await Journey.OrderAsync(harness);
            var order = await LoadAsync(harness, sessionId);
            var paymentId = order.ProviderPaymentId!;

            run.Section("Callback verification (the only way in)");
            var succeeded = Body($"evt_zzrt_{Guid.NewGuid():N}", "payment.succeeded", paymentId, order.TotalPayable, "MYR");

            run.Check("unsigned callback → 401", await IngestAsync(harness, "payment", "Mock", succeeded, signature: null) == 401);
            run.Check("callback signed with the wrong secret → 401",
                await IngestAsync(harness, "payment", "Mock", succeeded, MockCallbackSigning.Sign("not-the-secret", succeeded, DateTimeOffset.UtcNow)) == 401);
            run.Check("callback signed 10 minutes ago (replay) → 401",
                await IngestAsync(harness, "payment", "Mock", succeeded, MockCallbackSigning.Sign(RemittanceHarness.PaymentSecret, succeeded, DateTimeOffset.UtcNow.AddMinutes(-10))) == 401);
            var tampered = succeeded.Replace("payment.succeeded", "payment.refunded");
            run.Check("body changed after signing → 401",
                await IngestAsync(harness, "payment", "Mock", tampered, MockCallbackSigning.Sign(RemittanceHarness.PaymentSecret, succeeded, DateTimeOffset.UtcNow)) == 401);
            run.Check("unknown provider → 404", await IngestAsync(harness, "payment", "NoSuchGateway", succeeded, "t=1,v1=00") == 404);
            var garbage = "{\"id\":\"evt_zzrt_x\",\"type\":\"payment.teleported\",\"objectId\":\"x\",\"created\":\"2026-10-12T00:00:00Z\"}";
            run.Check("signed but unknown event type → 400",
                await IngestAsync(harness, "payment", "Mock", garbage, MockCallbackSigning.Sign(RemittanceHarness.PaymentSecret, garbage, DateTimeOffset.UtcNow)) == 400);

            var still = await LoadAsync(harness, sessionId);
            run.Check("rejected callbacks changed nothing", still.PaymentStatus == TransferPaymentStatus.Pending && still.OrderStatus == TransferOrderStatus.AwaitingPayment);

            var accepted = await IngestAsync(harness, "payment", "Mock", succeeded, MockCallbackSigning.Sign(RemittanceHarness.PaymentSecret, succeeded, DateTimeOffset.UtcNow));
            var eventId = JsonDocument.Parse(succeeded).RootElement.GetProperty("id").GetString()!;
            var queued = (string?)await harness.ScalarAsync("SELECT \"Payload\"::text FROM \"JobQueue\" WHERE \"QueueName\" = @p0 AND \"IdempotencyKey\" = @p1",
                QueueNames.RemittanceCallback, $"payment:Mock:{eventId}");
            run.Check("a correctly signed callback is accepted and queued", accepted is null && queued is not null, accepted?.ToString());
            await IngestAsync(harness, "payment", "Mock", succeeded, MockCallbackSigning.Sign(RemittanceHarness.PaymentSecret, succeeded, DateTimeOffset.UtcNow));
            run.Check("a redelivered callback is queued once",
                Convert.ToInt32(await harness.ScalarAsync("SELECT count(*) FROM \"JobQueue\" WHERE \"IdempotencyKey\" = @p0", $"payment:Mock:{eventId}")) == 1);
            run.Check("accepting a callback does not by itself change the order", (await LoadAsync(harness, sessionId)).PaymentStatus == TransferPaymentStatus.Pending);

            // Apply it as the worker would (the job row is removed so no other process races us).
            await harness.ExecAsync("DELETE FROM \"JobQueue\" WHERE \"IdempotencyKey\" = @p0", $"payment:Mock:{eventId}");
            var job = JsonSerializer.Deserialize<RemittanceCallbackJob>(queued!)!;

            run.Section("Payment events");
            var mismatch = new PaymentWebhookEvent($"evt_zzrt_{Guid.NewGuid():N}", paymentId, PaymentEventKind.Succeeded, 1.00m, "MYR", DateTime.UtcNow);
            run.Check("a success for the wrong amount is not applied", await ApplyPaymentAsync(harness, mismatch) == CallbackOutcome.Ignored);
            run.Check("…and the order is still unpaid", (await LoadAsync(harness, sessionId)).PaymentStatus == TransferPaymentStatus.Pending);

            var sentBefore = harness.WhatsApp.Sent.Count;
            run.Check("the verified success is applied", await ApplyPaymentAsync(harness, job.Payment!) == CallbackOutcome.Applied);
            var paid = await LoadAsync(harness, sessionId);
            run.Check("order Paid, payment Succeeded", paid.OrderStatus == TransferOrderStatus.Paid && paid.PaymentStatus == TransferPaymentStatus.Succeeded && paid.PaidAt is not null,
                $"{paid.OrderStatus}/{paid.PaymentStatus}");
            run.Check("the payout is queued", Convert.ToInt32(await harness.ScalarAsync("SELECT count(*) FROM \"JobQueue\" WHERE \"IdempotencyKey\" = @p0", $"payout:{paid.Id}")) == 1);
            await harness.ExecAsync("DELETE FROM \"JobQueue\" WHERE \"IdempotencyKey\" = @p0", $"payout:{paid.Id}");
            run.Check("the customer is told the payment arrived", harness.WhatsApp.Sent.Skip(sentBefore).Any(s => s.Payload.Contains("Payment received")));

            run.Check("the same event again is a duplicate", await ApplyPaymentAsync(harness, job.Payment!) == CallbackOutcome.Duplicate);
            var lateFailure = new PaymentWebhookEvent($"evt_zzrt_{Guid.NewGuid():N}", paymentId, PaymentEventKind.Failed, null, null, DateTime.UtcNow);
            run.Check("a late \"failed\" after success is ignored", await ApplyPaymentAsync(harness, lateFailure) == CallbackOutcome.Ignored);
            run.Check("…the payment stays Succeeded", (await LoadAsync(harness, sessionId)).PaymentStatus == TransferPaymentStatus.Succeeded);

            run.Section("Payout");
            var submitted = await harness.InScopeAsync(sp => sp.GetRequiredService<ITransferOrderService>().SubmitPayoutAsync(paid.Id, CancellationToken.None));
            var processing = await LoadAsync(harness, sessionId);
            run.Check("the payout is submitted and the order is Processing",
                submitted.Accepted && processing.OrderStatus == TransferOrderStatus.Processing && processing.PayoutReference == submitted.PayoutReference, processing.OrderStatus.ToString());
            var again = await harness.InScopeAsync(sp => sp.GetRequiredService<ITransferOrderService>().SubmitPayoutAsync(paid.Id, CancellationToken.None));
            run.Check("submitting again does not create a second payout", again.PayoutReference == submitted.PayoutReference);

            var payoutBody = Body($"evt_zzrt_{Guid.NewGuid():N}", "payout.completed", submitted.PayoutReference!, null, null);
            run.Check("a payout callback signed with the payment secret → 401",
                await IngestAsync(harness, "payout", "Mock", payoutBody, MockCallbackSigning.Sign(RemittanceHarness.PaymentSecret, payoutBody, DateTimeOffset.UtcNow)) == 401);
            var payoutAccepted = await IngestAsync(harness, "payout", "Mock", payoutBody, MockCallbackSigning.Sign(RemittanceHarness.PayoutSecret, payoutBody, DateTimeOffset.UtcNow));
            var payoutEventId = JsonDocument.Parse(payoutBody).RootElement.GetProperty("id").GetString()!;
            var payoutQueued = (string?)await harness.ScalarAsync("SELECT \"Payload\"::text FROM \"JobQueue\" WHERE \"IdempotencyKey\" = @p0", $"payout:Mock:{payoutEventId}");
            run.Check("a signed payout callback is accepted", payoutAccepted is null && payoutQueued is not null);
            await harness.ExecAsync("DELETE FROM \"JobQueue\" WHERE \"IdempotencyKey\" = @p0", $"payout:Mock:{payoutEventId}");

            var payoutJob = JsonSerializer.Deserialize<RemittanceCallbackJob>(payoutQueued!)!;
            run.Check("payout completed is applied", await ApplyPayoutAsync(harness, payoutJob.Payout!) == CallbackOutcome.Applied);
            var completed = await LoadAsync(harness, sessionId);
            run.Check("order Completed, payment still Succeeded",
                completed.OrderStatus == TransferOrderStatus.Completed && completed.PaymentStatus == TransferPaymentStatus.Succeeded && completed.CompletedAt is not null);
            run.Check("the payout callback again is a duplicate", await ApplyPayoutAsync(harness, payoutJob.Payout!) == CallbackOutcome.Duplicate);

            var timeline = await harness.InScopeAsync(sp => sp.GetRequiredService<AppDbContext>().TransferOrderEvents.AsNoTracking()
                .Where(e => e.TransferOrderId == completed.Id && e.Kind == "OrderStatus" && e.ToStatus != null)
                .OrderBy(e => e.Id).Select(e => e.ToStatus!).ToListAsync());
            run.Check("the timeline records every order step", timeline.SequenceEqual(["Created", "AwaitingPayment", "Paid", "Submitting", "Processing", "Completed"]), string.Join(" → ", timeline));
            var detail = await harness.InScopeAsync(sp => sp.GetRequiredService<IRemittanceAdminService>().GetOrderAsync(completed.Id, CancellationToken.None));
            run.Check("the admin order view shows both statuses",
                detail.Order.Reference == reference && detail.Order.OrderStatus == TransferOrderStatus.Completed && detail.Order.PaymentStatus == TransferPaymentStatus.Succeeded);

            run.Section("Failure and expiry");
            var (_, failedSessionId, _, _) = await Journey.OrderAsync(harness);
            var failedOrder = await LoadAsync(harness, failedSessionId);
            var failure = new PaymentWebhookEvent($"evt_zzrt_{Guid.NewGuid():N}", failedOrder.ProviderPaymentId!, PaymentEventKind.Failed, null, null, DateTime.UtcNow, "Declined by bank");
            run.Check("a failed payment is applied", await ApplyPaymentAsync(harness, failure) == CallbackOutcome.Applied);
            failedOrder = await LoadAsync(harness, failedSessionId);
            run.Check("order Failed, payment Failed", failedOrder.OrderStatus == TransferOrderStatus.Failed && failedOrder.PaymentStatus == TransferPaymentStatus.Failed);

            var (_, counterSessionId, _, _) = await Journey.OrderAsync(harness, "COUNTER");
            var counter = await LoadAsync(harness, counterSessionId);
            run.Check("pay-at-counter gets a payment reference", counter.PaymentReference?.StartsWith("MOCK-") == true, counter.PaymentReference);
            run.Check("its window is the method's 24 hours",
                counter.PaymentExpiresAt is { } window && Math.Abs((window - DateTime.UtcNow).TotalHours - 24) < 0.1);

            // The expiry sweep works on every order; only run it when this suite's orders are the only ones overdue.
            await harness.ExecAsync("UPDATE \"TransferOrders\" SET \"PaymentExpiresAt\" = now() - interval '1 minute' WHERE \"SessionId\" = @p0", counterSessionId);
            var foreignOverdue = Convert.ToInt32(await harness.ScalarAsync(
                $"SELECT count(*) FROM \"TransferOrders\" o JOIN \"Contacts\" c ON c.\"Id\" = o.\"ContactId\" WHERE c.\"Name\" NOT LIKE '{RemittanceHarness.Prefix}%' AND o.\"OrderStatus\" = 'AwaitingPayment' AND o.\"PaymentExpiresAt\" < now()"));
            if (foreignOverdue > 0)
            {
                run.Skip("unpaid orders expire after their window", $"{foreignOverdue} real overdue order(s) in this database would be touched");
            }
            else
            {
                var expired = await harness.InScopeAsync(sp => sp.GetRequiredService<ITransferOrderService>().ExpireOverdueAsync(CancellationToken.None));
                counter = await LoadAsync(harness, counterSessionId);
                run.Check("unpaid orders expire after their window",
                    expired >= 1 && counter.OrderStatus == TransferOrderStatus.Expired && counter.PaymentStatus == TransferPaymentStatus.Expired, $"{counter.OrderStatus}/{counter.PaymentStatus}");

                var latePayment = new PaymentWebhookEvent($"evt_zzrt_{Guid.NewGuid():N}", counter.ProviderPaymentId!, PaymentEventKind.Succeeded, counter.TotalPayable, "MYR", DateTime.UtcNow);
                run.Check("money arriving after expiry is still honoured", await ApplyPaymentAsync(harness, latePayment) == CallbackOutcome.Applied);
                counter = await LoadAsync(harness, counterSessionId);
                run.Check("…the order becomes Paid", counter.OrderStatus == TransferOrderStatus.Paid && counter.PaymentStatus == TransferPaymentStatus.Succeeded);
                await harness.ExecAsync("DELETE FROM \"JobQueue\" WHERE \"IdempotencyKey\" = @p0", $"payout:{counter.Id}");
            }

            var unknown = new PaymentWebhookEvent($"evt_zzrt_{Guid.NewGuid():N}", "mockpay_doesnotexist", PaymentEventKind.Succeeded, 1m, "MYR", DateTime.UtcNow);
            run.Check("a callback for an unknown payment is reported, not applied", await ApplyPaymentAsync(harness, unknown) == CallbackOutcome.UnknownOrder);
        }
        catch (Exception ex)
        {
            run.Error("Phase 5 aborted", ex);
        }
    }

    private static string Body(string id, string type, string objectId, decimal? amount, string? currency) =>
        JsonSerializer.Serialize(new MockCallback(id, type, objectId, amount, currency, DateTimeOffset.UtcNow), MockCallbackSigning.Json);

    private static Task<int?> IngestAsync(RemittanceHarness harness, string kind, string provider, string body, string? signature) =>
        harness.InScopeAsync(sp =>
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (signature is not null) headers[MockCallbackSigning.SignatureHeader] = signature;
            return sp.GetRequiredService<IRemittanceCallbackIngestion>().IngestAsync(kind, provider, headers, body, CancellationToken.None);
        });

    private static Task<CallbackOutcome> ApplyPaymentAsync(RemittanceHarness harness, PaymentWebhookEvent evt) =>
        harness.InScopeAsync(sp => sp.GetRequiredService<ITransferOrderService>().ApplyPaymentEventAsync(MockPaymentProvider.Name, evt, CancellationToken.None));

    private static Task<CallbackOutcome> ApplyPayoutAsync(RemittanceHarness harness, PayoutWebhookEvent evt) =>
        harness.InScopeAsync(sp => sp.GetRequiredService<ITransferOrderService>().ApplyPayoutEventAsync(MockPayoutProvider.Name, evt, CancellationToken.None));

    private static Task<TransferOrder> LoadAsync(RemittanceHarness harness, Guid sessionId) =>
        harness.InScopeAsync(sp => sp.GetRequiredService<AppDbContext>().TransferOrders.AsNoTracking().FirstAsync(o => o.SessionId == sessionId));
}
