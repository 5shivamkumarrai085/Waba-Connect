using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Services.Security;

namespace WhatsAppCampaignApi.Services;

/// <summary>
/// Connection access: which users and which roles ("departments") may use which connections.
/// </summary>
/// <remarks>
/// Every row is tied to a real account or role. Names, emails, department names and member counts
/// are read from those records rather than stored from what an operator typed, so the screen
/// cannot drift from who actually exists. <see cref="IAccessScope"/> enforces what is stored here.
/// </remarks>
public class PermissionManagementService : IPermissionManagementService
{
    private readonly AppDbContext _dbContext;
    private readonly IAuditService _auditService;
    private readonly IAccessScope _accessScope;

    public PermissionManagementService(AppDbContext dbContext, IAuditService auditService, IAccessScope accessScope)
    {
        _dbContext = dbContext;
        _auditService = auditService;
        _accessScope = accessScope;
    }

    // ── Users ──────────────────────────────────────────────────────────────────────────────────

    public async Task<UserPermissionDashboardResponse> GetUserPermissionsDashboardAsync()
    {
        var users = await GetUserPermissionsAsync();

        return new UserPermissionDashboardResponse
        {
            TotalUsers = await _dbContext.AppUsers.CountAsync(u => !u.IsDeleted && u.IsActive),
            UsersWithAccess = users.Count(u => u.IsActive && u.ConnectionIds.Count > 0),
            TotalConnections = await _dbContext.Connections.CountAsync(),
            ActivePermissions = users.Where(u => u.IsActive).Sum(u => u.ConnectionIds.Count),
            UserPermissions = users
        };
    }

    public async Task<List<UserPermissionResponse>> GetUserPermissionsAsync(string? department = null, int? connectionId = null, bool? activeOnly = null)
    {
        var query = _dbContext.UserConnections.AsNoTracking()
            .Where(u => !u.AppUser.IsDeleted);

        if (!string.IsNullOrWhiteSpace(department) && !department.Equals("All Departments", StringComparison.OrdinalIgnoreCase))
        {
            var dept = department.ToLower();
            query = query.Where(u => u.AppUser.Role != null && u.AppUser.Role.Name.ToLower() == dept);
        }

        if (connectionId is > 0)
        {
            // Every assignment of the users who hold this connection, not only the matching row.
            var holders = _dbContext.UserConnections.Where(x => x.ConnectionId == connectionId).Select(x => x.AppUserId);
            query = query.Where(u => holders.Contains(u.AppUserId));
        }

        if (activeOnly.HasValue) query = query.Where(u => u.IsActive == activeOnly.Value);

        var rows = await query
            .OrderBy(u => u.AppUserId)
            .Select(u => new
            {
                u.Id,
                u.AppUserId,
                u.ConnectionId,
                ConnectionName = u.Connection.Name,
                u.IsActive,
                u.CreatedAt,
                u.AppUser.FirstName,
                u.AppUser.LastName,
                u.AppUser.Email,
                RoleName = u.AppUser.Role != null ? u.AppUser.Role.Name : null
            })
            .ToListAsync();

        return rows.GroupBy(r => r.AppUserId).Select(g =>
        {
            var first = g.First();
            var connIds = g.Select(x => x.ConnectionId).Distinct().ToList();
            return new UserPermissionResponse
            {
                Id = first.Id,
                UserId = first.AppUserId.ToString(),
                UserName = $"{first.FirstName} {first.LastName}".Trim(),
                UserEmail = first.Email,
                DepartmentName = first.RoleName ?? string.Empty,
                ConnectionIds = connIds,
                ConnectionNames = g.Select(x => x.ConnectionName ?? $"Connection {x.ConnectionId}").Distinct().ToList(),
                PermissionScopeText = $"{connIds.Count} Connection{(connIds.Count == 1 ? "" : "s")}",
                IsActive = g.Any(x => x.IsActive),
                CreatedAt = g.Min(x => x.CreatedAt)
            };
        }).ToList();
    }

    public async Task<UserPermissionResponse> AssignUserPermissionAsync(AssignUserPermissionRequest request)
    {
        if (!int.TryParse(request.UserId, out var appUserId))
            throw new ArgumentException("Choose an existing user.");

        var user = await _dbContext.AppUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == appUserId && !u.IsDeleted)
            ?? throw new KeyNotFoundException("User not found.");

        var connectionIds = await ValidConnectionIdsAsync(request.ConnectionIds);

        var existing = await _dbContext.UserConnections.Where(u => u.AppUserId == appUserId).ToListAsync();
        _dbContext.UserConnections.RemoveRange(existing);

        var fullName = $"{user.FirstName} {user.LastName}".Trim();
        _dbContext.UserConnections.AddRange(connectionIds.Select(id => new UserConnection
        {
            AppUserId = appUserId,
            UserId = appUserId.ToString(),
            UserName = fullName,
            UserEmail = user.Email,
            ConnectionId = id,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        }));

        await _dbContext.SaveChangesAsync();
        _accessScope.Invalidate();

        // Who can reach which account is an access-control change, audited like role edits.
        await _auditService.LogAsync(
            "ConnectionAccess.Updated",
            "Security",
            $"Set connection access for {user.Email} to {connectionIds.Count} connection(s).",
            entityType: "AppUser",
            entityId: appUserId.ToString());

        return (await GetUserPermissionsAsync()).FirstOrDefault(u => u.UserId == appUserId.ToString())
            ?? new UserPermissionResponse { UserId = appUserId.ToString(), UserName = fullName, UserEmail = user.Email };
    }

    public async Task<bool> ToggleUserPermissionStatusAsync(int id)
    {
        var row = await _dbContext.UserConnections.FindAsync(id);
        if (row == null) return false;

        var allForUser = await _dbContext.UserConnections.Where(u => u.AppUserId == row.AppUserId).ToListAsync();
        var enable = !allForUser.Any(x => x.IsActive);
        foreach (var item in allForUser) item.IsActive = enable;

        await _dbContext.SaveChangesAsync();
        _accessScope.Invalidate();

        await _auditService.LogAsync(
            enable ? "ConnectionAccess.Enabled" : "ConnectionAccess.Disabled",
            "Security",
            $"Connection access for {row.UserEmail} was {(enable ? "enabled" : "disabled")}.",
            entityType: "AppUser",
            entityId: row.AppUserId.ToString());

        return true;
    }

    public async Task<bool> DeleteUserPermissionAsync(int id)
    {
        var row = await _dbContext.UserConnections.FindAsync(id);
        if (row == null) return false;

        var allForUser = await _dbContext.UserConnections.Where(u => u.AppUserId == row.AppUserId).ToListAsync();
        _dbContext.UserConnections.RemoveRange(allForUser);
        await _dbContext.SaveChangesAsync();
        _accessScope.Invalidate();

        await _auditService.LogAsync(
            "ConnectionAccess.Deleted",
            "Security",
            $"Removed all connection access for {row.UserEmail}.",
            entityType: "AppUser",
            entityId: row.AppUserId.ToString());

        return true;
    }

    // ── Roles ("departments") ─────────────────────────────────────────────────────────────────

    public async Task<DepartmentPermissionDashboardResponse> GetDepartmentPermissionsDashboardAsync()
    {
        var depts = await GetDepartmentPermissionsAsync();

        return new DepartmentPermissionDashboardResponse
        {
            TotalDepartments = await _dbContext.Roles.CountAsync(),
            DepartmentsWithAccess = depts.Count(d => d.IsActive && d.ConnectionIds.Count > 0),
            TotalConnections = await _dbContext.Connections.CountAsync(),
            ActivePermissions = depts.Where(d => d.IsActive).Sum(d => d.ConnectionIds.Count),
            DepartmentPermissions = depts
        };
    }

    public async Task<List<DepartmentPermissionResponse>> GetDepartmentPermissionsAsync(int? connectionId = null, bool? activeOnly = null)
    {
        var query = _dbContext.DepartmentConnections.AsNoTracking();

        if (connectionId is > 0)
        {
            var holders = _dbContext.DepartmentConnections.Where(x => x.ConnectionId == connectionId).Select(x => x.RoleId);
            query = query.Where(d => holders.Contains(d.RoleId));
        }

        if (activeOnly.HasValue) query = query.Where(d => d.IsActive == activeOnly.Value);

        var rows = await query
            .OrderBy(d => d.RoleId)
            .Select(d => new
            {
                d.Id,
                d.RoleId,
                RoleName = d.Role.Name,
                d.Description,
                d.ConnectionId,
                ConnectionName = d.Connection.Name,
                d.IsActive,
                d.CreatedAt,
                Members = _dbContext.AppUsers.Count(u => u.RoleId == d.RoleId && !u.IsDeleted && u.IsActive)
            })
            .ToListAsync();

        return rows.GroupBy(r => r.RoleId).Select(g =>
        {
            var first = g.First();
            return new DepartmentPermissionResponse
            {
                Id = first.Id,
                DepartmentId = first.RoleId.ToString(),
                DepartmentName = first.RoleName,
                Description = first.Description,
                MemberCount = first.Members,
                ConnectionIds = g.Select(x => x.ConnectionId).Distinct().ToList(),
                ConnectionNames = g.Select(x => x.ConnectionName ?? $"Connection {x.ConnectionId}").Distinct().ToList(),
                IsActive = g.Any(x => x.IsActive),
                CreatedAt = g.Min(x => x.CreatedAt)
            };
        }).ToList();
    }

    public async Task<DepartmentPermissionResponse> AssignDepartmentPermissionAsync(AssignDepartmentPermissionRequest request)
    {
        if (!int.TryParse(request.DepartmentId, out var roleId))
            throw new ArgumentException("Choose an existing role.");

        var role = await _dbContext.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roleId)
            ?? throw new KeyNotFoundException("Role not found.");

        var connectionIds = await ValidConnectionIdsAsync(request.ConnectionIds);

        var existing = await _dbContext.DepartmentConnections.Where(d => d.RoleId == roleId).ToListAsync();
        _dbContext.DepartmentConnections.RemoveRange(existing);

        _dbContext.DepartmentConnections.AddRange(connectionIds.Select(id => new DepartmentConnection
        {
            RoleId = roleId,
            DepartmentId = roleId.ToString(),
            DepartmentName = role.Name,
            Description = request.Description?.Trim() ?? string.Empty,
            ConnectionId = id,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        }));

        await _dbContext.SaveChangesAsync();
        _accessScope.Invalidate();

        await _auditService.LogAsync(
            "ConnectionAccess.RoleUpdated",
            "Security",
            $"Set connection access for role '{role.Name}' to {connectionIds.Count} connection(s).",
            entityType: "Role",
            entityId: roleId.ToString());

        return (await GetDepartmentPermissionsAsync()).FirstOrDefault(d => d.DepartmentId == roleId.ToString())
            ?? new DepartmentPermissionResponse { DepartmentId = roleId.ToString(), DepartmentName = role.Name };
    }

    public async Task<bool> ToggleDepartmentPermissionStatusAsync(int id)
    {
        var row = await _dbContext.DepartmentConnections.FindAsync(id);
        if (row == null) return false;

        var allForRole = await _dbContext.DepartmentConnections.Where(d => d.RoleId == row.RoleId).ToListAsync();
        var enable = !allForRole.Any(x => x.IsActive);
        foreach (var item in allForRole) item.IsActive = enable;

        await _dbContext.SaveChangesAsync();
        _accessScope.Invalidate();

        await _auditService.LogAsync(
            enable ? "ConnectionAccess.RoleEnabled" : "ConnectionAccess.RoleDisabled",
            "Security",
            $"Connection access for role '{row.DepartmentName}' was {(enable ? "enabled" : "disabled")}.",
            entityType: "Role",
            entityId: row.RoleId.ToString());

        return true;
    }

    public async Task<bool> DeleteDepartmentPermissionAsync(int id)
    {
        var row = await _dbContext.DepartmentConnections.FindAsync(id);
        if (row == null) return false;

        var allForRole = await _dbContext.DepartmentConnections.Where(d => d.RoleId == row.RoleId).ToListAsync();
        _dbContext.DepartmentConnections.RemoveRange(allForRole);
        await _dbContext.SaveChangesAsync();
        _accessScope.Invalidate();

        await _auditService.LogAsync(
            "ConnectionAccess.RoleDeleted",
            "Security",
            $"Removed all connection access for role '{row.DepartmentName}'.",
            entityType: "Role",
            entityId: row.RoleId.ToString());

        return true;
    }

    // ── Pickers ───────────────────────────────────────────────────────────────────────────────

    public async Task<PermissionCandidatesResponse> GetCandidatesAsync()
    {
        var users = await _dbContext.AppUsers.AsNoTracking()
            .Where(u => !u.IsDeleted && u.IsActive)
            .OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
            .Select(u => new PermissionCandidateUser(
                u.Id,
                (u.FirstName + " " + (u.LastName ?? "")).Trim(),
                u.Email,
                u.Role != null ? u.Role.Name : null,
                u.IsAdministrator))
            .ToListAsync();

        var roles = await _dbContext.Roles.AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new PermissionCandidateRole(
                r.Id,
                r.Name,
                _dbContext.AppUsers.Count(u => u.RoleId == r.Id && !u.IsDeleted && u.IsActive)))
            .ToListAsync();

        return new PermissionCandidatesResponse(users, roles);
    }

    private async Task<List<int>> ValidConnectionIdsAsync(IEnumerable<int> requested)
    {
        var ids = requested.Distinct().ToList();
        if (ids.Count == 0) throw new ArgumentException("Choose at least one connection.");

        var existing = await _dbContext.Connections.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync();

        if (existing.Count != ids.Count) throw new ArgumentException("One or more connections do not exist.");
        return existing;
    }
}
