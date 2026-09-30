using System;
using System.Collections.Generic;
using WhatsAppCampaignApi.Helpers;

namespace WhatsAppCampaignApi.Models.DTOs.Auth;

/// <remarks>
/// Every password property here carries <see cref="SkipSanitizationAttribute"/>.
///
/// The global SanitizeInputFilter rewrites all writable string properties through
/// SanitizationHelper.SanitizeString, which strips anything matching &lt;...&gt; and collapses
/// whitespace runs. Applied to a password that silently weakens it — "a&lt;b&gt;c" becomes "ac" —
/// and because it mutates identically on both set and login, the account still "works", so the
/// weakening never surfaces as a bug report. Passwords must reach the hasher byte-for-byte.
/// </remarks>
public class LoginRequest
{
    public string Email { get; set; } = string.Empty;

    [SkipSanitization]
    public string Password { get; set; } = string.Empty;
}

public class LoginResponse
{
    /// <summary>Short-lived bearer token for API calls.</summary>
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }

    /// <summary>Single-use token for <c>POST /api/auth/refresh</c>. Rotated on every use.</summary>
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime RefreshTokenExpiresAt { get; set; }

    public CurrentUserResponse User { get; set; } = new();
}

public class RefreshTokenRequest
{
    [SkipSanitization]
    public string RefreshToken { get; set; } = string.Empty;
}

public class LogoutRequest
{
    /// <summary>The refresh token to revoke. Omitted means this user's every session.</summary>
    [SkipSanitization]
    public string? RefreshToken { get; set; }
}

public class ChangePasswordRequest
{
    [SkipSanitization]
    public string CurrentPassword { get; set; } = string.Empty;

    [SkipSanitization]
    public string NewPassword { get; set; } = string.Empty;

    [SkipSanitization]
    public string ConfirmPassword { get; set; } = string.Empty;
}

/// <summary>
/// What /api/auth/me returns. Permissions are resolved server-side and sent as a flat key list;
/// they are deliberately NOT packed into the JWT, so revoking access takes effect on the next
/// request rather than at token expiry.
/// </summary>
public class CurrentUserResponse
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string? LastName { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? ProfileImageUrl { get; set; }
    public string? DefaultLanguageCode { get; set; }

    public int? RoleId { get; set; }
    public string? RoleName { get; set; }

    public bool IsAdministrator { get; set; }
    public bool MustChangePassword { get; set; }
    public bool IsVerified { get; set; }

    /// <summary>Empty when <see cref="IsAdministrator"/> is true — admins bypass the check entirely.</summary>
    public List<string> Permissions { get; set; } = new();
}
