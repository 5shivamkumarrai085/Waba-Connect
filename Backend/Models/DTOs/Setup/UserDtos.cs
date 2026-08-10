using System;
using System.Collections.Generic;
using WhatsAppCampaignApi.Helpers;

namespace WhatsAppCampaignApi.Models.DTOs.Setup;

public class UserListItemResponse
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string? LastName { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? ProfileImageUrl { get; set; }
    public int? RoleId { get; set; }
    public string? RoleName { get; set; }
    public bool IsActive { get; set; }
    public bool IsVerified { get; set; }
    public bool IsAdministrator { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

public class UserDetailResponse : UserListItemResponse
{
    public string? DialCode { get; set; }
    public string? DefaultLanguageCode { get; set; }
    public bool SendWelcomeEmail { get; set; }
    public bool MustChangePassword { get; set; }
    public bool UsesCustomPermissions { get; set; }

    /// <summary>
    /// The matrix state. When <see cref="UsesCustomPermissions"/> is false these are the role's
    /// grants, shown so the form can display the preset the role implies.
    /// </summary>
    public List<string> PermissionKeys { get; set; } = new();
}

public class CreateUserRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string? LastName { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? DialCode { get; set; }
    public string? ProfileImageUrl { get; set; }
    public string? DefaultLanguageCode { get; set; }

    // See AuthDtos for why passwords must bypass the global input sanitizer: it strips
    // <...> and collapses whitespace, silently weakening the password on the way in.
    [SkipSanitization]
    public string Password { get; set; } = string.Empty;

    [SkipSanitization]
    public string ConfirmPassword { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    public bool IsVerified { get; set; }

    /// <summary>Recorded only. This build ships email template management with no SMTP.</summary>
    public bool SendWelcomeEmail { get; set; }

    public bool IsAdministrator { get; set; }
    public int? RoleId { get; set; }

    /// <summary>When true, <see cref="PermissionKeys"/> replaces the role's grants for this user.</summary>
    public bool UsesCustomPermissions { get; set; }
    public List<string> PermissionKeys { get; set; } = new();
}

public class UpdateUserRequest : CreateUserRequest
{
    /// <summary>
    /// Optional on update. Left blank the existing password is kept — an admin editing someone's
    /// phone number shouldn't have to know or reset their password.
    /// </summary>
    public new string? Password { get; set; }
    public new string? ConfirmPassword { get; set; }
}

public class UserDashboardResponse
{
    public int TotalUsers { get; set; }
    public int ActiveUsers { get; set; }
    public int InactiveUsers { get; set; }
    public int AdministratorCount { get; set; }
}
