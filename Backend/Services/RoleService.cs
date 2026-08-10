using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Setup;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services;

/// <inheritdoc />
public class RoleService : IRoleService
{
    private readonly AppDbContext _dbContext;
    private readonly IPermissionResolver _permissionResolver;
    private readonly IAuditService _auditService;

    public RoleService(
        AppDbContext dbContext,
        IPermissionResolver permissionResolver,
        IAuditService auditService)
    {
        _dbContext = dbContext;
        _permissionResolver = permissionResolver;
        _auditService = auditService;
    }

    public async Task<List<RoleListItemResponse>> GetAllAsync() =>
        await _dbContext.Roles
            .AsNoTracking()
            .OrderBy(r => r.Id)
            .Select(r => new RoleListItemResponse
            {
                Id = r.Id,
                Name = r.Name,
                Description = r.Description,
                IsSystem = r.IsSystem,
                IsAdministrator = r.IsAdministrator,
                UserCount = r.Users.Count(u => !u.IsDeleted),
                PermissionCount = r.RolePermissions.Count,
                CreatedAt = r.CreatedAt
            })
            .ToListAsync();

    public async Task<RoleDetailResponse> GetByIdAsync(int id)
    {
        var role = await _dbContext.Roles
            .AsNoTracking()
            .Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .Include(r => r.Users)
            // Two collections: ~90 permissions multiplied by every user holding the role.
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == id)
            ?? throw new KeyNotFoundException($"Role with ID {id} not found.");

        return new RoleDetailResponse
        {
            Id = role.Id,
            Name = role.Name,
            Description = role.Description,
            IsSystem = role.IsSystem,
            IsAdministrator = role.IsAdministrator,
            UserCount = role.Users.Count(u => !u.IsDeleted),
            PermissionCount = role.RolePermissions.Count,
            CreatedAt = role.CreatedAt,
            PermissionKeys = role.RolePermissions.Select(rp => rp.Permission.Key).ToList(),
            Users = role.Users
                .Where(u => !u.IsDeleted)
                .OrderBy(u => u.FirstName)
                .Select(u => new RoleUserResponse
                {
                    Id = u.Id,
                    FullName = u.FullName,
                    Email = u.Email,
                    IsActive = u.IsActive
                })
                .ToList()
        };
    }

    public async Task<RoleDetailResponse> CreateAsync(CreateRoleRequest request)
    {
        var name = request.Name.Trim();

        if (await _dbContext.Roles.AnyAsync(r => r.Name.ToLower() == name.ToLower()))
            throw new InvalidOperationException("A role with this name already exists.");

        var role = new Role
        {
            Name = name,
            Description = request.Description,
            IsAdministrator = request.IsAdministrator,
            // Only the seeded roles are system roles; anything created here can be deleted.
            IsSystem = false
        };

        _dbContext.Roles.Add(role);
        await _dbContext.SaveChangesAsync();

        await ReplacePermissionsAsync(role, request.PermissionKeys);
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            "Role.Created", "Role", $"Created role '{role.Name}'.", nameof(Role), role.Id.ToString());

        return await GetByIdAsync(role.Id);
    }

    public async Task<RoleDetailResponse> UpdateAsync(int id, UpdateRoleRequest request)
    {
        var role = await _dbContext.Roles
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.Id == id)
            ?? throw new KeyNotFoundException($"Role with ID {id} not found.");

        var name = request.Name.Trim();
        if (await _dbContext.Roles.AnyAsync(r => r.Name.ToLower() == name.ToLower() && r.Id != id))
            throw new InvalidOperationException("A role with this name already exists.");

        // System roles can be renamed and re-scoped — only deletion is blocked — but revoking
        // administrator access from the last admin role would lock everyone out.
        if (role.IsAdministrator && !request.IsAdministrator)
            await EnsureNotLastAdministratorRoleAsync(role.Id);

        role.Name = name;
        role.Description = request.Description;
        role.IsAdministrator = request.IsAdministrator;
        role.UpdatedAt = DateTime.UtcNow;

        await ReplacePermissionsAsync(role, request.PermissionKeys);
        await _dbContext.SaveChangesAsync();

        // Everyone holding this role has a stale cached permission set.
        await _permissionResolver.InvalidateRoleAsync(role.Id);

        await _auditService.LogAsync(
            "Role.Updated", "Role", $"Updated role '{role.Name}'.", nameof(Role), role.Id.ToString());

        return await GetByIdAsync(role.Id);
    }

    public async Task DeleteAsync(int id)
    {
        var role = await _dbContext.Roles
            .Include(r => r.Users)
            .FirstOrDefaultAsync(r => r.Id == id)
            ?? throw new KeyNotFoundException($"Role with ID {id} not found.");

        if (role.IsSystem)
            throw new InvalidOperationException($"'{role.Name}' is a built-in role and cannot be deleted.");

        var assignedCount = role.Users.Count(u => !u.IsDeleted);
        if (assignedCount > 0)
        {
            throw new InvalidOperationException(
                $"'{role.Name}' is assigned to {assignedCount} user(s). Reassign them before deleting this role.");
        }

        _dbContext.Roles.Remove(role);
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            "Role.Deleted", "Role", $"Deleted role '{role.Name}'.", nameof(Role), id.ToString());
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task EnsureNotLastAdministratorRoleAsync(int excludingRoleId)
    {
        var otherAdminRoles = await _dbContext.Roles
            .CountAsync(r => r.IsAdministrator && r.Id != excludingRoleId);

        var standaloneAdmins = await _dbContext.AppUsers
            .CountAsync(u => !u.IsDeleted && u.IsActive && u.IsAdministrator);

        if (otherAdminRoles == 0 && standaloneAdmins == 0)
        {
            throw new InvalidOperationException(
                "This is the only role granting administrator access. Grant it elsewhere before removing it here.");
        }
    }

    private async Task ReplacePermissionsAsync(Role role, List<string> permissionKeys)
    {
        var existing = await _dbContext.RolePermissions
            .Where(rp => rp.RoleId == role.Id)
            .ToListAsync();

        _dbContext.RolePermissions.RemoveRange(existing);

        // An administrator role grants everything by short-circuit; storing rows would only
        // go stale as the catalogue grows.
        if (role.IsAdministrator) return;

        var ids = await _dbContext.Permissions
            .Where(p => permissionKeys.Contains(p.Key))
            .Select(p => p.Id)
            .ToListAsync();

        foreach (var permissionId in ids)
            _dbContext.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permissionId });
    }
}
