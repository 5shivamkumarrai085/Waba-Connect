using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.Setup;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services;

/// <inheritdoc />
public class UserService : IUserService
{
    private readonly AppDbContext _dbContext;
    private readonly IPermissionResolver _permissionResolver;
    private readonly IAuditService _auditService;
    private readonly ICurrentUserService _currentUser;

    public UserService(
        AppDbContext dbContext,
        IPermissionResolver permissionResolver,
        IAuditService auditService,
        ICurrentUserService currentUser)
    {
        _dbContext = dbContext;
        _permissionResolver = permissionResolver;
        _auditService = auditService;
        _currentUser = currentUser;
    }

    public async Task<PagedResponse<UserListItemResponse>> GetAllAsync(
        PagedRequest request, bool? isActive = null, int? roleId = null)
    {
        var query = _dbContext.AppUsers
            .AsNoTracking()
            .Include(u => u.Role)
            .Where(u => !u.IsDeleted);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(u =>
                u.FirstName.ToLower().Contains(search) ||
                (u.LastName != null && u.LastName.ToLower().Contains(search)) ||
                u.Email.ToLower().Contains(search) ||
                (u.PhoneNumber != null && u.PhoneNumber.Contains(search)));
        }

        if (isActive.HasValue) query = query.Where(u => u.IsActive == isActive.Value);
        if (roleId.HasValue) query = query.Where(u => u.RoleId == roleId.Value);

        query = request.SortBy?.ToLower() switch
        {
            "name" => request.SortDescending ? query.OrderByDescending(u => u.FirstName) : query.OrderBy(u => u.FirstName),
            "email" => request.SortDescending ? query.OrderByDescending(u => u.Email) : query.OrderBy(u => u.Email),
            "role" => request.SortDescending ? query.OrderByDescending(u => u.Role!.Name) : query.OrderBy(u => u.Role!.Name),
            _ => request.SortDescending ? query.OrderByDescending(u => u.CreatedAt) : query.OrderBy(u => u.CreatedAt)
        };

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync();

        return new PagedResponse<UserListItemResponse>
        {
            Items = items.Select(MapToListItem).ToList(),
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize
        };
    }

    public async Task<UserDetailResponse> GetByIdAsync(int id)
    {
        var user = await _dbContext.AppUsers
            .AsNoTracking()
            .Include(u => u.Role)
            .Include(u => u.UserPermissions).ThenInclude(up => up.Permission)
            .FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted)
            ?? throw new KeyNotFoundException($"User with ID {id} not found.");

        // Show the role's grants when the user hasn't overridden them, so the matrix opens
        // pre-filled with what the selected role actually implies rather than empty.
        var permissionKeys = user.UsesCustomPermissions
            ? user.UserPermissions.Select(up => up.Permission.Key).ToList()
            : user.RoleId.HasValue
                ? await _dbContext.RolePermissions
                    .Where(rp => rp.RoleId == user.RoleId.Value)
                    .Select(rp => rp.Permission.Key)
                    .ToListAsync()
                : new List<string>();

        var response = new UserDetailResponse
        {
            DialCode = user.DialCode,
            DefaultLanguageCode = user.DefaultLanguageCode,
            SendWelcomeEmail = user.SendWelcomeEmail,
            MustChangePassword = user.MustChangePassword,
            UsesCustomPermissions = user.UsesCustomPermissions,
            PermissionKeys = permissionKeys
        };

        CopyListFields(user, response);
        return response;
    }

    public async Task<UserDashboardResponse> GetDashboardAsync()
    {
        var users = await _dbContext.AppUsers
            .AsNoTracking()
            .Where(u => !u.IsDeleted)
            .Select(u => new { u.IsActive, u.IsAdministrator, RoleIsAdmin = u.Role != null && u.Role.IsAdministrator })
            .ToListAsync();

        return new UserDashboardResponse
        {
            TotalUsers = users.Count,
            ActiveUsers = users.Count(u => u.IsActive),
            InactiveUsers = users.Count(u => !u.IsActive),
            AdministratorCount = users.Count(u => u.IsAdministrator || u.RoleIsAdmin)
        };
    }

    public async Task<UserDetailResponse> CreateAsync(CreateUserRequest request)
    {
        var email = NormalizeEmail(request.Email);

        if (await _dbContext.AppUsers.AnyAsync(u => u.Email == email && !u.IsDeleted))
            throw new InvalidOperationException("A user with this email address already exists.");

        if (string.IsNullOrWhiteSpace(request.Password))
            throw new ArgumentException("A password is required when creating a user.");

        if (request.Password != request.ConfirmPassword)
            throw new ArgumentException("Password and confirmation do not match.");

        await ValidateRoleAsync(request.RoleId);

        var user = new AppUser
        {
            FirstName = request.FirstName.Trim(),
            LastName = string.IsNullOrWhiteSpace(request.LastName) ? null : request.LastName.Trim(),
            Email = email,
            PhoneNumber = request.PhoneNumber,
            DialCode = request.DialCode,
            ProfileImageUrl = request.ProfileImageUrl,
            DefaultLanguageCode = request.DefaultLanguageCode,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            IsActive = request.IsActive,
            IsVerified = request.IsVerified,
            SendWelcomeEmail = request.SendWelcomeEmail,
            IsAdministrator = request.IsAdministrator,
            RoleId = request.IsAdministrator ? null : request.RoleId,
            UsesCustomPermissions = !request.IsAdministrator && request.UsesCustomPermissions
        };

        _dbContext.AppUsers.Add(user);
        await _dbContext.SaveChangesAsync();

        await ReplaceUserPermissionsAsync(user, request.PermissionKeys);
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            "User.Created", "User",
            $"Created user {user.Email}.", nameof(AppUser), user.Id.ToString());

        return await GetByIdAsync(user.Id);
    }

    public async Task<UserDetailResponse> UpdateAsync(int id, UpdateUserRequest request)
    {
        var user = await _dbContext.AppUsers
            .Include(u => u.UserPermissions)
            .FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted)
            ?? throw new KeyNotFoundException($"User with ID {id} not found.");

        var email = NormalizeEmail(request.Email);
        if (await _dbContext.AppUsers.AnyAsync(u => u.Email == email && u.Id != id && !u.IsDeleted))
            throw new InvalidOperationException("A user with this email address already exists.");

        await ValidateRoleAsync(request.RoleId);

        // Guard against an administrator removing their own admin access and locking the
        // organisation out of user management entirely.
        if (user.IsAdministrator && !request.IsAdministrator)
            await EnsureNotLastAdministratorAsync(user.Id, "remove administrator access from");

        user.FirstName = request.FirstName.Trim();
        user.LastName = string.IsNullOrWhiteSpace(request.LastName) ? null : request.LastName.Trim();
        user.Email = email;
        user.PhoneNumber = request.PhoneNumber;
        user.DialCode = request.DialCode;
        user.ProfileImageUrl = request.ProfileImageUrl;
        user.DefaultLanguageCode = request.DefaultLanguageCode;
        user.IsActive = request.IsActive;
        user.IsVerified = request.IsVerified;
        user.SendWelcomeEmail = request.SendWelcomeEmail;
        user.IsAdministrator = request.IsAdministrator;
        user.RoleId = request.IsAdministrator ? null : request.RoleId;
        user.UsesCustomPermissions = !request.IsAdministrator && request.UsesCustomPermissions;
        user.UpdatedAt = DateTime.UtcNow;

        // Blank means "leave it alone" — an admin editing a phone number shouldn't need to
        // know or reset the person's password.
        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            if (request.Password != request.ConfirmPassword)
                throw new ArgumentException("Password and confirmation do not match.");

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
            // An admin-set password is known to someone else, so force a reset at next login.
            user.MustChangePassword = true;
        }

        await ReplaceUserPermissionsAsync(user, request.PermissionKeys);
        await _dbContext.SaveChangesAsync();

        _permissionResolver.InvalidateUser(user.Id);

        await _auditService.LogAsync(
            "User.Updated", "User",
            $"Updated user {user.Email}.", nameof(AppUser), user.Id.ToString());

        return await GetByIdAsync(user.Id);
    }

    public async Task DeleteAsync(int id)
    {
        var user = await _dbContext.AppUsers.FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted)
            ?? throw new KeyNotFoundException($"User with ID {id} not found.");

        if (_currentUser.UserId == id)
            throw new InvalidOperationException("You cannot delete your own account.");

        if (user.IsAdministrator)
            await EnsureNotLastAdministratorAsync(id, "delete");

        user.IsDeleted = true;
        user.IsActive = false;
        user.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        _permissionResolver.InvalidateUser(id);

        await _auditService.LogAsync(
            "User.Deleted", "User",
            $"Deleted user {user.Email}.", nameof(AppUser), id.ToString());
    }

    public async Task<UserListItemResponse> ToggleActiveAsync(int id)
    {
        var user = await _dbContext.AppUsers
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted)
            ?? throw new KeyNotFoundException($"User with ID {id} not found.");

        if (_currentUser.UserId == id && user.IsActive)
            throw new InvalidOperationException("You cannot deactivate your own account.");

        if (user.IsActive && user.IsAdministrator)
            await EnsureNotLastAdministratorAsync(id, "deactivate");

        user.IsActive = !user.IsActive;
        user.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // A deactivated account must stop being able to act immediately rather than when its
        // cached permission set happens to expire.
        _permissionResolver.InvalidateUser(id);

        await _auditService.LogAsync(
            user.IsActive ? "User.Activated" : "User.Deactivated", "User",
            $"{(user.IsActive ? "Activated" : "Deactivated")} user {user.Email}.",
            nameof(AppUser), id.ToString());

        return MapToListItem(user);
    }

    public async Task<List<AssignableUserResponse>> GetAssignableAsync() =>
        await _dbContext.AppUsers
            .AsNoTracking()
            .Where(u => !u.IsDeleted && u.IsActive)
            .OrderBy(u => u.FirstName)
            .Select(u => new AssignableUserResponse
            {
                Id = u.Id,
                Name = u.LastName == null ? u.FirstName : u.FirstName + " " + u.LastName,
                Email = u.Email
            })
            .ToListAsync();

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private async Task ValidateRoleAsync(int? roleId)
    {
        if (roleId.HasValue && !await _dbContext.Roles.AnyAsync(r => r.Id == roleId.Value))
            throw new ArgumentException($"Role with ID {roleId} does not exist.");
    }

    /// <summary>
    /// Blocks the change if it would leave the system with no active administrator. Without
    /// this, an organisation can lock itself out of user and role management with no recovery
    /// path short of editing the database by hand.
    /// </summary>
    private async Task EnsureNotLastAdministratorAsync(int excludingUserId, string verb)
    {
        var otherAdmins = await _dbContext.AppUsers
            .Where(u => !u.IsDeleted && u.IsActive && u.Id != excludingUserId)
            .Where(u => u.IsAdministrator || (u.Role != null && u.Role.IsAdministrator))
            .CountAsync();

        if (otherAdmins == 0)
        {
            throw new InvalidOperationException(
                $"You cannot {verb} the only remaining administrator. Grant administrator access to another user first.");
        }
    }

    /// <summary>
    /// Replaces the user's permission rows. Per-user permissions REPLACE the role's grants
    /// rather than adding to them, so unchecking a box the role granted actually revokes it.
    /// </summary>
    private async Task ReplaceUserPermissionsAsync(AppUser user, List<string> permissionKeys)
    {
        var existing = await _dbContext.UserPermissions
            .Where(up => up.UserId == user.Id)
            .ToListAsync();

        _dbContext.UserPermissions.RemoveRange(existing);

        // Administrators short-circuit every check, and a role-driven user reads from the
        // role — in both cases storing rows would go stale without ever being consulted.
        if (user.IsAdministrator || !user.UsesCustomPermissions) return;

        var ids = await _dbContext.Permissions
            .Where(p => permissionKeys.Contains(p.Key))
            .Select(p => p.Id)
            .ToListAsync();

        foreach (var permissionId in ids)
            _dbContext.UserPermissions.Add(new UserPermission { UserId = user.Id, PermissionId = permissionId });
    }

    private static UserListItemResponse MapToListItem(AppUser user)
    {
        var response = new UserListItemResponse();
        CopyListFields(user, response);
        return response;
    }

    private static void CopyListFields(AppUser user, UserListItemResponse target)
    {
        target.Id = user.Id;
        target.FirstName = user.FirstName;
        target.LastName = user.LastName;
        target.FullName = user.FullName;
        target.Email = user.Email;
        target.PhoneNumber = user.PhoneNumber;
        target.ProfileImageUrl = user.ProfileImageUrl;
        target.RoleId = user.RoleId;
        target.RoleName = user.Role?.Name;
        target.IsActive = user.IsActive;
        target.IsVerified = user.IsVerified;
        target.IsAdministrator = user.IsAdministrator || (user.Role?.IsAdministrator ?? false);
        target.CreatedAt = user.CreatedAt;
        target.LastLoginAt = user.LastLoginAt;
    }
}
