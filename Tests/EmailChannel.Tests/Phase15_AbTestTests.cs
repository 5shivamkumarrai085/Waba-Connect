using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Campaigns;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services;
using WhatsAppCampaignApi.Services.Campaigns;
using WhatsAppCampaignApi.Services.Email;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Services.WhatsApp;

namespace EmailChannel.Tests;

/// <summary>
/// Phase 15 — A/B testing: the deterministic split, the held-back group, deciding by the metric,
/// and releasing the winner to the held recipients exactly once.
/// </summary>
public static class Phase15_AbTestTests
{
    public static async Task RunAsync(TestHarness harness, TestRun run)
    {
        run.BeginPhase("Phase 15 — A/B testing");

        int templateA = 0, templateB = 0, connectionId = 0, campaignId = 0;
        var contactIds = new List<int>();
        var dispatched = new List<int>();

        await harness.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            Template T(string suffix) => new() { Name = $"zz_emailtest_ab_{suffix}_{harness.Tag}", Status = TemplateStatus.Approved, BodyText = "Hi", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            var a = T("a");
            var b = T("b");
            var connection = new Connection { Name = $"{TestHarness.Prefix} ab conn {harness.Tag}", IsActive = true };
            db.Templates.AddRange(a, b);
            db.Connections.Add(connection);
            var contacts = Enumerable.Range(1, 60).Select(i => new Contact
            {
                Name = $"{TestHarness.Prefix} AB {i} {harness.Tag}",
                Phone = $"+9193{Random.Shared.Next(10000000, 99999999)}",
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

        ICampaignService Build(IServiceProvider services)
        {
            var db = services.GetRequiredService<AppDbContext>();
            var dispatcher = new RecordingDispatcher(dispatched);
            var ab = new AbTestService(db, services.GetRequiredService<IEmailCampaignDispatcher>(), dispatcher,
                services.GetRequiredService<IAuditService>(), NullLogger<AbTestService>.Instance);
            return ActivatorUtilities.CreateInstance<CampaignService>(services, dispatcher, ab);
        }

        try
        {
            run.Section("The split");
            CampaignResponse created = null!;
            await harness.InScopeAsync(async services =>
            {
                created = await Build(services).CreateAsync(new CreateCampaignRequest
                {
                    Name = $"{TestHarness.Prefix} ab {harness.Tag}",
                    Channel = "WhatsApp",
                    TemplateId = templateA,
                    ConnectionId = connectionId,
                    RelationType = "Lead",
                    ScheduleType = "Immediate",
                    ContactIds = contactIds,
                    AbTest = new AbTestRequest { Percent = 50, Metric = "read", DecideAfterHours = 2, Variants = [new AbVariantRequest { TemplateId = templateB }] }
                });
            });
            campaignId = created.Id;

            var held = await harness.CountAsync("""SELECT count(*) FROM "CampaignContacts" WHERE "CampaignId" = @id AND "HeldForWinner" """, ("id", campaignId));
            var inA = await harness.CountAsync("""SELECT count(*) FROM "CampaignContacts" cc JOIN "CampaignVariants" v ON v."Id" = cc."VariantId" WHERE cc."CampaignId" = @id AND v."Label" = 'A'""", ("id", campaignId));
            var inB = await harness.CountAsync("""SELECT count(*) FROM "CampaignContacts" cc JOIN "CampaignVariants" v ON v."Id" = cc."VariantId" WHERE cc."CampaignId" = @id AND v."Label" = 'B'""", ("id", campaignId));
            run.Check("every recipient is either in the test or held", held + inA + inB == 60, $"held {held}, A {inA}, B {inB}");
            run.Check("roughly half are held (20–40 of 60)", held is >= 20 and <= 40, $"{held}");
            run.Check("both variants get recipients", inA > 0 && inB > 0, $"A {inA}, B {inB}");
            run.Check("the test was dispatched", dispatched.Count(x => x == campaignId) == 1, $"{dispatched.Count}");

            run.Section("Deciding by read rate");
            // B's recipients read the message; A's did not.
            await harness.ExecAsync("""
                UPDATE "CampaignContacts" cc SET "Status" = 'Read', "SentAt" = now(), "ReadAt" = now()
                  FROM "CampaignVariants" v
                 WHERE v."Id" = cc."VariantId" AND cc."CampaignId" = @id AND v."Label" = 'B'
                """, ("id", campaignId));
            await harness.ExecAsync("""
                UPDATE "CampaignContacts" cc SET "Status" = 'Delivered', "SentAt" = now()
                  FROM "CampaignVariants" v
                 WHERE v."Id" = cc."VariantId" AND cc."CampaignId" = @id AND v."Label" = 'A'
                """, ("id", campaignId));

            int? winner = null;
            await harness.InScopeAsync(async services => winner = await Build(services).DecideAbTestAsync(campaignId, null));
            var winnerLabel = await harness.ScalarAsync("""SELECT "Label" FROM "CampaignVariants" WHERE "Id" = @id""", ("id", winner ?? 0));
            run.Check("the variant with the higher read rate wins", winnerLabel == "B", winnerLabel);

            var stillHeld = await harness.CountAsync("""SELECT count(*) FROM "CampaignContacts" WHERE "CampaignId" = @id AND "HeldForWinner" """, ("id", campaignId));
            var releasedToB = await harness.CountAsync("""SELECT count(*) FROM "CampaignContacts" WHERE "CampaignId" = @id AND "VariantId" = @v AND "Status" = 'Pending'""", ("id", campaignId), ("v", winner ?? 0));
            run.Check("nobody is held any more", stillHeld == 0, $"{stillHeld}");
            run.Check("the held recipients were given the winner", releasedToB == held, $"{releasedToB} of {held}");
            run.Check("the winner was dispatched to them", dispatched.Count(x => x == campaignId) == 2, $"{dispatched.Count(x => x == campaignId)}");

            Exception? again = null;
            try { await harness.InScopeAsync(async services => await Build(services).DecideAbTestAsync(campaignId, null)); } catch (Exception ex) { again = ex; }
            run.Check("a decided test cannot be decided again", again is InvalidOperationException, again?.GetType().Name);

            run.Section("Validation");
            Exception? badMetric = null;
            try
            {
                await harness.InScopeAsync(async services => await Build(services).CreateAsync(new CreateCampaignRequest
                {
                    Name = $"{TestHarness.Prefix} ab bad {harness.Tag}",
                    Channel = "WhatsApp",
                    TemplateId = templateA,
                    ConnectionId = connectionId,
                    RelationType = "Lead",
                    ScheduleType = "Immediate",
                    ContactIds = contactIds.Take(5).ToList(),
                    AbTest = new AbTestRequest { Percent = 50, Metric = "open", Variants = [new AbVariantRequest { TemplateId = templateB }] }
                }));
            }
            catch (Exception ex) { badMetric = ex; }
            run.Check("a WhatsApp test cannot be decided by email opens", badMetric is ArgumentException, badMetric?.GetType().Name);
        }
        finally
        {
            await harness.ExecAsync("""DELETE FROM "CampaignContacts" WHERE "CampaignId" IN (SELECT "Id" FROM "Campaigns" WHERE "Name" LIKE @p)""", ("p", $"{TestHarness.Prefix} ab%"));
            await harness.ExecAsync("""DELETE FROM "Campaigns" WHERE "Name" LIKE @p""", ("p", $"{TestHarness.Prefix} ab%"));
            await harness.ExecAsync("""DELETE FROM "Templates" WHERE "Id" IN (@a, @b)""", ("a", templateA), ("b", templateB));
            await harness.ExecAsync("""DELETE FROM "Connections" WHERE "Id" = @id""", ("id", connectionId));
        }
    }

    private sealed class RecordingDispatcher(List<int> calls) : IWhatsAppCampaignDispatcher
    {
        public Task StartAsync(int campaignId, DateTime? notBefore, CancellationToken ct = default)
        {
            lock (calls) calls.Add(campaignId);
            return Task.CompletedTask;
        }
    }
}
