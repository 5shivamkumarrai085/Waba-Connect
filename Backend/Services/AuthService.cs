using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
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
    private readonly ISecurityStampCache _stampCache;

    public AuthService(
        AppDbContext dbContext,
        IConfiguration configuration,
        IPermissionResolver permissionResolver,
        ICurrentUserService currentUser,
        IAuditService auditService,
        ISecurityStampCache stampCache)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _permissionResolver = permissionResolver;
        _currentUser = currentUser;
        _auditService = auditService;
        _stampCache = stampCache;
    }

    private int MaxFailedAttempts => Math.Max(1, _configuration.GetValue("Auth:Lockout:MaxFailedAttempts", 5));
    private TimeSpan LockoutDuration => TimeSpan.FromMinutes(Math.Max(1, _configuration.GetValue("Auth:Lockout:DurationMinutes", 15)));
    private TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(Math.Clamp(_configuration.GetValue("Auth:Jwt:AccessTokenMinutes", 15), 1, 24 * 60));
    private TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(Math.Clamp(_configuration.GetValue("Auth:Jwt:RefreshTokenDays", 7), 1, 90));

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

        // Checked before the password: a locked account must not keep answering guesses, or the
        // lockout would only slow an attacker down instead of stopping them.
        if (user.LockoutEndAt is { } lockedUntil && lockedUntil > DateTime.UtcNow)
        {
            BCrypt.Net.BCrypt.Verify(request.Password ?? string.Empty, DummyHash);
            await RecordAttemptAsync(email, false, "Account temporarily locked.", user.Id);
            throw new InvalidOperationException(
                $"Too many failed sign-in attempts. Try again after {lockedUntil:HH:mm} UTC, or ask an administrator to unlock the account.");
        }

        if (!BCrypt.Net.BCrypt.Verify(request.Password ?? string.Empty, user.PasswordHash))
        {
            user.FailedLoginCount++;
            var locked = user.FailedLoginCount >= MaxFailedAttempts;
            if (locked)
            {
                user.LockoutEndAt = DateTime.UtcNow.Add(LockoutDuration);
                user.FailedLoginCount = 0;
            }

            await RecordAttemptAsync(email, false, locked ? "Invalid password; account locked." : "Invalid password.", user.Id);
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
        user.FailedLoginCount = 0;
        user.LockoutEndAt = null;
        await RecordAttemptAsync(email, true, null, user.Id);

        return await IssueTokensAsync(user, familyId: Guid.NewGuid());
    }

    public async Task<LoginResponse> RefreshAsync(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) throw new UnauthorizedAccessException("Invalid refresh token.");

        var hash = Hash(refreshToken);
        var stored = await _dbContext.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash)
            ?? throw new UnauthorizedAccessException("Invalid refresh token.");

        if (stored.RevokedAt is not null)
        {
            // A rotated token presented again: either a client bug or a stolen token being
            // replayed. Either way the whole chain is no longer trustworthy.
            await RevokeFamilyAsync(stored.FamilyId, "Refresh token reuse detected.");
            await _auditService.LogAsync("Auth.RefreshTokenReuse", "Security",
                "A previously used refresh token was presented again; all sessions from that sign-in were revoked.",
                nameof(AppUser), stored.UserId.ToString(), actorUserId: stored.UserId);
            throw new UnauthorizedAccessException("Invalid refresh token.");
        }

        if (stored.ExpiresAt <= DateTime.UtcNow) throw new UnauthorizedAccessException("Refresh token expired.");

        var user = await _dbContext.AppUsers
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == stored.UserId && !u.IsDeleted);

        if (user is null || !user.IsActive)
        {
            await RevokeFamilyAsync(stored.FamilyId, "Account deactivated or deleted.");
            throw new UnauthorizedAccessException("Invalid refresh token.");
        }

        stored.RevokedAt = DateTime.UtcNow;
        stored.RevokedReason = "Rotated";

        return await IssueTokensAsync(user, stored.FamilyId);
    }

    public async Task LogoutAsync(int userId, string? refreshToken)
    {
        var now = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            var hash = Hash(refreshToken);
            await _dbContext.RefreshTokens
                .Where(t => t.UserId == userId && t.TokenHash == hash && t.RevokedAt == null)
                .ExecuteUpdateAsync(u => u.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.RevokedReason, "Signed out"));
        }
        else
        {
            await _dbContext.RefreshTokens
                .Where(t => t.UserId == userId && t.RevokedAt == null)
                .ExecuteUpdateAsync(u => u.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.RevokedReason, "Signed out everywhere"));
        }

        await _auditService.LogAsync("Auth.Logout", "Auth", "Signed out.", nameof(AppUser), userId.ToString());
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

    public async Task<LoginResponse> ChangePasswordAsync(int userId, ChangePasswordRequest request)
    {
        if (request.NewPassword != request.ConfirmPassword)
            throw new InvalidOperationException("New password and confirmation do not match.");

        var user = await _dbContext.AppUsers
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted)
            ?? throw new KeyNotFoundException("User not found.");

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword ?? string.Empty, user.PasswordHash))
            throw new UnauthorizedAccessException("Current password is incorrect.");

        if (BCrypt.Net.BCrypt.Verify(request.NewPassword, user.PasswordHash))
            throw new InvalidOperationException("The new password must be different from the current one.");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.MustChangePassword = false;
        user.UpdatedAt = DateTime.UtcNow;

        // A new password ends every other session: whoever might have known the old one is out.
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await _dbContext.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.RevokedAt, DateTime.UtcNow).SetProperty(t => t.RevokedReason, "Password changed"));

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

        _permissionResolver.InvalidateUser(user.Id);
        _stampCache.Invalidate(user.Id);

        // Fresh tokens for this session, whose claims no longer say "must change password".
        return await IssueTokensAsync(user, Guid.NewGuid());
    }

    /// <summary>Creates the access token and a refresh token, and saves pending changes.</summary>
    private async Task<LoginResponse> IssueTokensAsync(AppUser user, Guid familyId)
    {
        var (token, expiresAt) = BuildToken(user);

        var refreshValue = WebEncodersBase64Url(RandomNumberGenerator.GetBytes(48));
        var refreshExpiresAt = DateTime.UtcNow.Add(RefreshTokenLifetime);

        _dbContext.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = Hash(refreshValue),
            FamilyId = familyId,
            ExpiresAt = refreshExpiresAt,
            CreatedByIp = _currentUser.IpAddress,
            UserAgent = Truncate(_currentUser.UserAgent, 500)
        });

        await _dbContext.SaveChangesAsync();

        return new LoginResponse
        {
            Token = token,
            ExpiresAt = expiresAt,
            RefreshToken = refreshValue,
            RefreshTokenExpiresAt = refreshExpiresAt,
            User = await BuildCurrentUserAsync(user)
        };
    }

    private Task RevokeFamilyAsync(Guid familyId, string reason) =>
        _dbContext.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.RevokedAt, DateTime.UtcNow).SetProperty(t => t.RevokedReason, reason));

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

        var expiresAt = DateTime.UtcNow.Add(AccessTokenLifetime);
        var isAdmin = user.IsAdministrator || (user.Role?.IsAdministrator ?? false);

        var claims = new List<Claim>
        {
            new(AuthClaims.UserId, user.Id.ToString()),
            new(AuthClaims.Email, user.Email),
            new(AuthClaims.Name, user.FullName),
            new(AuthClaims.IsAdministrator, isAdmin ? "true" : "false"),
            new(AuthClaims.MustChangePassword, user.MustChangePassword ? "true" : "false"),
            new(AuthClaims.SecurityStamp, user.SecurityStamp),
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

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string WebEncodersBase64Url(byte[] bytes) => Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(bytes);

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];

    /// <summary>A real BCrypt hash of a random value, used only to equalise timing on unknown emails.</summary>
    private const string DummyHash = "$2a$11$N9qo8uLOickgx2ZMRZoMyeIjZAgcfl7p92ldGxad68LJZdL17lhWy";
}
