using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WhatsAppCampaignApi.Data;

namespace WhatsAppCampaignApi.Services;

/// <summary>
/// The current security stamp and active state of each user, as the token validator sees it.
/// </summary>
/// <remarks>
/// Every authenticated request compares its token's stamp with this. The value is cached briefly
/// so that check costs a database read at most once per user per <see cref="Ttl"/>; changes made
/// through this process invalidate the entry at once, and other instances converge within the TTL.
/// </remarks>
public interface ISecurityStampCache
{
    Task<(bool Active, string? Stamp)> GetAsync(int userId, CancellationToken ct);
    void Invalidate(int userId);
}

public sealed class SecurityStampCache : ISecurityStampCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private readonly IMemoryCache _cache;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public SecurityStampCache(IMemoryCache cache, IDbContextFactory<AppDbContext> contextFactory)
    {
        _cache = cache;
        _contextFactory = contextFactory;
    }

    public async Task<(bool Active, string? Stamp)> GetAsync(int userId, CancellationToken ct)
    {
        var key = CacheKey(userId);
        if (_cache.TryGetValue(key, out (bool Active, string? Stamp) cached)) return cached;

        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var row = await db.AppUsers
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.IsActive, u.IsDeleted, u.SecurityStamp })
            .FirstOrDefaultAsync(ct);

        var value = row is null ? (false, (string?)null) : (row.IsActive && !row.IsDeleted, row.SecurityStamp);
        _cache.Set(key, value, Ttl);
        return value;
    }

    public void Invalidate(int userId) => _cache.Remove(CacheKey(userId));

    private static string CacheKey(int userId) => $"auth-stamp:{userId}";
}
