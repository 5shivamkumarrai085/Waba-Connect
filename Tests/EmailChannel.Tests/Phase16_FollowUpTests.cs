using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Campaigns;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services;
using WhatsAppCampaignApi.Services.Campaigns;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Services.WhatsApp;

namespace EmailChannel.Tests;

/// <summary>
/// Phase 16 — follow-ups: rules are validated against the channel, a due "did not read" rule
/// becomes a child campaign for exactly the non-readers, a "failed → tag" rule tags contacts, and
/// a rule runs once.
/// </summary>
public static class Phase16_FollowUpTests
{
    public static async Task RunAsync(TestHarness harness, TestRun run)
    {
        run.BeginPhase("Phase 16 — follow-ups");

        int templateA = 0, templateB = 0, connectionId = 0;
        var contactIds = new List<int>();
        var dispatched = new List<int>();
        var tag = $"zzfu{harness.Tag}";

        await harness.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            Template T(string s) => new() { Name = $"zz_emailtest_fu_{s}_{harness.Tag}", Status = TemplateStatus.Approved, BodyText = "Hi", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            var a = T("a");
            var b = T("b");
            var connection = new Connection { Name = $"{TestHarness.Prefix} fu conn {harness.Tag}", IsActive = true };
            db.Templates.AddRange(a, b);
            db.Connections.Add(connection);
            var contacts = Enumerable.Range(1, 5).Select(i => new Contact
            {
                Name = $"{TestHarness.Prefix} FU {i} {harness.Tag}",
                Phone = $"+9192{Random.Shared.Next(10000000, 99999999)}",
                Type = "Lead",
                Status = "New",
                Source = "Import",
                IsActive = true
            }).ToList();
            db.Contacts.AddRange(contacts);
            await db.SaveChangesAsync();
            templateA = a.Id;
            templateB = b.Id;
            connectionId = connection.Id;
            contactIds.AddRange(contacts.Select(c => c.Id));
        });

        ICampaignService Campaigns(IServiceProvider services) =>
            ActivatorUtilities.CreateInstance<CampaignService>(services, new RecordingDispatcher(dispatched));

        FollowUpService FollowUps(IServiceProvider services) =>
            new(services.GetRequiredService<AppDbContext>(), new OneService(Campaigns(services)),
                services.GetRequiredService<IAuditService>(), NullLogger<FollowUpService>.Instance);

        CreateCampaignRequest Request(string suffix, List<FollowUpRequest> followUps) => new()
        {
            Name = $"{TestHarness.Prefix} fu {suffix} {harness.Tag}",
            Channel = "WhatsApp",
            TemplateId = templateA,
            ConnectionId = connectionId,
            RelationType = "Lead",
            ScheduleType = "Immediate",
            ContactIds = contactIds,
            FollowUps = followUps
        };

        try
        {
            run.Section("Rules are validated against the channel");
            Exception? invalid = null;
            try
            {
                await harness.InScopeAsync(async services => await Campaigns(services).CreateAsync(
                    Request("bad", [new FollowUpRequest { Condition = "NotOpened", DelayHours = 24, TemplateId = templateB }])));
            }
            catch (Exception ex) { invalid = ex; }
            run.Check("\"did not open\" is refused for a WhatsApp campaign", invalid is ArgumentException, invalid?.GetType().Name);

            CampaignResponse parent = null!;
            await harness.InScopeAsync(async services => parent = await Campaigns(services).CreateAsync(Request("parent",
            [
                new FollowUpRequest { Condition = "NotRead", DelayHours = 24, Channel = "WhatsApp", TemplateId = templateB },
                new FollowUpRequest { Condition = "Failed", DelayHours = 24, Action = "tag", Tag = tag }
            ])));
            var rules = await harness.CountAsync("""SELECT count(*) FROM "FollowUpRules" WHERE "CampaignId" = @id AND "Status" = 'Pending'""", ("id", parent.Id));
            run.Check("both rules are stored, waiting", rules == 2, $"{rules}");

            run.Section("A due rule targets exactly the matching recipients");
            // Two read, two delivered but unread, one failed.
            await harness.ExecAsync("""
                WITH ranked AS (SELECT "Id", row_number() OVER (ORDER BY "Id") AS n FROM "CampaignContacts" WHERE "CampaignId" = @id)
                UPDATE "CampaignContacts" cc SET
                    "Status"  = CASE WHEN r.n <= 2 THEN 'Read' WHEN r.n <= 4 THEN 'Delivered' ELSE 'Failed' END,
                    "SentAt"  = CASE WHEN r.n <= 4 THEN now() END,
                    "ReadAt"  = CASE WHEN r.n <= 2 THEN now() END
                  FROM ranked r WHERE r."Id" = cc."Id"
                """, ("id", parent.Id));
            await harness.ExecAsync("""UPDATE "Campaigns" SET "Status" = 'PartiallyFailed' WHERE "Id" = @id""", ("id", parent.Id));
            await harness.ExecAsync("""UPDATE "FollowUpRules" SET "DueAt" = now() - interval '1 minute' WHERE "CampaignId" = @id""", ("id", parent.Id));

            var ruleIds = new List<int>();
            await harness.InScopeAsync(async services =>
                ruleIds = await services.GetRequiredService<AppDbContext>().FollowUpRules
                    .Where(r => r.CampaignId == parent.Id).OrderBy(r => r.Id).Select(r => r.Id).ToListAsyncSafe());

            foreach (var id in ruleIds)
                await harness.InScopeAsync(async services => await FollowUps(services).RunAsync(id));

            var childId = await harness.ScalarAsync("""SELECT "ChildCampaignId" FROM "FollowUpRules" WHERE "Id" = @id""", ("id", ruleIds[0]));
            run.Check("the send rule created a child campaign", childId is not null, await harness.ScalarAsync("""SELECT "Note" FROM "FollowUpRules" WHERE "Id" = @id""", ("id", ruleIds[0])));
            if (childId is not null)
            {
                var childRecipients = await harness.CountAsync("""SELECT count(*) FROM "CampaignContacts" WHERE "CampaignId" = @id""", ("id", int.Parse(childId)));
                run.Check("it goes to the two who did not read", childRecipients == 2, $"{childRecipients}");
                var linked = await harness.ScalarAsync("""SELECT "ParentCampaignId" FROM "Campaigns" WHERE "Id" = @id""", ("id", int.Parse(childId)));
                run.Check("it records the campaign it follows up", linked == parent.Id.ToString(), linked);
                var childTemplate = await harness.ScalarAsync("""SELECT "TemplateId" FROM "Campaigns" WHERE "Id" = @id""", ("id", int.Parse(childId)));
                run.Check("it uses the follow-up's template", childTemplate == templateB.ToString(), childTemplate);
            }

            var tagged = await harness.CountAsync("""SELECT count(*) FROM "Contacts" WHERE "Id" = ANY(@ids) AND "Tags" LIKE @t""", ("ids", contactIds.ToArray()), ("t", $"%{tag}%"));
            run.Check("the tag rule tagged the failed recipient only", tagged == 1, $"{tagged}");

            run.Section("A rule runs once");
            var childCountBefore = await harness.CountAsync("""SELECT count(*) FROM "Campaigns" WHERE "ParentCampaignId" = @id""", ("id", parent.Id));
            await harness.InScopeAsync(async services => await FollowUps(services).RunAsync(ruleIds[0]));
            var childCountAfter = await harness.CountAsync("""SELECT count(*) FROM "Campaigns" WHERE "ParentCampaignId" = @id""", ("id", parent.Id));
            run.Check("running it again creates nothing", childCountAfter == childCountBefore, $"{childCountBefore} → {childCountAfter}");
        }
        finally
        {
            await harness.ExecAsync("""DELETE FROM "CampaignContacts" WHERE "CampaignId" IN (SELECT "Id" FROM "Campaigns" WHERE "Name" LIKE @p)""", ("p", $"{TestHarness.Prefix} fu%"));
            await harness.ExecAsync("""DELETE FROM "Campaigns" WHERE "ParentCampaignId" IN (SELECT "Id" FROM "Campaigns" WHERE "Name" LIKE @p)""", ("p", $"{TestHarness.Prefix} fu%"));
            await harness.ExecAsync("""DELETE FROM "CampaignContacts" WHERE "CampaignId" NOT IN (SELECT "Id" FROM "Campaigns")""");
            await harness.ExecAsync("""DELETE FROM "Campaigns" WHERE "Name" LIKE @p""", ("p", $"{TestHarness.Prefix} fu%"));
            await harness.ExecAsync("""DELETE FROM "Templates" WHERE "Id" IN (@a, @b)""", ("a", templateA), ("b", templateB));
            await harness.ExecAsync("""DELETE FROM "Connections" WHERE "Id" = @id""", ("id", connectionId));
        }
    }

    private static Task<List<int>> ToListAsyncSafe(this IQueryable<int> query) =>
        Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(query);

    private sealed class RecordingDispatcher(List<int> calls) : IWhatsAppCampaignDispatcher
    {
        public Task StartAsync(int campaignId, DateTime? notBefore, CancellationToken ct = default)
        {
            lock (calls) calls.Add(campaignId);
            return Task.CompletedTask;
        }
    }

    /// <summary>Hands the follow-up service our campaign service (with the recording dispatcher).</summary>
    private sealed class OneService(ICampaignService campaigns) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(ICampaignService) ? campaigns : null;
    }
}
