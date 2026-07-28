using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services;

public class PermissionService : IPermissionService
{
    private readonly AppDbContext _dbContext;

    public PermissionService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<int>> GetAccessibleConnectionIdsAsync(string? userId = null, string? departmentId = null)
    {
        // Admin mode / stub: return all active connection IDs
        return await _dbContext.Connections
            .AsNoTracking()
            .Where(c => c.IsActive)
            .Select(c => c.Id)
            .ToListAsync();
    }

    public async Task<bool> HasAccessToConnectionAsync(int connectionId, string? userId = null, string? departmentId = null)
    {
        var accessibleIds = await GetAccessibleConnectionIdsAsync(userId, departmentId);
        return accessibleIds.Contains(connectionId);
    }
}
