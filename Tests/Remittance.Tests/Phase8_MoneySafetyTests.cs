using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Remittance;
using WhatsAppCampaignApi.Services.Remittance.Providers;

namespace Remittance.Tests;

/// <summary>
/// The audit's money-safety fixes: concurrent callbacks and payout submissions never pay twice, a
/// success without an amount is not trusted, a crash-orphaned paid order is picked up again,
/// references have room to grow, the mocks cannot run outside Development, and a customer can only
/// have one open Send Money form.
/// </summary>
public static class Phase8_MoneySafetyTests
{
    public static async Task RunAsync(RemittanceHarness harness, TestRun run)
    {
        run.BeginPhase("Phase 8 — Money safety");

        try
        {
            run.Section("Concurrent callbacks and payouts");
            var (_, sessionId, _, reference) = await Journey.OrderAsync(harness);
            var order = await LoadAsync(harness, sessionId);

            run.Check("references carry the date and 8 random characters (19 long)",
                Regex.IsMatch(reference, @"^IR-[A-Z]{2}\d{6}[0-9A-HJKMNP-TV-Z]{8}$"), reference);

            var noAmount = new PaymentWebhookEvent($"evt_zzrt_{Guid.NewGuid():N}", order.ProviderPaymentId!, PaymentEventKind.Succeeded, null, null, DateTime.UtcNow);
            run.Check("a success callback without an amount is not trusted", await ApplyPaymentAsync(harness, noAmount) == CallbackOutcome.Ignored);
            run.Check("…the order is still unpaid", (await LoadAsync(harness, sessionId)).PaymentStatus == TransferPaymentStatus.Pending);

            var paidEvent = new PaymentWebhookEvent($"evt_zzrt_{Guid.NewGuid():N}", order.ProviderPaymentId!, PaymentEventKind.Succeeded, order.TotalPayable, "MYR", DateTime.UtcNow);
            var outcomes = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => ApplyPaymentAsync(harness, paidEvent)));
            run.Check("the same callback delivered four times at once is applied exactly once",
                outcomes.Count(o => o == CallbackOutcome.Applied) == 1 && outcomes.Count(o => o == CallbackOutcome.Duplicate) == 3,
                string.Join(",", outcomes));
            var callbackEvents = Convert.ToInt32(await harness.ScalarAsync(
                "SELECT count(*) FROM \"TransferOrderEvents\" WHERE \"TransferOrderId\" = @p0 AND \"Kind\" = 'PaymentCallback' AND \"ProviderEventId\" = @p1", order.Id, paidEvent.ProviderEventId));
            run.Check("…and recorded once", callbackEvents == 1, callbackEvents.ToString());
            await harness.ExecAsync("DELETE FROM \"JobQueue\" WHERE \"IdempotencyKey\" = @p0", $"payout:{order.Id}");

            var submissions = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
                harness.InScopeAsync(sp => sp.GetRequiredService<ITransferOrderService>().SubmitPayoutAsync(order.Id, CancellationToken.None))));
            var claims = Convert.ToInt32(await harness.ScalarAsync(
                "SELECT count(*) FROM \"TransferOrderEvents\" WHERE \"TransferOrderId\" = @p0 AND \"ToStatus\" = 'Submitting'", order.Id));
            var processing = await LoadAsync(harness, sessionId);
            run.Check("four simultaneous payout submissions reach the provider once", claims == 1, claims.ToString());
            run.Check("…and the order is Processing with one payout reference",
                processing.OrderStatus == TransferOrderStatus.Processing && submissions.Count(s => s.Accepted) >= 1
                && submissions.Where(s => s.PayoutReference is not null).Select(s => s.PayoutReference).Distinct().Count() == 1,
                $"{processing.OrderStatus}, refs {string.Join(",", submissions.Select(s => s.PayoutReference ?? "-"))}");

            run.Section("Repairing what a crash leaves behind");
            var (_, orphanSessionId, _, _) = await Journey.OrderAsync(harness);
            var orphan = await LoadAsync(harness, orphanSessionId);
            // A paid order whose payout job was never queued (the process died between the two).
            await harness.ExecAsync(
                "UPDATE \"TransferOrders\" SET \"OrderStatus\" = 'Paid', \"PaymentStatus\" = 'Succeeded', \"UpdatedAt\" = now() - interval '10 minutes' WHERE \"Id\" = @p0", orphan.Id);
            await harness.ExecAsync("DELETE FROM \"JobQueue\" WHERE \"IdempotencyKey\" = @p0", $"payout:{orphan.Id}");
            var foreignStalled = Convert.ToInt32(await harness.ScalarAsync(
                $"SELECT count(*) FROM \"TransferOrders\" o JOIN \"Contacts\" c ON c.\"Id\" = o.\"ContactId\" WHERE c.\"Name\" NOT LIKE '{RemittanceHarness.Prefix}%' AND o.\"OrderStatus\" IN ('Paid', 'Submitting')"));
            if (foreignStalled > 0)
            {
                run.Skip("a paid order with no payout job gets one", $"{foreignStalled} real paid order(s) in this database would be touched");
            }
            else
            {
                await harness.InScopeAsync(sp => sp.GetRequiredService<ITransferOrderService>().ReconcileAsync(CancellationToken.None));
                var queued = Convert.ToInt32(await harness.ScalarAsync("SELECT count(*) FROM \"JobQueue\" WHERE \"IdempotencyKey\" = @p0", $"payout:{orphan.Id}"));
                run.Check("a paid order with no payout job gets one", queued == 1, queued.ToString());

                await harness.ExecAsync("UPDATE \"TransferOrders\" SET \"OrderStatus\" = 'Submitting', \"UpdatedAt\" = now() - interval '30 minutes' WHERE \"Id\" = @p0", orphan.Id);
                await harness.InScopeAsync(sp => sp.GetRequiredService<ITransferOrderService>().ReconcileAsync(CancellationToken.None));
                var review = await LoadAsync(harness, orphanSessionId);
                run.Check("a submission that never reported back goes to a person, not resent", review.OrderStatus == TransferOrderStatus.NeedsReview, review.OrderStatus.ToString());
            }
            await harness.ExecAsync("DELETE FROM \"JobQueue\" WHERE \"IdempotencyKey\" = @p0", $"payout:{orphan.Id}");

            run.Section("One open form per customer");
            var (contactId, firstSession, _) = await harness.NewSessionAsync();
            var (second, _) = await harness.InScopeAsync(async sp =>
            {
                var phone = await sp.GetRequiredService<AppDbContext>().Contacts.Where(c => c.Id == contactId).Select(c => c.Phone).FirstAsync();
                return await sp.GetRequiredService<IRemittanceSessionService>().StartAsync(contactId, phone.TrimStart('+'), harness.ConnectionId, CancellationToken.None);
            });
            var firstStatus = (string?)await harness.ScalarAsync("SELECT \"Status\" FROM \"RemittanceSessions\" WHERE \"Id\" = @p0", firstSession);
            run.Check("opening the form again cancels the older one", firstStatus == nameof(RemittanceSessionStatus.Cancelled) && second.Status == RemittanceSessionStatus.Started, firstStatus);

            run.Section("Development-only adapters");
            var production = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Remittance:Enabled"] = "true",
                ["Remittance:Otp:Provider"] = "Mock"
            }).Build();
            var refused = false;
            try { RemittanceStartupGuard.Validate(production, new FixedEnvironment("Staging")); }
            catch (InvalidOperationException) { refused = true; }
            run.Check("a non-Development environment refuses the mock OTP", refused);
            var devAllowed = true;
            try { RemittanceStartupGuard.Validate(production, new FixedEnvironment("Development")); }
            catch (InvalidOperationException) { devAllowed = false; }
            run.Check("Development still allows it", devAllowed);
        }
        catch (Exception ex)
        {
            run.Error("Phase 8 aborted", ex);
        }
    }

    private sealed class FixedEnvironment(string name) : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "WhatsAppCampaignApi";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private static Task<CallbackOutcome> ApplyPaymentAsync(RemittanceHarness harness, PaymentWebhookEvent evt) =>
        harness.InScopeAsync(sp => sp.GetRequiredService<ITransferOrderService>().ApplyPaymentEventAsync(MockPaymentProvider.Name, evt, CancellationToken.None));

    private static Task<TransferOrder> LoadAsync(RemittanceHarness harness, Guid sessionId) =>
        harness.InScopeAsync(sp => sp.GetRequiredService<AppDbContext>().TransferOrders.AsNoTracking().FirstAsync(o => o.SessionId == sessionId));
}
