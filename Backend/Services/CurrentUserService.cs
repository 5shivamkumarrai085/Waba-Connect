using System.Security.Claims;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services;

/// <inheritdoc />
public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _accessor;
    private readonly IPermissionResolver _permissionResolver;

    public CurrentUserService(IHttpContextAccessor accessor, IPermissionResolver permissionResolver)
    {
        _accessor = accessor;
        _permissionResolver = permissionResolver;
    }

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public int? UserId
    {
        get
        {
            var raw = Principal?.FindFirstValue(AuthClaims.UserId);
            return int.TryParse(raw, out var id) ? id : null;
        }
    }

    public string? Email => Principal?.FindFirstValue(AuthClaims.Email);

    public string? UserName => Principal?.FindFirstValue(AuthClaims.Name);

    public bool IsAdministrator =>
        string.Equals(Principal?.FindFirstValue(AuthClaims.IsAdministrator), "true",
            StringComparison.OrdinalIgnoreCase);

    public string? IpAddress => _accessor.HttpContext?.Connection?.RemoteIpAddress?.ToString();

    public string? UserAgent
    {
        get
        {
            var value = _accessor.HttpContext?.Request?.Headers.UserAgent.ToString();
            return string.IsNullOrWhiteSpace(value) ? null : Truncate(value, 500);
        }
    }

    public async Task<bool> HasPermissionAsync(string permissionKey)
    {
        // Short-circuit before touching the database. Administrators are never expanded into
        // permission rows, so anything added to the catalogue later reaches them automatically.
        if (IsAdministrator) return true;

        var userId = UserId;
        if (userId is null) return false;

        var permissions = await _permissionResolver.GetEffectivePermissionsAsync(userId.Value);
        return permissions.Contains(permissionKey);
    }

    public async Task<bool> HasAnyPermissionAsync(IEnumerable<string> permissionKeys)
    {
        if (IsAdministrator) return true;

        var userId = UserId;
        if (userId is null) return false;

        var permissions = await _permissionResolver.GetEffectivePermissionsAsync(userId.Value);
        return permissionKeys.Any(permissions.Contains);
    }

    public async Task<List<string>> GetPermissionsAsync()
    {
        var userId = UserId;
        if (userId is null) return new List<string>();

        var permissions = await _permissionResolver.GetEffectivePermissionsAsync(userId.Value);
        return permissions.ToList();
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
