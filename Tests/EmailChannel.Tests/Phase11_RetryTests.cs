using Microsoft.Extensions.DependencyInjection;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Services.WhatsApp;

namespace EmailChannel.Tests;

/// <summary>
/// Phase 11 — retry failed recipients. Only failed sends are retried (never delivered, bounced or
/// suppressed recipients), the counters are recomputed, the retry is dispatched once under a new
/// run, and the number of retries is capped.
/// </summary>
public static class Phase11_RetryTests
{
    public static async Task RunAsync(TestHarness harness, TestRun run)
    {
        run.BeginPhase("Phase 11 — retry failed recipients");

        int campaignId = 0, templateId = 0, connectionId = 0;
        var contactIds = new List<int>();
        var recipient = new Dictionary<string, int>();
        var dispatched = new List<int>();

        await harness.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            var connection = new Connection { Name = $"{TestHarness.Prefix} retry conn {harness.Tag}", IsActive = true };
            var template = new Template
            {
                Name = $"zz_emailtest_retry_{harness.Tag}",
                Status = TemplateStatus.Approved,
                BodyText = "Hello",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Connections.Add(connection);
            db.Templates.Add(template);

            var contacts = new[] { "sent", "failed-1", "failed-2", "suppressed" }.Select(label => new Contact
            {
                Name = $"{TestHarness.Prefix} Retry {label} {harness.Tag}",
                Phone = $"+9196{Random.Shared.Next(10000000, 99999999)}",
                Type = "Lead",
                Status = "New",
                Source = "Import",
                IsActive = true
            }).ToArray();
            db.Contacts.AddRange(contacts);
            await db.SaveChangesAsync();

            var campaign = new Campaign
            {
                Name = $"{TestHarness.Prefix} retry campaign {harness.Tag}",
                Channel = MessageChannel.WhatsApp,
                TemplateId = template.Id,
                ConnectionId = connection.Id,
                RelationType = "Lead",
                ScheduleType = ScheduleType.Immediate,
                Status = CampaignStatus.PartiallyFailed,
                TotalRecipients = 4,
                SentCount = 1,
                FailedCount = 2
            };
            var statuses = new[] { MessageStatus.Sent, MessageStatus.Failed, MessageStatus.Failed, MessageStatus.Suppressed };
            for (var i = 0; i < contacts.Length; i++)
            {
                campaign.CampaignContacts.Add(new CampaignContact
                {
                    ContactId = contacts[i].Id,
                    Status = statuses[i],
                    SendAttemptedAt = DateTime.UtcNow.AddMinutes(-5),
                    ErrorMessage = statuses[i] == MessageStatus.Failed ? "(#131026) Message undeliverable" : null,
                    SentAt = statuses[i] == MessageStatus.Sent ? DateTime.UtcNow.AddMinutes(-5) : null
                });
            }
            db.Campaigns.Add(campaign);
            await db.SaveChangesAsync();

            campaignId = campaign.Id;
            templateId = template.Id;
            connectionId = connection.Id;
            contactIds.AddRange(contacts.Select(c => c.Id));
            var labels = new[] { "sent", "failed-1", "failed-2", "suppressed" };
            var rows = campaign.CampaignContacts.OrderBy(cc => cc.Id).ToList();
            for (var i = 0; i < labels.Length; i++) recipient[labels[i]] = rows[i].Id;
        });

        async Task<T> WithServiceAsync<T>(Func<ICampaignService, Task<T>> action)
        {
            T result = default!;
            await harness.InScopeAsync(async services =>
            {
                var campaigns = ActivatorUtilities.CreateInstance<CampaignService>(
                    services, new RecordingDispatcher(dispatched));
                result = await action(campaigns);
            });
            return result;
        }

        async Task<string?> StatusOf(string label) => await harness.ScalarAsync(
            """SELECT "Status" FROM "CampaignContacts" WHERE "Id" = @id""", ("id", recipient[label]));

        async Task<string?> Counter(string column) => await harness.ScalarAsync(
            $"""SELECT "{column}" FROM "Campaigns" WHERE "Id" = @id""", ("id", campaignId));

        try
        {
            run.Section("Only failed recipients are retried");
            var result = await WithServiceAsync(c => c.RetryFailedAsync(campaignId));

            run.Check("both failed recipients were queued", result.RecipientCount == 2, $"{result.RecipientCount}");
            run.Check("a failed recipient is back to Pending", await StatusOf("failed-1") == "Pending", await StatusOf("failed-1"));
            run.Check("its previous error is cleared",
                await harness.ScalarAsync("""SELECT "ErrorMessage" FROM "CampaignContacts" WHERE "Id" = @id""", ("id", recipient["failed-1"])) is null);
            run.Check("its send claim is released so it can be sent again",
                await harness.ScalarAsync("""SELECT "SendAttemptedAt" FROM "CampaignContacts" WHERE "Id" = @id""", ("id", recipient["failed-2"])) is null);
            run.Check("a sent recipient is left alone", await StatusOf("sent") == "Sent", await StatusOf("sent"));
            run.Check("a suppressed recipient is never retried", await StatusOf("suppressed") == "Suppressed", await StatusOf("suppressed"));

            run.Section("Counters and dispatch");
            run.Check("the Failed figure drops to zero", await Counter("FailedCount") == "0", await Counter("FailedCount"));
            run.Check("the Sent figure is unchanged", await Counter("SentCount") == "1", await Counter("SentCount"));
            run.Check("the campaign is sending again", await Counter("Status") == "Sending", await Counter("Status"));
            run.Check("the retry was dispatched once", dispatched.Count(id => id == campaignId) == 1, $"{dispatched.Count}");
            run.Check("the retry is recorded",
                await harness.CountAsync("""SELECT count(*) FROM "CampaignRetryRuns" WHERE "CampaignId" = @id""", ("id", campaignId)) == 1);

            run.Section("A running campaign cannot be retried");
            Exception? whileSending = null;
            try { await WithServiceAsync(c => c.RetryFailedAsync(campaignId)); }
            catch (Exception ex) { whileSending = ex; }
            run.Check("retry is refused while the campaign is sending", whileSending is InvalidOperationException, whileSending?.GetType().Name);

            run.Section("Retries are capped");
            await harness.ExecAsync("""UPDATE "Campaigns" SET "Status" = 'PartiallyFailed' WHERE "Id" = @id""", ("id", campaignId));
            await harness.ExecAsync("""UPDATE "CampaignContacts" SET "Status" = 'Failed' WHERE "Id" = @id""", ("id", recipient["failed-1"]));
            await harness.ExecAsync("""
                INSERT INTO "CampaignRetryRuns" ("CampaignId", "RunId", "RecipientCount", "RequestedAt")
                VALUES (@id, 'x1', 1, now()), (@id, 'x2', 1, now())
                """, ("id", campaignId));

            Exception? overLimit = null;
            try { await WithServiceAsync(c => c.RetryFailedAsync(campaignId)); }
            catch (Exception ex) { overLimit = ex; }
            run.Check("a fourth retry is refused (limit 3)", overLimit is InvalidOperationException, overLimit?.GetType().Name);
            run.Check("and nothing was reset", await StatusOf("failed-1") == "Failed", await StatusOf("failed-1"));
        }
        finally
        {
            await harness.ExecAsync("""DELETE FROM "CampaignRetryRuns" WHERE "CampaignId" = @id""", ("id", campaignId));
            await harness.ExecAsync("""DELETE FROM "CampaignContacts" WHERE "CampaignId" = @id""", ("id", campaignId));
            await harness.ExecAsync("""DELETE FROM "Campaigns" WHERE "Id" = @id""", ("id", campaignId));
            await harness.ExecAsync("""DELETE FROM "Templates" WHERE "Id" = @id""", ("id", templateId));
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
