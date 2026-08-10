using System.Collections.Generic;
using System.Threading.Tasks;

namespace WhatsAppCampaignApi.Services.Interfaces;

/// <summary>
/// Works out what a given user is allowed to do, independent of any HTTP request. Split out from
/// <see cref="ICurrentUserService"/> so background work and tests can resolve permissions for an
/// arbitrary user.
/// </summary>
public interface IPermissionResolver
{
    /// <summary>
    /// Effective permission keys for a user:
    /// administrator → all keys; custom permissions → the user's own rows; otherwise → the role's rows.
    /// Results are cached briefly and evicted explicitly when a user or role is saved.
    /// </summary>
    Task<HashSet<string>> GetEffectivePermissionsAsync(int userId);

    /// <summary>Drops the cached permission set for one user.</summary>
    void InvalidateUser(int userId);

    /// <summary>Drops cached sets for every user holding the given role.</summary>
    Task InvalidateRoleAsync(int roleId);

    /// <summary>Drops every cached permission set.</summary>
    void InvalidateAll();
}
