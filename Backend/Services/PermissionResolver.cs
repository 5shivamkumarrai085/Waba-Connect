using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Data.Seed;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services;

/// <inheritdoc />
public class PermissionResolver : IPermissionResolver
{
    private readonly AppDbContext _dbContext;
    private readonly IMemoryCache _cache;

    /// <summary>
    /// Short deliberately. Permissions are looked up on every guarded request, so the cache is
    /// about avoiding a query per request rather than long-term storage. Saves evict explicitly;
    /// this TTL is the backstop for anything that changes the graph without going through a
    /// service (a manual SQL edit, say).
    /// </summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private const string CacheKeyPrefix = "perm:user:";

    public PermissionResolver(AppDbContext dbContext, IMemoryCache cache)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    public async Task<HashSet<string>> GetEffectivePermissionsAsync(int userId)
    {
        var cacheKey = CacheKeyPrefix + userId;

        if (_cache.TryGetValue(cacheKey, out HashSet<string>? cached) && cached is not null)
            return cached;

        var user = await _dbContext.Set<Models.Entities.AppUser>()
            .AsNoTracking()
            .Where(u => u.Id == userId && !u.IsDeleted)
            .Select(u => new
            {
                u.Id,
                u.IsActive,
                u.IsAdministrator,
                u.UsesCustomPermissions,
                u.RoleId,
                RoleIsAdministrator = u.Role != null && u.Role.IsAdministrator
            })
            .FirstOrDefaultAsync();

        HashSet<string> permissions;

        if (user is null || !user.IsActive)
        {
            // Deleted or deactivated mid-session: grant nothing rather than falling through to
            // a role lookup. The token may still be valid but the account is not.
            permissions = new HashSet<string>();
        }
        else if (user.IsAdministrator || user.RoleIsAdministrator)
        {
            // Administrators are resolved to the full catalogue only for display purposes
            // (/auth/me sends an empty list and a flag). Enforcement short-circuits before
            // ever reaching here, so this is never on the hot path.
            permissions = PermissionCatalog.AllKeys().ToHashSet();
        }
        else if (user.UsesCustomPermissions)
        {
            // Per-user rows REPLACE the role's grants — they are not additive. Unchecking a box
            // the role granted therefore actually revokes it.
            permissions = await _dbContext.Set<Models.Entities.UserPermission>()
                .AsNoTracking()
                .Where(up => up.UserId == userId)
                .Select(up => up.Permission.Key)
                .ToHashSetAsync();
        }
        else if (user.RoleId.HasValue)
        {
            permissions = await _dbContext.Set<Models.Entities.RolePermission>()
                .AsNoTracking()
                .Where(rp => rp.RoleId == user.RoleId.Value)
                .Select(rp => rp.Permission.Key)
                .ToHashSetAsync();
        }
        else
        {
            permissions = new HashSet<string>();
        }

        _cache.Set(cacheKey, permissions, CacheTtl);
        return permissions;
    }

    public void InvalidateUser(int userId) => _cache.Remove(CacheKeyPrefix + userId);

    public async Task InvalidateRoleAsync(int roleId)
    {
        var userIds = await _dbContext.Set<Models.Entities.AppUser>()
            .AsNoTracking()
            .Where(u => u.RoleId == roleId)
            .Select(u => u.Id)
            .ToListAsync();

        foreach (var id in userIds)
            InvalidateUser(id);
    }

    public void InvalidateAll()
    {
        // IMemoryCache has no bulk clear and the instance is shared with the dashboard cache,
        // so enumerate our own keys rather than compacting the whole cache out from under it.
        var userIds = _dbContext.Set<Models.Entities.AppUser>()
            .AsNoTracking()
            .Select(u => u.Id)
            .ToList();

        foreach (var id in userIds)
            InvalidateUser(id);
    }
}
