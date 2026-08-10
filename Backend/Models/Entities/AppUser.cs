using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// An internal staff account — the person who logs into OmniConnect. Deliberately NOT the same
/// thing as a <see cref="Contact"/>, which is a customer the business messages on WhatsApp.
///
/// Named AppUser rather than User because "User" collides with ClaimsPrincipal-adjacent naming
/// and with the pre-existing UserConnection entity.
///
/// <para>
/// Host-app integration: <see cref="ExternalUserId"/> is reserved for when a host application
/// supplies identity via Module Federation. Local records can then be matched to host users
/// without changing any foreign key in the rest of the schema.
/// </para>
/// </summary>
public class AppUser
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? LastName { get; set; }

    [Required, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(30)]
    public string? PhoneNumber { get; set; }

    /// <summary>Country dial code kept separate so the phone input can round-trip its selector.</summary>
    [MaxLength(10)]
    public string? DialCode { get; set; }

    [MaxLength(500)]
    public string? ProfileImageUrl { get; set; }

    /// <summary>BCrypt hash. The plaintext password is never stored or logged.</summary>
    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Language code (e.g. "en") matching Languages.Code. Not an FK — codes are stable.</summary>
    [MaxLength(10)]
    public string? DefaultLanguageCode { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Email-verified flag. Surfaced as the "Is Verified User" toggle on the user form.</summary>
    public bool IsVerified { get; set; }

    /// <summary>
    /// Forces a password reset at next login. Set when the super admin is seeded with the
    /// documented default password, and whenever an admin resets someone's password.
    /// </summary>
    public bool MustChangePassword { get; set; }

    /// <summary>
    /// Records the "Send Welcome Email" intent from the user form. No mail is sent — this
    /// build ships email template management only, with no SMTP configured.
    /// </summary>
    public bool SendWelcomeEmail { get; set; }

    public int? RoleId { get; set; }

    /// <summary>
    /// Master "Administrator Access" toggle. When true the user bypasses every permission check
    /// and <see cref="RoleId"/>/<see cref="UserPermissions"/> are ignored. Admin is never expanded
    /// into permission rows, so permissions added to the catalogue later reach admins automatically.
    /// </summary>
    public bool IsAdministrator { get; set; }

    /// <summary>
    /// When true the effective permission set comes from <see cref="UserPermissions"/> rather than
    /// the assigned role. Flipped by the UI the moment an admin edits the matrix away from the
    /// role's preset; reset to false when the Role select changes.
    /// </summary>
    public bool UsesCustomPermissions { get; set; }

    /// <summary>Reserved for host-app identity handover. Unused while auth is local.</summary>
    [MaxLength(150)]
    public string? ExternalUserId { get; set; }

    public DateTime? LastLoginAt { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public virtual Role? Role { get; set; }
    public virtual ICollection<UserPermission> UserPermissions { get; set; } = new List<UserPermission>();

    public string FullName => string.IsNullOrWhiteSpace(LastName) ? FirstName : $"{FirstName} {LastName}";
}
