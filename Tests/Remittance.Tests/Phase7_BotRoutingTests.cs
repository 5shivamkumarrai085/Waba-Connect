using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.BotFlow;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Services;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Services.Queue;
using WhatsAppCampaignApi.Services.WhatsApp;

namespace Remittance.Tests;

/// <summary>
/// The audit's routing bugs, reproduced against the real engine: a keyword typed while a menu is
/// waiting starts its own flow, a menu that gives up hands the message on instead of swallowing
/// it, STOP/START leave no menu behind, saving a flow never unbinds or reactivates it, and one
/// customer's queued messages run one at a time in order.
/// </summary>
public static class Phase7_BotRoutingTests
{
    private const string MenuKeyword = "zzhi";
    private const string SendKeyword = "zz send money";

    public static async Task RunAsync(RemittanceHarness harness, TestRun run)
    {
        run.BeginPhase("Phase 7 — Bot routing, STOP/START, ordered inbound queue");

        try
        {
            await TriggerRulesAsync(run);
            var (menuFlowId, sendFlowId) = await SeedFlowsAsync(harness);
            await RoutingAsync(harness, run, menuFlowId, sendFlowId);
            await ConsentCleanupAsync(harness, run, menuFlowId);
            await SafeSaveAsync(harness, run, menuFlowId);
            await OrderedQueueAsync(harness, run);
            PartitionKeys(run);
        }
        catch (Exception ex)
        {
            run.Error("Phase 7 aborted", ex);
        }
    }

    // ── pure rules ────────────────────────────────────────────────────────────────────────────

    private static Task TriggerRulesAsync(TestRun run)
    {
        run.Section("Trigger keyword rules");
        var exact = new BotFlowTrigger(BotFlowTrigger.ExactType, ["send money"]);
        run.Check("case, spacing and trailing punctuation are ignored", BotFlowTriggers.Match(exact, "  Send   MONEY! ") == TriggerMatch.Exact);
        run.Check("an exact trigger does not fire on a longer sentence", BotFlowTriggers.Match(exact, "i want to send money") == TriggerMatch.None);

        var node = new FlowNode { NodeType = "Start Trigger", DataJson = "{\"keywords\":\"hi, hello ,HEY\",\"triggerType\":\"on exact match\"}" };
        var parsed = BotFlowTriggers.Parse(node);
        run.Check("keywords stored as a comma-separated string are read", parsed is not null && parsed.Keywords.SequenceEqual(["hi", "hello", "hey"]),
            parsed is null ? "null" : string.Join("|", parsed.Keywords));
        run.Check("a trigger without keywords is unusable", BotFlowTriggers.Parse(new FlowNode { DataJson = "{\"keywords\":[]}" }) is null);

        var contains = new BotFlowTrigger(BotFlowTrigger.ContainsType, ["money"]);
        run.Check("an exact keyword match outranks \"contains\"", (int)BotFlowTriggers.Match(contains, "send money") < (int)BotFlowTriggers.Match(exact, "send money"));

        var menu = new FlowNode { DataJson = "{\"buttons\":[{\"Text\":\"Send Money\",\"value\":\"sm\"}]}" };
        run.Check("a menu's own option is recognised (field names in any case)", BotFlowTriggers.IsMenuOption(menu, "send money"));
        return Task.CompletedTask;
    }

    // ── fixture ───────────────────────────────────────────────────────────────────────────────

    private static async Task<(int MenuFlowId, int SendFlowId)> SeedFlowsAsync(RemittanceHarness harness)
    {
        var menuCanvas = Canvas(
            [
                Node("t1", "Start Trigger", new { keywords = new[] { MenuKeyword }, triggerType = "on exact match" }),
                Node("b1", "Button Message", new { messageText = "zz choose", buttons = new[] { new { text = "Option A", value = "a" }, new { text = "Option B", value = "b" } } }),
                Node("a1", "Text Message", new { messageText = "zz picked A" }),
                Node("b2", "Text Message", new { messageText = "zz picked B" })
            ],
            [Edge("t1", "b1"), Edge("b1", "a1", "button-0"), Edge("b1", "b2", "button-1")]);

        // "Option B" is also a keyword of the Send Money flow, to prove a menu's own option wins.
        var sendCanvas = Canvas(
            [
                Node("t1", "Start Trigger", new { keywords = new[] { SendKeyword, "option b" }, triggerType = "on exact match" }),
                Node("f1", "WhatsApp Flow", new { flowPurpose = "send_money", bodyText = "zz send money body", ctaText = "Send money" })
            ],
            [Edge("t1", "f1")]);

        return await harness.InScopeAsync(async sp =>
        {
            var flows = sp.GetRequiredService<IBotFlowService>();
            var menu = await flows.CreateAsync(new CreateBotFlowRequest { Name = $"{RemittanceHarness.Prefix}-menu-{harness.Tag}", IsActive = true, ConnectionId = harness.ConnectionId, FlowData = menuCanvas });
            var send = await flows.CreateAsync(new CreateBotFlowRequest { Name = $"{RemittanceHarness.Prefix}-send-{harness.Tag}", IsActive = true, ConnectionId = harness.ConnectionId, FlowData = sendCanvas });
            return (menu.Id, send.Id);
        });
    }

    private static string Canvas(object[] nodes, object[] edges) => JsonSerializer.Serialize(new { nodes, edges });
    private static object Node(string id, string type, object data) => new { id, type, position = new { x = 0, y = 0 }, data };
    private static object Edge(string source, string target, string? sourceHandle = null) => new { id = $"{source}-{target}", source, target, sourceHandle };

    private static async Task<string> NewBotContactAsync(RemittanceHarness harness)
    {
        var bare = $"{RemittanceHarness.BotPhonePrefix}{Random.Shared.Next(10, 99)}";
        await harness.ExecAsync("DELETE FROM \"ConversationStates\" WHERE \"PhoneNumber\" = @p0", bare);
        await harness.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            if (!await db.Contacts.IgnoreQueryFilters().AnyAsync(c => c.Phone == "+" + bare))
            {
                db.Contacts.Add(new Contact { Name = $"{RemittanceHarness.Prefix}-bot-{harness.Tag}", Phone = "+" + bare, IsActive = true, Source = "WhatsApp" });
                await db.SaveChangesAsync();
            }
            return 0;
        });
        return bare;
    }

    private static Task<bool> SayAsync(RemittanceHarness harness, string phone, string text) =>
        harness.InScopeAsync(sp => sp.GetRequiredService<IFlowExecutionService>().ExecuteFlowStepAsync(phone, text, harness.ConnectionId));

    private static Task<ConversationState?> ActiveStateAsync(RemittanceHarness harness, string phone) =>
        harness.InScopeAsync(sp => sp.GetRequiredService<AppDbContext>().ConversationStates.AsNoTracking()
            .Where(s => s.PhoneNumber == phone && s.Status == "Active").OrderByDescending(s => s.Id).FirstOrDefaultAsync());

    private static List<(string Method, string Phone, string Payload)> SentSince(RemittanceHarness harness, int before) =>
        harness.WhatsApp.Sent.Skip(before).ToList();

    // ── the reported bug ──────────────────────────────────────────────────────────────────────

    private static async Task RoutingAsync(RemittanceHarness harness, TestRun run, int menuFlowId, int sendFlowId)
    {
        run.Section("A keyword typed while a menu is waiting");
        var phone = await NewBotContactAsync(harness);

        var before = harness.WhatsApp.Sent.Count;
        await SayAsync(harness, phone, MenuKeyword);
        var menuState = await ActiveStateAsync(harness, phone);
        run.Check("the menu keyword opens the menu", SentSince(harness, before).Any(s => s.Payload.Contains("zz choose")) && menuState?.FlowId == menuFlowId,
            menuState?.FlowId.ToString() ?? "no state");

        before = harness.WhatsApp.Sent.Count;
        await SayAsync(harness, phone, "ZZ  Send Money!");
        var afterSwitch = await ActiveStateAsync(harness, phone);
        var sent = SentSince(harness, before);
        run.Check("\"send money\" while the menu waits launches Send Money, not the menu again",
            sent.Any(s => s.Payload.Contains("\"flow\"") && s.Payload.Contains("flow_token")) && !sent.Any(s => s.Payload.Contains("zz choose")),
            string.Join(" | ", sent.Select(s => s.Payload.Length > 60 ? s.Payload[..60] : s.Payload)));
        run.Check("…and the menu session is closed", afterSwitch is null, afterSwitch?.FlowId.ToString());

        run.Section("A menu's own option is never taken as another flow's keyword");
        await SayAsync(harness, phone, MenuKeyword);
        before = harness.WhatsApp.Sent.Count;
        await SayAsync(harness, phone, "Option B");
        sent = SentSince(harness, before);
        run.Check("\"Option B\" answers the menu even though it is also a Send Money keyword",
            sent.Any(s => s.Payload.Contains("zz picked B")) && !sent.Any(s => s.Payload.Contains("flow_token")),
            string.Join(" | ", sent.Select(s => s.Payload.Length > 60 ? s.Payload[..60] : s.Payload)));

        run.Section("A menu that gives up does not swallow the message");
        await SayAsync(harness, phone, MenuKeyword);
        before = harness.WhatsApp.Sent.Count;
        await SayAsync(harness, phone, "qqxzv plorp");
        run.Check("one unrecognised reply re-sends the menu once", SentSince(harness, before).Count(s => s.Payload.Contains("zz choose")) == 1);
        before = harness.WhatsApp.Sent.Count;
        await SayAsync(harness, phone, "qqxzv plorp again");
        sent = SentSince(harness, before);
        run.Check("the second one gets a closing reply instead of silence", sent.Any(s => s.Payload.Contains("No problem")),
            string.Join(" | ", sent.Select(s => s.Payload)));
        run.Check("…and no menu is left waiting", await ActiveStateAsync(harness, phone) is null);

        await SayAsync(harness, phone, MenuKeyword);
        before = harness.WhatsApp.Sent.Count;
        await SayAsync(harness, phone, "qqxzv plorp");
        await SayAsync(harness, phone, SendKeyword);
        run.Check("a keyword after an unrecognised reply still starts its flow",
            SentSince(harness, before).Any(s => s.Payload.Contains("flow_token")));

        run.Section("Orphaned sessions");
        await SayAsync(harness, phone, MenuKeyword);
        await harness.ExecAsync("UPDATE \"BotFlows\" SET \"IsActive\" = false WHERE \"Id\" = @p0", menuFlowId);
        before = harness.WhatsApp.Sent.Count;
        await SayAsync(harness, phone, "Option A");
        run.Check("a flow switched off mid-conversation stops answering", !SentSince(harness, before).Any(s => s.Payload.Contains("zz picked A")));
        run.Check("…and its session is closed", await ActiveStateAsync(harness, phone) is null);
        await harness.ExecAsync("UPDATE \"BotFlows\" SET \"IsActive\" = true WHERE \"Id\" = @p0", menuFlowId);
    }

    private static async Task ConsentCleanupAsync(RemittanceHarness harness, TestRun run, int menuFlowId)
    {
        run.Section("STOP / START leave no menu behind");
        var phone = await NewBotContactAsync(harness);
        await SayAsync(harness, phone, MenuKeyword);
        run.Check("a menu is waiting before STOP", (await ActiveStateAsync(harness, phone))?.FlowId == menuFlowId);

        var closed = await harness.InScopeAsync(sp => sp.GetRequiredService<IBotSessionCleanup>().EndAutomationAsync("+" + phone));
        run.Check("opting out closes the waiting menu", closed >= 1 && await ActiveStateAsync(harness, phone) is null, closed.ToString());

        await harness.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            db.BotSuppressions.Add(new BotSuppression { PhoneNumber = phone, MatchedKeyword = "zz-stop", CreatedAt = DateTime.UtcNow, ResumeAt = null });
            await db.SaveChangesAsync();
            return 0;
        });
        var lifted = await harness.InScopeAsync(sp => sp.GetRequiredService<IBotSessionCleanup>().LiftSuppressionsAsync(phone));
        var stillSuppressed = Convert.ToInt32(await harness.ScalarAsync(
            "SELECT count(*) FROM \"BotSuppressions\" WHERE \"PhoneNumber\" = @p0 AND (\"ResumeAt\" IS NULL OR \"ResumeAt\" > now())", phone));
        run.Check("opting back in lifts a Stop Bot suppression (kept as history)", lifted == 1 && stillSuppressed == 0, $"lifted {lifted}, active {stillSuppressed}");

        var before = harness.WhatsApp.Sent.Count;
        await SayAsync(harness, phone, SendKeyword);
        run.Check("after START, \"send money\" launches the Flow", SentSince(harness, before).Any(s => s.Payload.Contains("flow_token")));
    }

    private static async Task SafeSaveAsync(RemittanceHarness harness, TestRun run, int menuFlowId)
    {
        run.Section("Saving a flow");
        var flows = await harness.InScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IBotFlowService>();
            // The designer's request: no connectionId, no isActive.
            var request = JsonSerializer.Deserialize<UpdateBotFlowRequest>(
                JsonSerializer.Serialize(new { name = $"{RemittanceHarness.Prefix}-menu-{harness.Tag}", description = "saved by the designer" }),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            return await service.UpdateAsync(menuFlowId, request);
        });
        run.Check("a save without connectionId keeps the flow on its connection", flows.ConnectionId == harness.ConnectionId, flows.ConnectionId?.ToString() ?? "global");
        run.Check("a save without isActive keeps the active switch", flows.IsActive);

        var global = await harness.InScopeAsync(sp => sp.GetRequiredService<IBotFlowService>().UpdateAsync(menuFlowId,
            JsonSerializer.Deserialize<UpdateBotFlowRequest>("{\"name\":\"" + $"{RemittanceHarness.Prefix}-menu-{harness.Tag}" + "\",\"connectionId\":null}",
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!));
        run.Check("an explicit connectionId: null makes it global", global.ConnectionId is null);
        await harness.ExecAsync("UPDATE \"BotFlows\" SET \"ConnectionId\" = @p0 WHERE \"Id\" = @p1", harness.ConnectionId, menuFlowId);

        var refused = false;
        try
        {
            await harness.InScopeAsync(sp => sp.GetRequiredService<IBotFlowService>().UpdateAsync(menuFlowId, new UpdateBotFlowRequest
            {
                Name = $"{RemittanceHarness.Prefix}-menu-{harness.Tag}",
                FlowData = Canvas([Node("t1", "Start Trigger", new { keywords = Array.Empty<string>() })], [])
            }));
        }
        catch (ArgumentException)
        {
            refused = true;
        }
        var nodes = Convert.ToInt32(await harness.ScalarAsync("SELECT count(*) FROM \"FlowNodes\" WHERE \"FlowId\" = @p0", menuFlowId));
        run.Check("an active flow with no trigger keywords is refused", refused);
        run.Check("…and the refused save left its steps in place", nodes == 4, nodes.ToString());

        // Triggers and steps are cached on the message path; a save must take effect at once.
        var phone = await NewBotContactAsync(harness);
        await SayAsync(harness, phone, MenuKeyword); // warms the cache with the old keyword
        await harness.InScopeAsync(sp => sp.GetRequiredService<AppDbContext>().ConversationStates
            .Where(s => s.PhoneNumber == phone && s.Status == "Active").ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, "Completed")));
        await harness.InScopeAsync(sp => sp.GetRequiredService<IBotFlowService>().UpdateAsync(menuFlowId, new UpdateBotFlowRequest
        {
            Name = $"{RemittanceHarness.Prefix}-menu-{harness.Tag}",
            FlowData = Canvas(
                [
                    Node("t1", "Start Trigger", new { keywords = new[] { "zzhello" }, triggerType = "on exact match" }),
                    Node("b1", "Button Message", new { messageText = "zz choose", buttons = new[] { new { text = "Option A", value = "a" } } }),
                    Node("a1", "Text Message", new { messageText = "zz picked A" })
                ],
                [Edge("t1", "b1"), Edge("b1", "a1", "button-0")])
        }));
        var before = harness.WhatsApp.Sent.Count;
        await SayAsync(harness, phone, "zzhello");
        run.Check("a keyword saved a moment ago works at once (the cache is cleared by the save)",
            SentSince(harness, before).Any(s => s.Payload.Contains("zz choose")));
        before = harness.WhatsApp.Sent.Count;
        await harness.InScopeAsync(sp => sp.GetRequiredService<AppDbContext>().ConversationStates
            .Where(s => s.PhoneNumber == phone && s.Status == "Active").ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, "Completed")));
        await SayAsync(harness, phone, MenuKeyword);
        run.Check("…and the old keyword no longer does", !SentSince(harness, before).Any(s => s.Payload.Contains("zz choose")));
    }

    private static async Task OrderedQueueAsync(RemittanceHarness harness, TestRun run)
    {
        run.Section("One customer's messages, one at a time, in order");
        const string queue = "zz-ordered-test";
        var queueService = harness.Get<IJobQueue>();
        await queueService.EnqueueAsync(new[]
        {
            new QueueMessage { QueueName = queue, Payload = "{\"n\":\"a1\"}", PartitionKey = "cust-a" },
            new QueueMessage { QueueName = queue, Payload = "{\"n\":\"a2\"}", PartitionKey = "cust-a" },
            new QueueMessage { QueueName = queue, Payload = "{\"n\":\"b1\"}", PartitionKey = "cust-b" }
        });

        QueueClaimRequest Claim(string worker) => new() { QueueName = queue, WorkerId = worker, BatchSize = 10, OrderedPerPartition = true };

        var first = await queueService.ClaimAsync(Claim("zzrt-w1"));
        var firstNames = first.Select(l => JsonDocument.Parse(l.Payload).RootElement.GetProperty("n").GetString()).OrderBy(n => n).ToList();
        run.Check("the first claim takes the oldest job of each customer only", firstNames.SequenceEqual(["a1", "b1"]), string.Join(",", firstNames));

        var whileBusy = await queueService.ClaimAsync(Claim("zzrt-w2"));
        run.Check("another worker cannot take a2 while a1 is running", whileBusy.Count == 0, whileBusy.Count.ToString());

        foreach (var lease in first) await queueService.CompleteAsync(lease);
        var next = await queueService.ClaimAsync(Claim("zzrt-w2"));
        run.Check("a2 becomes available once a1 is done", next.Count == 1 && next[0].Payload.Contains("a2"), string.Join(",", next.Select(l => l.Payload)));
        foreach (var lease in next) await queueService.CompleteAsync(lease);
    }

    private static void PartitionKeys(TestRun run)
    {
        run.Section("Inbound ordering key");
        const string message = "{\"entry\":[{\"changes\":[{\"value\":{\"metadata\":{\"phone_number_id\":\"123\"},\"messages\":[{\"from\":\"60123\",\"id\":\"wamid.x\"}]}}]}]}";
        const string status = "{\"entry\":[{\"changes\":[{\"value\":{\"metadata\":{\"phone_number_id\":\"123\"},\"statuses\":[{\"recipient_id\":\"60123\",\"id\":\"wamid.y\"}]}}]}]}";
        run.Check("a customer message is keyed by number and customer", WhatsAppWebhookPartition.For(message) == "wa:123:60123", WhatsAppWebhookPartition.For(message));
        run.Check("a delivery status gets no key, so it never holds up the customer's messages", WhatsAppWebhookPartition.For(status) is null);
    }
}
