using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Services.Security;

namespace WhatsAppCampaignApi.Services;

/// <summary>
/// Which active connections the signed-in caller may use. Delegates to <see cref="IAccessScope"/>.
/// </summary>
/// <remarks>
/// The userId/departmentId parameters are accepted for signature compatibility and ignored: scope
/// comes from the authenticated identity, never from values a client can put in a query string.
/// </remarks>
public class PermissionService : IPermissionService
{
    private readonly AppDbContext _dbContext;
    private readonly IAccessScope _accessScope;

    public PermissionService(AppDbContext dbContext, IAccessScope accessScope)
    {
        _dbContext = dbContext;
        _accessScope = accessScope;
    }

    public async Task<List<int>> GetAccessibleConnectionIdsAsync(string? userId = null, string? departmentId = null)
    {
        var query = _dbContext.Connections.AsNoTracking().Where(c => c.IsActive);

        if (await _accessScope.GetAllowedConnectionIdsAsync() is { } allowed)
        {
            var ids = allowed.ToArray();
            query = query.Where(c => ids.Contains(c.Id));
        }

        return await query.Select(c => c.Id).ToListAsync();
    }

    /// <summary>
    /// Scope only — not whether the connection is active, so an inactive connection can still be
    /// opened and re-enabled by someone assigned to it.
    /// </summary>
    public async Task<bool> HasAccessToConnectionAsync(int connectionId, string? userId = null, string? departmentId = null)
    {
        var allowed = await _accessScope.GetAllowedConnectionIdsAsync();
        return allowed is null || allowed.Contains(connectionId);
    }
}
