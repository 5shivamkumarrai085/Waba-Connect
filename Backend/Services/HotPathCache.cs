using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace WhatsAppCampaignApi.Services;

/// <summary>
/// Short-lived cache for read-mostly configuration on the per-message path: which bot flows and
/// bots are active, a connection's send credentials, the Send Money catalogue. Every inbound
/// message used to read all of it from the database, one round trip at a time — about twenty
/// queries per message that, at ~280 ms each from a distant server, made the reply take seconds.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Entries live 60 seconds, so a change made on another instance (several share one
/// database) is picked up within a minute.</item>
/// <item>A save on this instance calls <see cref="Invalidate"/> for its region, which takes effect
/// at once: entries are keyed by a per-region generation, and bumping it orphans them all.</item>
/// <item>Cached values must be detached (AsNoTracking) and treated as read-only by callers.</item>
/// </list>
/// </remarks>
public sealed class HotPathCache
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(60);

    /// <summary>Active bot flows, their triggers, nodes and edges.</summary>
    public const string BotFlows = "botflows";

    /// <summary>Active Message Bots and Template Bots.</summary>
    public const string Bots = "bots";

    /// <summary>Connections' send credentials and phone numbers.</summary>
    public const string Connections = "connections";

    /// <summary>Send Money catalogue (corridors, methods, lookups, payment methods) and Flow registrations.</summary>
    public const string SendMoney = "sendmoney";

    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<string, long> _generations = new();

    public HotPathCache(IMemoryCache cache) => _cache = cache;

    /// <summary>The cached value for <paramref name="key"/> in <paramref name="region"/>, loading it on a miss.</summary>
    public async Task<T> GetOrLoadAsync<T>(string region, string key, Func<Task<T>> load, TimeSpan? ttl = null)
    {
        var cacheKey = $"hot:{region}:{_generations.GetOrAdd(region, 0)}:{key}";
        if (_cache.TryGetValue(cacheKey, out T? cached)) return cached!;

        var loaded = await load();
        _cache.Set(cacheKey, loaded, ttl ?? DefaultTtl);
        return loaded;
    }

    /// <summary>Drops everything cached in the region on this instance (call after saving what it holds).</summary>
    public void Invalidate(string region) => _generations.AddOrUpdate(region, 1, (_, generation) => generation + 1);
}
