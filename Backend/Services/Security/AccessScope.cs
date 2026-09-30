using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services.Security;

/// <summary>
/// The caller acted on something their role allows but their connection assignment does not.
/// Mapped to 403 — not 401, which the client reads as an expired session and signs the user out.
/// </summary>
public sealed class ForbiddenException(string message) : Exception(message);

/// <summary>
/// Which messaging connections (WhatsApp accounts, email connections) the caller may see and act on.
/// </summary>
/// <remarks>
/// <para>
/// Permissions say <i>what</i> a user may do (view campaigns, reply in chat); this says <i>where</i>.
/// A user reaches a connection by being assigned to it directly, or through their role (the
/// "department" assignments). Administrators, background workers and — while
/// <c>Security:EnforceConnectionScoping</c> is off — everyone, are unrestricted.
/// </para>
/// <para>
/// Deny by default: a restricted user with no assignment sees nothing. Records with no connection
/// (legacy rows) are visible only to unrestricted callers.
/// </para>
/// </remarks>
public interface IAccessScope
{
    /// <summary>Whether connection scoping is switched on.</summary>
    bool IsEnforced { get; }

    /// <summary>The connections the current caller may use, or null when unrestricted.</summary>
    Task<IReadOnlySet<int>?> GetAllowedConnectionIdsAsync(CancellationToken ct = default);

    /// <summary>The connections a given user may use, or null when unrestricted. For callers with no HTTP context (SignalR).</summary>
    Task<IReadOnlySet<int>?> GetAllowedConnectionIdsForUserAsync(int userId, bool isAdministrator, CancellationToken ct = default);

    /// <summary>Throws <see cref="ForbiddenException"/> unless the caller may use this connection.</summary>
    Task EnsureConnectionAllowedAsync(int? connectionId, CancellationToken ct = default);

    /// <summary>Drops every cached scope; called whenever an assignment changes.</summary>
    void Invalidate();

    /// <summary>True when the current caller sees every connection (scoping off, administrator, or no user).</summary>
    bool IsUnrestricted { get; }

    /// <summary>
    /// The current caller's allowed connection ids as a query, for composing into another query
    /// (translated to a SQL subquery). Only meaningful when <see cref="IsUnrestricted"/> is false.
    /// </summary>
    IQueryable<int> AllowedConnectionIdsQuery();
}

/// <inheritdoc />
public sealed class AccessScope : IAccessScope
{
    private const string VersionKey = "access-scope:version";
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);

    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _configuration;

    private IReadOnlySet<int>? _requestScope;
    private bool _requestScopeResolved;

    public AccessScope(AppDbContext db, ICurrentUserService currentUser, IMemoryCache cache, IConfiguration configuration)
    {
        _db = db;
        _currentUser = currentUser;
        _cache = cache;
        _configuration = configuration;
    }

    public bool IsEnforced => _configuration.GetValue("Security:EnforceConnectionScoping", false);

    public async Task<IReadOnlySet<int>?> GetAllowedConnectionIdsAsync(CancellationToken ct = default)
    {
        if (_requestScopeResolved) return _requestScope;

        // Off-request work (queue workers, schedulers) acts for the system, not for a user.
        _requestScope = !_currentUser.IsAuthenticated || _currentUser.UserId is not { } userId
            ? null
            : await GetAllowedConnectionIdsForUserAsync(userId, _currentUser.IsAdministrator, ct);

        _requestScopeResolved = true;
        return _requestScope;
    }

    public async Task<IReadOnlySet<int>?> GetAllowedConnectionIdsForUserAsync(int userId, bool isAdministrator, CancellationToken ct = default)
    {
        if (!IsEnforced || isAdministrator) return null;

        var version = _cache.GetOrCreate(VersionKey, e => { e.Priority = CacheItemPriority.NeverRemove; return 0; });
        var key = $"access-scope:{version}:{userId}";
        if (_cache.TryGetValue(key, out IReadOnlySet<int>? cached) && cached is not null) return cached;

        var roleId = await _db.AppUsers.AsNoTracking()
            .Where(u => u.Id == userId && u.IsActive && !u.IsDeleted)
            .Select(u => u.RoleId)
            .FirstOrDefaultAsync(ct);

        var direct = _db.UserConnections.AsNoTracking()
            .Where(uc => uc.AppUserId == userId && uc.IsActive)
            .Select(uc => uc.ConnectionId);

        var viaRole = _db.DepartmentConnections.AsNoTracking()
            .Where(dc => roleId != null && dc.RoleId == roleId && dc.IsActive)
            .Select(dc => dc.ConnectionId);

        var ids = await direct.Union(viaRole).ToListAsync(ct);
        IReadOnlySet<int> scope = ids.ToHashSet();

        _cache.Set(key, scope, CacheFor);
        return scope;
    }

    public async Task EnsureConnectionAllowedAsync(int? connectionId, CancellationToken ct = default)
    {
        var allowed = await GetAllowedConnectionIdsAsync(ct);
        if (allowed is null) return;

        if (connectionId is not { } id || !allowed.Contains(id))
        {
            throw new ForbiddenException("You do not have access to this connection.");
        }
    }

    public bool IsUnrestricted =>
        !IsEnforced || _currentUser.IsAdministrator || !_currentUser.IsAuthenticated || _currentUser.UserId is null;

    public IQueryable<int> AllowedConnectionIdsQuery()
    {
        var userId = _currentUser.UserId ?? 0;

        var direct = _db.UserConnections
            .Where(uc => uc.AppUserId == userId && uc.IsActive)
            .Select(uc => uc.ConnectionId);

        var viaRole = _db.DepartmentConnections
            .Where(dc => dc.IsActive && _db.AppUsers.Any(u => u.Id == userId && u.IsActive && !u.IsDeleted && u.RoleId == dc.RoleId))
            .Select(dc => dc.ConnectionId);

        return direct.Union(viaRole);
    }

    public void Invalidate()
    {
        var version = _cache.Get<int?>(VersionKey) ?? 0;
        _cache.Set(VersionKey, version + 1, new MemoryCacheEntryOptions { Priority = CacheItemPriority.NeverRemove });
        _requestScopeResolved = false;
    }
}
