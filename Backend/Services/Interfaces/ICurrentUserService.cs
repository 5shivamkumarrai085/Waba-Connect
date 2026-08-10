using System.Collections.Generic;
using System.Threading.Tasks;

namespace WhatsAppCampaignApi.Services.Interfaces;

/// <summary>
/// Resolves the caller from the current request.
///
/// <para>
/// Every member must tolerate the absence of an HttpContext and return a null/empty/false
/// result rather than throwing. Two send paths run with no request at all —
/// CampaignSchedulerService (a hosted service) and the Meta webhook — and both call into code
/// that records who performed an action.
/// </para>
/// </summary>
public interface ICurrentUserService
{
    /// <summary>The signed-in user's id, or null when unauthenticated or off-request.</summary>
    int? UserId { get; }

    string? Email { get; }

    string? UserName { get; }

    /// <summary>True only for a signed-in user holding administrator access.</summary>
    bool IsAdministrator { get; }

    bool IsAuthenticated { get; }

    /// <summary>Caller IP for audit rows, or null off-request.</summary>
    string? IpAddress { get; }

    string? UserAgent { get; }

    /// <summary>
    /// Whether the caller holds the given permission key. Returns true immediately for
    /// administrators without consulting the database. Returns false when unauthenticated.
    /// </summary>
    Task<bool> HasPermissionAsync(string permissionKey);

    /// <summary>Whether the caller holds at least one of the given keys.</summary>
    Task<bool> HasAnyPermissionAsync(IEnumerable<string> permissionKeys);

    /// <summary>
    /// The caller's effective permission keys. Empty for administrators — they bypass checks
    /// rather than holding an enumerated set.
    /// </summary>
    Task<List<string>> GetPermissionsAsync();
}
