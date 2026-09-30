using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Setup;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Chat;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Services.Security;

namespace EmailChannel.Tests;

/// <summary>
/// Phase 17 — chat operations: the first-response clock, reopening a resolved conversation,
/// round-robin and least-busy routing, and assignment limited to eligible agents.
/// </summary>
public static class Phase17_ChatOperationsTests
{
    public static async Task RunAsync(TestHarness harness, TestRun run)
    {
        run.BeginPhase("Phase 17 — chat routing, status and SLA");

        var agentIds = new List<int>();
        var conversationIds = new List<int>();
        var contactIds = new List<int>();
        int outsiderId = 0;

        await harness.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            AppUser User(string name) => new()
            {
                FirstName = TestHarness.Prefix, LastName = name,
                Email = $"{name.ToLowerInvariant()}-{harness.Tag}@example.test",
                PasswordHash = "not-a-real-hash", IsActive = true
            };
            var a = User("AgentA");
            var b = User("AgentB");
            var outsider = User("Outsider");
            db.AppUsers.AddRange(a, b, outsider);

            var contacts = Enumerable.Range(1, 4).Select(i => new Contact
            {
                Name = $"{TestHarness.Prefix} Chat {i} {harness.Tag}",
                Phone = $"+9191{Random.Shared.Next(10000000, 99999999)}",
                Type = "Lead", Status = "New", Source = "Import", IsActive = true
            }).ToList();
            db.Contacts.AddRange(contacts);
            await db.SaveChangesAsync();

            var conversations = contacts.Select(c => new ChatConversation
            {
                Channel = MessageChannel.WhatsApp, ContactId = c.Id, LastMessageAt = DateTime.UtcNow
            }).ToList();
            db.ChatConversations.AddRange(conversations);
            await db.SaveChangesAsync();

            agentIds.AddRange([a.Id, b.Id]);
            outsiderId = outsider.Id;
            contactIds.AddRange(contacts.Select(c => c.Id));
            conversationIds.AddRange(conversations.Select(c => c.Id));
        });

        async Task WithOps(string strategy, Func<IConversationOperations, Task> body) =>
            await harness.InScopeAsync(async services =>
            {
                var db = services.GetRequiredService<AppDbContext>();
                var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Security:EnforceConnectionScoping"] = "false" }).Build();
                var ops = new ConversationOperations(
                    db,
                    new Settings(strategy, agentIds),
                    new ChatOnlyPermissions(agentIds),
                    new AccessScope(db, services.GetRequiredService<ICurrentUserService>(), new MemoryCache(new MemoryCacheOptions()), config),
                    services.GetRequiredService<ICurrentUserService>(),
                    services.GetRequiredService<WhatsAppCampaignApi.Services.Realtime.IInboxNotifier>(),
                    services.GetRequiredService<IAuditService>());
                await body(ops);
            });

        async Task<string?> Col(int conversationId, string column) =>
            await harness.ScalarAsync($"""SELECT "{column}" FROM "ChatConversations" WHERE "Id" = @id""", ("id", conversationId));

        try
        {
            run.Section("The first-response clock");
            await WithOps("off", ops => ops.OnInboundAsync(conversationIds[0]));
            run.Check("an inbound message sets a first-response deadline", await Col(conversationIds[0], "FirstResponseDueAt") is not null);
            run.Check("and marks the conversation Open", await Col(conversationIds[0], "Status") == "Open");
            run.Check("with routing off nobody is assigned", await Col(conversationIds[0], "AssignedUserId") is null);

            var due = await Col(conversationIds[0], "FirstResponseDueAt");
            await WithOps("off", ops => ops.OnInboundAsync(conversationIds[0]));
            run.Check("a second message does not push the deadline back", await Col(conversationIds[0], "FirstResponseDueAt") == due);

            await WithOps("off", ops => ops.OnAgentReplyAsync(conversationIds[0]));
            run.Check("an agent reply stops the clock", await Col(conversationIds[0], "FirstRespondedAt") is not null);
            run.Check("and the conversation waits on the customer", await Col(conversationIds[0], "Status") == "Pending");

            run.Section("Resolve and reopen");
            await WithOps("off", ops => ops.SetStatusAsync(conversationIds[0], ConversationStatus.Resolved));
            run.Check("resolving records when", await Col(conversationIds[0], "ResolvedAt") is not null);
            await WithOps("off", ops => ops.OnInboundAsync(conversationIds[0]));
            run.Check("a new message reopens it", await Col(conversationIds[0], "Status") == "Open");
            run.Check("with a fresh first-response clock", await Col(conversationIds[0], "FirstRespondedAt") is null && await Col(conversationIds[0], "FirstResponseDueAt") is not null);

            run.Section("Routing");
            await WithOps("roundRobin", ops => ops.OnInboundAsync(conversationIds[1]));
            await WithOps("roundRobin", ops => ops.OnInboundAsync(conversationIds[2]));
            var first = await Col(conversationIds[1], "AssignedUserId");
            var second = await Col(conversationIds[2], "AssignedUserId");
            run.Check("round robin assigns someone", first is not null && second is not null);
            run.Check("and alternates between agents", first != second, $"{first} / {second}");

            // Give the first agent a second open conversation, so the other is clearly less busy.
            await harness.ExecAsync("""UPDATE "ChatConversations" SET "AssignedUserId" = @a, "Status" = 'Open' WHERE "Id" = @id""", ("a", int.Parse(first!)), ("id", conversationIds[0]));
            await WithOps("leastBusy", ops => ops.OnInboundAsync(conversationIds[3]));
            run.Check("least-busy picks the agent with fewer open conversations", await Col(conversationIds[3], "AssignedUserId") == second);

            run.Section("Assignment is limited to eligible agents");
            Exception? refused = null;
            try { await WithOps("off", ops => ops.AssignAsync(conversationIds[3], outsiderId)); } catch (Exception ex) { refused = ex; }
            run.Check("a user outside the rotation cannot be assigned", refused is ArgumentException, refused?.GetType().Name);
            await WithOps("off", ops => ops.AssignAsync(conversationIds[3], null));
            run.Check("a conversation can be unassigned", await Col(conversationIds[3], "AssignedUserId") is null);
        }
        finally
        {
            foreach (var id in conversationIds) await harness.ExecAsync("""DELETE FROM "ChatConversations" WHERE "Id" = @id""", ("id", id));
            await harness.ExecAsync("""DELETE FROM "AppUsers" WHERE "Id" = ANY(@ids)""", ("ids", agentIds.Append(outsiderId).ToArray()));
        }
    }

    private sealed class Settings(string strategy, List<int> agents) : IOmniSettingsService
    {
        public Task<string?> GetValueAsync(string key) => Task.FromResult<string?>(key == ConversationOperations.StrategyKey ? strategy : null);
        public Task<bool> GetFlagAsync(string key, bool fallback = false) => Task.FromResult(fallback);
        public Task<int> GetNumberAsync(string key, int fallback) => Task.FromResult(key switch
        {
            ConversationOperations.FirstResponseKey => 30,
            ConversationOperations.ResolutionKey => 24,
            _ => fallback
        });
        public Task<IReadOnlyList<string>> GetListAsync(string key) =>
            Task.FromResult<IReadOnlyList<string>>(key == ConversationOperations.AgentsKey ? agents.Select(a => a.ToString()).ToList() : []);
        public Task<OmniSettingsSchemaDto> GetSchemaAsync() => throw new NotSupportedException();
        public Task<OmniSettingsSchemaDto> SaveSectionAsync(string sectionKey, SaveOmniSettingsRequest request) => throw new NotSupportedException();
        public void InvalidateCache() { }
    }

    private sealed class ChatOnlyPermissions(List<int> agents) : IPermissionResolver
    {
        public Task<HashSet<string>> GetEffectivePermissionsAsync(int userId) =>
            Task.FromResult(agents.Contains(userId) ? new HashSet<string> { "Chat.View" } : new HashSet<string>());
        public void InvalidateUser(int userId) { }
        public Task InvalidateRoleAsync(int roleId) => Task.CompletedTask;
        public void InvalidateAll() { }
    }
}
