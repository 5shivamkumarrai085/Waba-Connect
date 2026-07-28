using System.Collections.Generic;
using System.Threading.Tasks;

namespace WhatsAppCampaignApi.Services.Interfaces;

public interface IPermissionService
{
    /// <summary>
    /// Gets list of Connection IDs accessible to the current request user or department.
    /// Admin user sees all connections. Stub implementation currently returns all active connections.
    /// </summary>
    Task<List<int>> GetAccessibleConnectionIdsAsync(string? userId = null, string? departmentId = null);

    /// <summary>
    /// Checks whether the user/department has access to a specific connection.
    /// </summary>
    Task<bool> HasAccessToConnectionAsync(int connectionId, string? userId = null, string? departmentId = null);
}
