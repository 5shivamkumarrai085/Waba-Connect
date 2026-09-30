using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Services.Security;

namespace EmailChannel.Tests;

/// <summary>
/// Phase 9 — connection scoping. Who may see which connection: deny by default, granted directly
/// or through the user's role, administrators and a switched-off flag unrestricted.
/// </summary>
public static class Phase9_ConnectionScopingTests
{
    public static async Task RunAsync(TestHarness harness, TestRun run)
    {
        run.BeginPhase("Phase 9 — connection scoping");

        int roleId = 0, userId = 0, directConnection = 0, roleConnection = 0, otherConnection = 0;

        await harness.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            var role = new Role { Name = $"{TestHarness.Prefix} scope role {harness.Tag}" };
            db.Roles.Add(role);

            var connections = Enumerable.Range(1, 3)
                .Select(i => new Connection { Name = $"{TestHarness.Prefix} scope conn {i} {harness.Tag}", IsActive = true })
                .ToList();
            db.Connections.AddRange(connections);
            await db.SaveChangesAsync();

            var user = new AppUser
            {
                FirstName = TestHarness.Prefix,
                LastName = "Scoped",
                Email = $"scoped-{harness.Tag}@example.test",
                PasswordHash = "not-a-real-hash",
                IsActive = true,
                RoleId = role.Id
            };
            db.AppUsers.Add(user);
            await db.SaveChangesAsync();

            roleId = role.Id;
            userId = user.Id;
            directConnection = connections[0].Id;
            roleConnection = connections[1].Id;
            otherConnection = connections[2].Id;
        });

        try
        {
            async Task<IReadOnlySet<int>?> ScopeAsync(bool enforced, bool isAdmin, IMemoryCache? cache = null)
            {
                IReadOnlySet<int>? result = null;
                await harness.InScopeAsync(async services =>
                {
                    var config = new ConfigurationBuilder()
                        .AddInMemoryCollection(new Dictionary<string, string?> { ["Security:EnforceConnectionScoping"] = enforced.ToString() })
                        .Build();
                    var scope = new AccessScope(
                        services.GetRequiredService<AppDbContext>(),
                        new FixedUser(userId, isAdmin),
                        cache ?? new MemoryCache(new MemoryCacheOptions()),
                        config);
                    result = await scope.GetAllowedConnectionIdsAsync();
                });
                return result;
            }

            run.Section("Deny by default");
            var none = await ScopeAsync(enforced: true, isAdmin: false);
            run.Check("a user with no assignment sees no connection", none is { Count: 0 }, $"{none?.Count}");

            run.Section("Direct and role grants");
            await harness.InScopeAsync(async services =>
            {
                var db = services.GetRequiredService<AppDbContext>();
                db.UserConnections.Add(new UserConnection { AppUserId = userId, UserId = userId.ToString(), ConnectionId = directConnection, IsActive = true });
                db.DepartmentConnections.Add(new DepartmentConnection { RoleId = roleId, DepartmentId = roleId.ToString(), ConnectionId = roleConnection, IsActive = true });
                await db.SaveChangesAsync();
            });

            var granted = await ScopeAsync(enforced: true, isAdmin: false);
            run.Check("a direct assignment grants its connection", granted?.Contains(directConnection) == true);
            run.Check("the user's role grants its connection", granted?.Contains(roleConnection) == true);
            run.Check("an unassigned connection stays hidden", granted?.Contains(otherConnection) == false);

            run.Section("Unrestricted callers");
            run.Check("an administrator is unrestricted", await ScopeAsync(enforced: true, isAdmin: true) is null);
            run.Check("with the switch off, everyone is unrestricted", await ScopeAsync(enforced: false, isAdmin: false) is null);

            run.Section("A disabled assignment stops granting");
            await harness.ExecAsync("""UPDATE "UserConnections" SET "IsActive" = false WHERE "AppUserId" = @u""", ("u", userId));
            var afterDisable = await ScopeAsync(enforced: true, isAdmin: false);
            run.Check("the disabled direct grant is gone", afterDisable?.Contains(directConnection) == false);
            run.Check("the role grant remains", afterDisable?.Contains(roleConnection) == true);

            run.Section("Changes apply after invalidation, not after the cache expires");
            var shared = new MemoryCache(new MemoryCacheOptions());
            var before = await ScopeAsync(enforced: true, isAdmin: false, shared);
            await harness.ExecAsync("""UPDATE "DepartmentConnections" SET "IsActive" = false WHERE "RoleId" = @r""", ("r", roleId));
            var stale = await ScopeAsync(enforced: true, isAdmin: false, shared);
            run.Check("the cached scope is served until invalidated", stale?.Contains(roleConnection) == before?.Contains(roleConnection));

            await harness.InScopeAsync(async services =>
            {
                var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Security:EnforceConnectionScoping"] = "true" }).Build();
                new AccessScope(services.GetRequiredService<AppDbContext>(), new FixedUser(userId, false), shared, config).Invalidate();
                await Task.CompletedTask;
            });
            var fresh = await ScopeAsync(enforced: true, isAdmin: false, shared);
            run.Check("after invalidation the removed grant is gone", fresh is { Count: 0 }, $"{fresh?.Count}");
        }
        finally
        {
            await harness.ExecAsync("""DELETE FROM "UserConnections" WHERE "AppUserId" = @u""", ("u", userId));
            await harness.ExecAsync("""DELETE FROM "DepartmentConnections" WHERE "RoleId" = @r""", ("r", roleId));
            await harness.ExecAsync("""DELETE FROM "AppUsers" WHERE "Id" = @u""", ("u", userId));
            await harness.ExecAsync("""DELETE FROM "Roles" WHERE "Id" = @r""", ("r", roleId));
            await harness.ExecAsync("""DELETE FROM "Connections" WHERE "Name" LIKE @p""", ("p", $"{TestHarness.Prefix} scope conn %"));
        }
    }

    private sealed class FixedUser(int userId, bool isAdmin) : ICurrentUserService
    {
        public int? UserId => userId;
        public string? Email => null;
        public string? UserName => "scoped-test-user";
        public bool IsAdministrator => isAdmin;
        public bool IsAuthenticated => true;
        public string? IpAddress => null;
        public string? UserAgent => null;
        public Task<bool> HasPermissionAsync(string permissionKey) => Task.FromResult(false);
        public Task<bool> HasAnyPermissionAsync(IEnumerable<string> permissionKeys) => Task.FromResult(false);
        public Task<List<string>> GetPermissionsAsync() => Task.FromResult(new List<string>());
    }
}
