using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.Auth;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services;

/// <inheritdoc />
public class AuthService : IAuthService
{
    private readonly AppDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly IPermissionResolver _permissionResolver;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _auditService;

    public AuthService(
        AppDbContext dbContext,
        IConfiguration configuration,
        IPermissionResolver permissionResolver,
        ICurrentUserService currentUser,
        IAuditService auditService)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _permissionResolver = permissionResolver;
        _currentUser = currentUser;
        _auditService = auditService;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request)
    {
        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;

        var user = await _dbContext.AppUsers
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email && !u.IsDeleted);

        // Verify against a dummy hash when the account is missing so that a nonexistent email
        // and a wrong password take comparable time — otherwise response timing enumerates
        // which addresses have accounts.
        if (user is null)
        {
            BCrypt.Net.BCrypt.Verify(request.Password ?? string.Empty, DummyHash);
            await RecordAttemptAsync(email, false, "No account with that email address.", null);
            throw new UnauthorizedAccessException("Invalid email or password.");
        }

        if (!BCrypt.Net.BCrypt.Verify(request.Password ?? string.Empty, user.PasswordHash))
        {
            await RecordAttemptAsync(email, false, "Invalid password.", user.Id);
            throw new UnauthorizedAccessException("Invalid email or password.");
        }

        // Checked after the password so a deactivated account isn't revealed to someone who
        // doesn't hold its credentials.
        if (!user.IsActive)
        {
            await RecordAttemptAsync(email, false, "Account is deactivated.", user.Id);
            throw new InvalidOperationException("This account has been deactivated. Contact an administrator.");
        }

        user.LastLoginAt = DateTime.UtcNow;
        await RecordAttemptAsync(email, true, null, user.Id);

        var (token, expiresAt) = BuildToken(user);

        return new LoginResponse
        {
            Token = token,
            ExpiresAt = expiresAt,
            User = await BuildCurrentUserAsync(user)
        };
    }

    public async Task<CurrentUserResponse> GetCurrentUserAsync(int userId)
    {
        var user = await _dbContext.AppUsers
            .Include(u => u.Role)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted)
            ?? throw new KeyNotFoundException("User not found.");

        return await BuildCurrentUserAsync(user);
    }

    public async Task ChangePasswordAsync(int userId, ChangePasswordRequest request)
    {
        if (request.NewPassword != request.ConfirmPassword)
            throw new InvalidOperationException("New password and confirmation do not match.");

        var user = await _dbContext.AppUsers.FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted)
            ?? throw new KeyNotFoundException("User not found.");

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword ?? string.Empty, user.PasswordHash))
            throw new UnauthorizedAccessException("Current password is incorrect.");

        if (BCrypt.Net.BCrypt.Verify(request.NewPassword, user.PasswordHash))
            throw new InvalidOperationException("The new password must be different from the current one.");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.MustChangePassword = false;
        user.UpdatedAt = DateTime.UtcNow;

        _dbContext.AuditLogs.Add(new AuditLog
        {
            Event = "Auth.PasswordChanged",
            Category = "Auth",
            UserId = user.Id,
            UserName = user.FullName,
            Description = $"{user.Email} changed their password.",
            EntityType = nameof(AppUser),
            EntityId = user.Id.ToString(),
            IpAddress = _currentUser.IpAddress
        });

        await _dbContext.SaveChangesAsync();
        _permissionResolver.InvalidateUser(user.Id);
    }

    private async Task<CurrentUserResponse> BuildCurrentUserAsync(AppUser user)
    {
        var isAdmin = user.IsAdministrator || (user.Role?.IsAdministrator ?? false);

        return new CurrentUserResponse
        {
            Id = user.Id,
            FirstName = user.FirstName,
            LastName = user.LastName,
            FullName = user.FullName,
            Email = user.Email,
            PhoneNumber = user.PhoneNumber,
            ProfileImageUrl = user.ProfileImageUrl,
            DefaultLanguageCode = user.DefaultLanguageCode,
            RoleId = user.RoleId,
            RoleName = user.Role?.Name,
            IsAdministrator = isAdmin,
            MustChangePassword = user.MustChangePassword,
            IsVerified = user.IsVerified,
            // Administrators get an empty list plus the flag — the client treats the flag as
            // "allow everything" rather than checking membership.
            Permissions = isAdmin
                ? new List<string>()
                : (await _permissionResolver.GetEffectivePermissionsAsync(user.Id)).ToList()
        };
    }

    private (string Token, DateTime ExpiresAt) BuildToken(AppUser user)
    {
        var key = _configuration["Auth:Jwt:Key"]
            ?? throw new InvalidOperationException("Auth:Jwt:Key is not configured.");

        if (Encoding.UTF8.GetByteCount(key) < 32)
            throw new InvalidOperationException("Auth:Jwt:Key must be at least 32 bytes for HMAC-SHA256.");

        var expiryHours = _configuration.GetValue<int?>("Auth:Jwt:ExpiryHours") ?? 8;
        var expiresAt = DateTime.UtcNow.AddHours(expiryHours);
        var isAdmin = user.IsAdministrator || (user.Role?.IsAdministrator ?? false);

        var claims = new List<Claim>
        {
            new(AuthClaims.UserId, user.Id.ToString()),
            new(AuthClaims.Email, user.Email),
            new(AuthClaims.Name, user.FullName),
            new(AuthClaims.IsAdministrator, isAdmin ? "true" : "false"),
            new(AuthClaims.MustChangePassword, user.MustChangePassword ? "true" : "false"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (!string.IsNullOrWhiteSpace(user.Role?.Name))
            claims.Add(new Claim(AuthClaims.RoleName, user.Role!.Name));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _configuration["Auth:Jwt:Issuer"],
            audience: _configuration["Auth:Jwt:Audience"],
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    /// <summary>
    /// The single choke point for every sign-in outcome, so auditing here covers all four call
    /// sites at once and a future fifth cannot slip past unrecorded.
    /// </summary>
    private async Task RecordAttemptAsync(string email, bool success, string? failureReason, int? userId)
    {
        _dbContext.LoginAttempts.Add(new LoginAttempt
        {
            Email = email,
            Success = success,
            FailureReason = failureReason,
            UserId = userId,
            IpAddress = _currentUser.IpAddress,
            UserAgent = _currentUser.UserAgent
        });

        await _dbContext.SaveChangesAsync();

        // Attribution is passed explicitly: sign-in runs before there is a token, so
        // ICurrentUserService resolves to nothing at this point.
        //
        // A failed attempt is deliberately NOT attributed to the matched user id — someone
        // typing another person's email is not that person, and recording it as their action
        // would put a stranger's activity under their name.
        await _auditService.LogAsync(
            success ? "Auth.LoginSucceeded" : "Auth.LoginFailed",
            "Auth",
            success
                ? $"{email} signed in."
                : $"Sign-in failed for {email}: {failureReason}",
            "AppUser",
            userId?.ToString(),
            actorUserId: success ? userId : null,
            actorUserName: success ? email : null);
    }

    /// <summary>A real BCrypt hash of a random value, used only to equalise timing on unknown emails.</summary>
    private const string DummyHash = "$2a$11$N9qo8uLOickgx2ZMRZoMyeIjZAgcfl7p92ldGxad68LJZdL17lhWy";
}
