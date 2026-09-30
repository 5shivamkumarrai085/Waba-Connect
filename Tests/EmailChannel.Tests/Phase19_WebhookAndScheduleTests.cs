using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Services.Integrations;

namespace EmailChannel.Tests;

/// <summary>
/// Phase 19 — signed outbound webhooks (signature, SSRF guard, delivery, connection filter,
/// replay, failure) and the report-schedule timetable.
/// </summary>
public static class Phase19_WebhookAndScheduleTests
{
    private sealed record Received(string Body, string? Signature, string? EventId, string? EventType);

    public static async Task RunAsync(TestHarness harness, TestRun run)
    {
        run.BeginPhase("Phase 19 — webhooks and scheduled reports");

        run.Section("Signatures");
        var now = DateTimeOffset.UtcNow;
        var header = WebhookSigner.Sign("whsec_test", now.ToUnixTimeSeconds(), """{"a":1}""");
        run.Check("a signature verifies with the right secret", WebhookSigner.Verify("whsec_test", header, """{"a":1}""", TimeSpan.FromMinutes(5), now));
        run.Check("a changed body fails", !WebhookSigner.Verify("whsec_test", header, """{"a":2}""", TimeSpan.FromMinutes(5), now));
        run.Check("the wrong secret fails", !WebhookSigner.Verify("whsec_other", header, """{"a":1}""", TimeSpan.FromMinutes(5), now));
        run.Check("an old signature is refused (replay window)", !WebhookSigner.Verify("whsec_test", header, """{"a":1}""", TimeSpan.FromMinutes(5), now.AddMinutes(10)));
        run.Check("secrets are random and long", WebhookSigner.NewSecret() != WebhookSigner.NewSecret() && WebhookSigner.NewSecret().Length > 40);

        run.Section("Event selection");
        run.Check("a family wildcard matches its events", WebhookEventCatalog.Matches("email.*", "email.opened") && !WebhookEventCatalog.Matches("email.*", "message.sent"));
        run.Check("* matches everything", WebhookEventCatalog.Matches("*", "consent.changed"));
        Exception? unknown = null;
        try { WebhookEventCatalog.Normalize(["email.teleported"]); } catch (Exception ex) { unknown = ex; }
        run.Check("an unknown event is refused", unknown is ArgumentException);

        run.Section("SSRF guard (production settings)");
        var strict = new WebhookUrlGuard(allowHttp: false, allowPrivateNetworks: false);
        async Task<bool> Refused(string url)
        {
            try { await strict.ValidateAsync(url); return false; } catch (ArgumentException) { return true; }
        }
        run.Check("plain http is refused", await Refused("http://example.com/hook"));
        run.Check("loopback is refused", await Refused("https://127.0.0.1/hook"));
        run.Check("localhost is refused", await Refused("https://localhost/hook"));
        run.Check("the cloud metadata address is refused", await Refused("https://169.254.169.254/latest/meta-data"));
        run.Check("private ranges are refused", await Refused("https://10.0.0.5/hook") && await Refused("https://192.168.1.10/hook") && await Refused("https://[::1]/hook"));
        run.Check("credentials in the URL are refused", await Refused("https://user:pass@example.com/hook"));
        run.Check("IPv4-mapped loopback is blocked", WebhookUrlGuard.IsBlocked(IPAddress.Parse("::ffff:127.0.0.1")));
        run.Check("a public address is allowed", !WebhookUrlGuard.IsBlocked(IPAddress.Parse("93.184.216.34")));

        // A local receiver: records what arrives, answers 200 (or 400 for /reject).
        var port = FreePort();
        var received = new ConcurrentQueue<Received>();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        using var stop = new CancellationTokenSource();
        var serving = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); } catch { break; }
                using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync();
                received.Enqueue(new Received(body, ctx.Request.Headers[WebhookSigner.SignatureHeader],
                    ctx.Request.Headers[WebhookSigner.EventIdHeader], ctx.Request.Headers[WebhookSigner.EventTypeHeader]));
                ctx.Response.StatusCode = ctx.Request.Url!.AbsolutePath.EndsWith("/reject") ? 400 : 200;
                ctx.Response.Close();
            }
        });

        var subscriptionIds = new List<int>();
        int? connectionA = null, connectionB = null;
        try
        {
            var connections = await harness.ScalarAsync("""SELECT string_agg("Id"::text, ',' ORDER BY "Id") FROM (SELECT "Id" FROM "Connections" ORDER BY "Id" LIMIT 2) c""");
            var ids = (connections ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
            if (ids.Count >= 2) { connectionA = ids[0]; connectionB = ids[1]; }

            run.Section("Delivery");
            WebhookSubscriptionDto created = null!;
            await harness.InScopeAsync(async s =>
                created = await s.GetRequiredService<IWebhookSubscriptionService>().CreateAsync(new SaveWebhookRequest
                {
                    Name = $"{TestHarness.Prefix} hook {harness.Tag}",
                    Url = $"http://localhost:{port}/hook",
                    EventTypes = ["consent.*", "webhook.test"],
                    ConnectionIds = connectionA is { } a ? [a] : null
                }));
            subscriptionIds.Add(created.Id);
            run.Check("the secret is returned once, on create", created.Secret?.StartsWith("whsec_") == true);
            var stored = await harness.ScalarAsync("""SELECT "Secret" FROM "WebhookSubscriptions" WHERE "Id" = @id""", ("id", created.Id));
            run.Check("and stored encrypted", stored is not null && stored != created.Secret && stored.StartsWith("enc:"));

            WebhookDeliveryDto test = null!;
            await harness.InScopeAsync(async s => test = await s.GetRequiredService<IWebhookSubscriptionService>().SendTestAsync(created.Id));
            run.Check("a test event is delivered", test.Status == "Delivered" && test.ResponseCode == 200, $"{test.Status} {test.ResponseCode} {test.Error}");
            var first = received.TryDequeue(out var r1) ? r1 : null;
            run.Check("the receiver can verify the signature", first is not null && WebhookSigner.Verify(created.Secret!, first.Signature ?? "", first.Body, TimeSpan.FromMinutes(5), DateTimeOffset.UtcNow));
            run.Check("with the event id and type in headers", first?.EventId == test.EventId && first?.EventType == "webhook.test");

            // Queued path, through the real worker.
            IWebhookEmitter emitter = null!;
            await harness.InScopeAsync(s => { emitter = s.GetRequiredService<IWebhookEmitter>(); return Task.CompletedTask; });
            harnessCache(harness).Remove(WebhookEmitter.CacheKey);
            await emitter.EmitAsync("consent.changed", connectionA, new Dictionary<string, object?> { ["contactId"] = 1, ["status"] = "OptedOut" });
            if (connectionB is { } b)
                await emitter.EmitAsync("consent.changed", b, new Dictionary<string, object?> { ["contactId"] = 2 });
            await emitter.EmitAsync("message.sent", connectionA, new Dictionary<string, object?> { ["campaignId"] = 1 });

            var queued = await harness.ScalarAsync("""SELECT count(*) FROM "WebhookDeliveries" WHERE "SubscriptionId" = @id AND "EventType" <> 'webhook.test'""", ("id", created.Id));
            run.Check("only the subscribed event on the subscribed connection is queued", queued == "1", queued);

            await RunWorkerUntilAsync(harness, async () =>
                await harness.ScalarAsync("""SELECT "Status" FROM "WebhookDeliveries" WHERE "SubscriptionId" = @id AND "EventType" = 'consent.changed'""", ("id", created.Id)) == "Delivered");
            var status = await harness.ScalarAsync("""SELECT "Status" FROM "WebhookDeliveries" WHERE "SubscriptionId" = @id AND "EventType" = 'consent.changed'""", ("id", created.Id));
            run.Check("the worker delivers the queued event", status == "Delivered", status);
            var queuedBody = received.TryDequeue(out var r2) ? r2 : null;
            run.Check("and it is signed too", queuedBody is not null && WebhookSigner.Verify(created.Secret!, queuedBody.Signature ?? "", queuedBody.Body, TimeSpan.FromMinutes(5), DateTimeOffset.UtcNow));
            run.Check("personal data is left out by default", queuedBody is not null && !queuedBody.Body.Contains("recipient"));

            run.Section("Replay");
            var deliveryId = long.Parse((await harness.ScalarAsync("""SELECT "Id" FROM "WebhookDeliveries" WHERE "SubscriptionId" = @id AND "EventType" = 'consent.changed'""", ("id", created.Id)))!);
            await harness.InScopeAsync(s => s.GetRequiredService<IWebhookSubscriptionService>().ReplayAsync(deliveryId));
            await RunWorkerUntilAsync(harness, () => Task.FromResult(!received.IsEmpty));
            var replayed = received.TryDequeue(out var r3) ? r3 : null;
            run.Check("a replay is delivered again with the same event id", replayed is not null && replayed.EventId == queuedBody?.EventId);

            run.Section("Batched writes (the emitter running as a background service)");
            await harness.InScopeAsync(async services =>
            {
                var batching = ActivatorUtilities.CreateInstance<WebhookEmitter>(services);
                await batching.StartAsync(CancellationToken.None);
                await Task.Delay(200);
                for (var i = 0; i < 5; i++)
                    await batching.EmitAsync("consent.changed", connectionA, new Dictionary<string, object?> { ["contactId"] = 100 + i });
                var deadline = DateTime.UtcNow.AddSeconds(10);
                string? count = null;
                while (DateTime.UtcNow < deadline)
                {
                    count = await harness.ScalarAsync("""SELECT count(*) FROM "WebhookDeliveries" WHERE "SubscriptionId" = @id AND "EventType" = 'consent.changed'""", ("id", created.Id));
                    if (count == "6") break;
                    await Task.Delay(200);
                }
                await batching.StopAsync(CancellationToken.None);
                run.Check("buffered events are written and queued", count == "6", count);
            });
            await RunWorkerUntilAsync(harness, async () =>
                await harness.ScalarAsync("""SELECT count(*) FROM "WebhookDeliveries" WHERE "SubscriptionId" = @id AND "Status" = 'Delivered' AND "EventType" = 'consent.changed'""", ("id", created.Id)) == "6");
            run.Check("and delivered",
                await harness.ScalarAsync("""SELECT count(*) FROM "WebhookDeliveries" WHERE "SubscriptionId" = @id AND "Status" = 'Delivered' AND "EventType" = 'consent.changed'""", ("id", created.Id)) == "6",
                await harness.ScalarAsync("""SELECT string_agg(d."Id" || ':' || d."Status" || ':' || d."Attempts" || ':' || coalesce(d."Error", '') || ':' || coalesce((SELECT string_agg(j."Status" || '/' || coalesce(j."LastError", ''), ',') FROM "JobQueue" j WHERE j."IdempotencyKey" = 'delivery:' || d."Id" || ':r' || d."ReplayCount"), 'nojob'), ' | ') FROM "WebhookDeliveries" d WHERE d."SubscriptionId" = @id AND d."EventType" = 'consent.changed'""", ("id", created.Id)));
            while (received.TryDequeue(out _)) { }

            run.Section("A receiver that refuses");
            WebhookSubscriptionDto rejecting = null!;
            await harness.InScopeAsync(async s =>
                rejecting = await s.GetRequiredService<IWebhookSubscriptionService>().CreateAsync(new SaveWebhookRequest
                {
                    Name = $"{TestHarness.Prefix} reject {harness.Tag}",
                    Url = $"http://localhost:{port}/reject",
                    EventTypes = ["*"]
                }));
            subscriptionIds.Add(rejecting.Id);
            harnessCache(harness).Remove(WebhookEmitter.CacheKey);
            await emitter.EmitAsync("conversation.assigned", null, new Dictionary<string, object?> { ["conversationId"] = 1 });
            await RunWorkerUntilAsync(harness, async () =>
                await harness.ScalarAsync("""SELECT "Status" FROM "WebhookDeliveries" WHERE "SubscriptionId" = @id""", ("id", rejecting.Id)) == "Failed");
            run.Check("a 400 fails at once, without retries",
                await harness.ScalarAsync("""SELECT "Status" || '/' || "Attempts" || '/' || "ResponseCode" FROM "WebhookDeliveries" WHERE "SubscriptionId" = @id""", ("id", rejecting.Id)) == "Failed/1/400");
            run.Check("and counts towards switching the webhook off",
                await harness.ScalarAsync("""SELECT "FailureStreak" FROM "WebhookSubscriptions" WHERE "Id" = @id""", ("id", rejecting.Id)) == "1");
        }
        finally
        {
            stop.Cancel();
            listener.Stop();
            try { await serving; } catch { }
            foreach (var id in subscriptionIds)
                await harness.ExecAsync("""DELETE FROM "WebhookSubscriptions" WHERE "Id" = @id""", ("id", id));
            await harness.ExecAsync("""DELETE FROM "JobQueue" WHERE "QueueName" = @q""", ("q", WhatsAppCampaignApi.Services.Queue.QueueNames.WebhookOut));
            harnessCache(harness).Remove(WebhookEmitter.CacheKey);
        }

        run.Section("Report schedule timetable");
        ReportSchedule S(string frequency, string time, string zone, int? dow = null, int? dom = null) =>
            new() { Frequency = frequency, TimeOfDay = time, TimeZone = zone, DayOfWeek = dow, DayOfMonth = dom };

        var daily = ReportScheduleRunner.NextRun(S("Daily", "09:00", "Asia/Kolkata"), new DateTime(2026, 3, 10, 5, 0, 0, DateTimeKind.Utc));
        run.Check("daily 09:00 in Kolkata is 03:30 UTC the next day after it has passed", daily == new DateTime(2026, 3, 11, 3, 30, 0), daily.ToString("u"));
        var weekly = ReportScheduleRunner.NextRun(S("Weekly", "08:00", "UTC", dow: 1), new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc));
        run.Check("weekly on Monday picks the next Monday", weekly == new DateTime(2026, 3, 16, 8, 0, 0), weekly.ToString("u"));
        var monthly = ReportScheduleRunner.NextRun(S("Monthly", "07:00", "UTC", dom: 1), new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc));
        run.Check("monthly on the 1st picks next month", monthly == new DateTime(2026, 4, 1, 7, 0, 0), monthly.ToString("u"));
        var gap = ReportScheduleRunner.NextRun(S("Daily", "02:30", "America/New_York"), new DateTime(2026, 3, 8, 0, 0, 0, DateTimeKind.Utc));
        run.Check("a time skipped by daylight saving runs an hour later, not never", gap == new DateTime(2026, 3, 8, 7, 30, 0), gap.ToString("u"));
    }

    private static IMemoryCache harnessCache(TestHarness harness)
    {
        IMemoryCache cache = null!;
        harness.InScopeAsync(s => { cache = s.GetRequiredService<IMemoryCache>(); return Task.CompletedTask; }).GetAwaiter().GetResult();
        return cache;
    }

    /// <summary>Runs the real delivery worker until the condition holds (or 20 seconds pass).</summary>
    private static async Task RunWorkerUntilAsync(TestHarness harness, Func<Task<bool>> done)
    {
        await harness.InScopeAsync(async services =>
        {
            var worker = ActivatorUtilities.CreateInstance<WebhookDeliveryWorker>(services);
            await worker.StartAsync(CancellationToken.None);
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline && !await done()) await Task.Delay(250);
            await worker.StopAsync(CancellationToken.None);
        });
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
