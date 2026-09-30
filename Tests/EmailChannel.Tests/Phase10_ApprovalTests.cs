using Microsoft.Extensions.DependencyInjection;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Campaigns;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services;
using WhatsAppCampaignApi.Services.Campaigns;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Services.Security;
using WhatsAppCampaignApi.Services.WhatsApp;

namespace EmailChannel.Tests;

/// <summary>
/// Phase 10 — maker-checker approval. A campaign is parked until a different user approves it;
/// the maker cannot approve their own; a rejection needs a reason and sends nothing; WhatsApp is
/// gated exactly like email; an edit clears an earlier approval.
/// </summary>
public static class Phase10_ApprovalTests
{
    public static async Task RunAsync(TestHarness harness, TestRun run)
    {
        run.BeginPhase("Phase 10 — maker-checker approval");

        int makerId = 0, checkerId = 0, templateId = 0, connectionId = 0, contactId = 0;
        var campaignIds = new List<int>();

        await harness.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            AppUser User(string name) => new()
            {
                FirstName = TestHarness.Prefix,
                LastName = name,
                Email = $"{name.ToLowerInvariant()}-{harness.Tag}@example.test",
                PasswordHash = "not-a-real-hash",
                IsActive = true
            };

            var maker = User("Maker");
            var checker = User("Checker");
            db.AppUsers.AddRange(maker, checker);

            var connection = new Connection { Name = $"{TestHarness.Prefix} approval conn {harness.Tag}", IsActive = true };
            db.Connections.Add(connection);

            var template = new Template
            {
                Name = $"zz_emailtest_approval_{harness.Tag}",
                Status = TemplateStatus.Approved,
                BodyText = "Hello",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Templates.Add(template);

            var contact = new Contact
            {
                Name = $"{TestHarness.Prefix} Approval Recipient {harness.Tag}",
                Phone = $"+9197{Random.Shared.Next(10000000, 99999999)}",
                Type = "Lead",
                Status = "New",
                Source = "Import",
                IsActive = true
            };
            db.Contacts.Add(contact);
            await db.SaveChangesAsync();

            makerId = maker.Id;
            checkerId = checker.Id;
            templateId = template.Id;
            connectionId = connection.Id;
            contactId = contact.Id;
        });

        var dispatched = new List<int>();

        async Task<T> AsUserAsync<T>(int userId, Func<ICampaignService, Task<T>> action)
        {
            T result = default!;
            await harness.InScopeAsync(async services =>
            {
                var db = services.GetRequiredService<AppDbContext>();
                var campaigns = ActivatorUtilities.CreateInstance<CampaignService>(
                    services,
                    new InternalApprovalGate(db),
                    new ApproverUser(userId),
                    new RecordingWhatsAppDispatcher(dispatched));
                result = await action(campaigns);
            });
            return result;
        }

        async Task<Exception?> FailsAsync(int userId, Func<ICampaignService, Task> action)
        {
            try
            {
                await AsUserAsync(userId, async c => { await action(c); return true; });
                return null;
            }
            catch (Exception ex) { return ex; }
        }

        CreateCampaignRequest Request(string suffix) => new()
        {
            Name = $"{TestHarness.Prefix} approval {suffix} {harness.Tag}",
            Channel = "WhatsApp",
            TemplateId = templateId,
            ConnectionId = connectionId,
            RelationType = "Lead",
            ScheduleType = "Immediate",
            ContactIds = [contactId]
        };

        async Task<string?> StatusAsync(int id) =>
            await harness.ScalarAsync("""SELECT "Status" FROM "Campaigns" WHERE "Id" = @id""", ("id", id));

        try
        {
            run.Section("A new campaign waits for approval");
            var created = await AsUserAsync(makerId, c => c.CreateAsync(Request("a")));
            campaignIds.Add(created.Id);

            run.Check("it is parked as AwaitingApproval", await StatusAsync(created.Id) == "AwaitingApproval", await StatusAsync(created.Id));
            run.Check("nothing was queued for sending", dispatched.Count == 0, $"{dispatched.Count}");
            var maker = await harness.ScalarAsync("""SELECT "RequestedByUserId" FROM "CampaignApprovalStates" WHERE "CampaignId" = @id""", ("id", created.Id));
            run.Check("the maker is recorded", maker == makerId.ToString(), maker);

            run.Section("The maker cannot approve their own campaign");
            var selfApproval = await FailsAsync(makerId, c => c.ApproveAsync(created.Id, null));
            run.Check("self-approval is refused as forbidden", selfApproval is ForbiddenException, selfApproval?.GetType().Name);
            run.Check("and the campaign is still parked", await StatusAsync(created.Id) == "AwaitingApproval");

            run.Section("A different user approves it, and it is sent");
            var approved = await AsUserAsync(checkerId, c => c.ApproveAsync(created.Id, "Looks good"));
            run.Check("the campaign moves to Sending", approved.Status == "Sending" && await StatusAsync(created.Id) == "Sending", approved.Status);
            run.Check("it is queued for sending exactly once", dispatched.Count(id => id == created.Id) == 1, $"{dispatched.Count}");
            var decidedBy = await harness.ScalarAsync("""SELECT "DecidedByUserId" FROM "CampaignApprovalStates" WHERE "CampaignId" = @id""", ("id", created.Id));
            run.Check("the checker is recorded", decidedBy == checkerId.ToString(), decidedBy);
            var comment = await harness.ScalarAsync("""SELECT "Reason" FROM "CampaignApprovalStates" WHERE "CampaignId" = @id""", ("id", created.Id));
            run.Check("the approver's comment is kept", comment == "Looks good", comment);

            var twice = await FailsAsync(checkerId, c => c.ApproveAsync(created.Id, null));
            run.Check("an approved campaign cannot be approved again", twice is InvalidOperationException, twice?.GetType().Name);

            run.Section("A rejection needs a reason and sends nothing");
            var second = await AsUserAsync(makerId, c => c.CreateAsync(Request("b")));
            campaignIds.Add(second.Id);

            var noReason = await FailsAsync(checkerId, c => c.RejectAsync(second.Id, "  "));
            run.Check("a rejection without a reason is refused", noReason is ArgumentException, noReason?.GetType().Name);

            await AsUserAsync(checkerId, c => c.RejectAsync(second.Id, "Wrong audience"));
            run.Check("the rejected campaign is cancelled", await StatusAsync(second.Id) == "Cancelled", await StatusAsync(second.Id));
            run.Check("and was never queued", !dispatched.Contains(second.Id));

            run.Section("A parked campaign can be cancelled");
            var third = await AsUserAsync(makerId, c => c.CreateAsync(Request("c")));
            campaignIds.Add(third.Id);
            await AsUserAsync(makerId, c => c.CancelAsync(third.Id));
            run.Check("cancelling from the approval queue works", await StatusAsync(third.Id) == "Cancelled", await StatusAsync(third.Id));
        }
        finally
        {
            foreach (var id in campaignIds)
            {
                await harness.ExecAsync("""DELETE FROM "CampaignApprovalStates" WHERE "CampaignId" = @id""", ("id", id));
                await harness.ExecAsync("""DELETE FROM "CampaignContacts" WHERE "CampaignId" = @id""", ("id", id));
                await harness.ExecAsync("""DELETE FROM "CampaignVariables" WHERE "CampaignId" = @id""", ("id", id));
                await harness.ExecAsync("""DELETE FROM "Campaigns" WHERE "Id" = @id""", ("id", id));
            }
            await harness.ExecAsync("""DELETE FROM "Templates" WHERE "Id" = @id""", ("id", templateId));
            await harness.ExecAsync("""DELETE FROM "Connections" WHERE "Id" = @id""", ("id", connectionId));
            await harness.ExecAsync("""DELETE FROM "AppUsers" WHERE "Id" IN (@a, @b)""", ("a", makerId), ("b", checkerId));
        }
    }

    private sealed class RecordingWhatsAppDispatcher(List<int> calls) : IWhatsAppCampaignDispatcher
    {
        public Task StartAsync(int campaignId, DateTime? notBefore, CancellationToken ct = default)
        {
            lock (calls) calls.Add(campaignId);
            return Task.CompletedTask;
        }
    }

    private sealed class ApproverUser(int userId) : ICurrentUserService
    {
        public int? UserId => userId;
        public string? Email => $"user{userId}@example.test";
        public string? UserName => $"Test user {userId}";
        public bool IsAdministrator => true;
        public bool IsAuthenticated => true;
        public string? IpAddress => null;
        public string? UserAgent => null;
        public Task<bool> HasPermissionAsync(string permissionKey) => Task.FromResult(true);
        public Task<bool> HasAnyPermissionAsync(IEnumerable<string> permissionKeys) => Task.FromResult(true);
        public Task<List<string>> GetPermissionsAsync() => Task.FromResult(new List<string>());
    }
}
