using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// A named bundle of permissions. Roles are the normal way to grant access; per-user overrides
/// (<see cref="AppUser.UsesCustomPermissions"/>) exist but are the exception.
/// </summary>
public class Role
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Seeded roles that the product depends on. Deleting one is refused (409) because users
    /// would silently lose all access; renaming is allowed.
    /// </summary>
    public bool IsSystem { get; set; }

    /// <summary>
    /// Grants unrestricted access. Only Super Admin ships with this set. Like
    /// <see cref="AppUser.IsAdministrator"/>, it short-circuits rather than expanding to rows.
    /// </summary>
    public bool IsAdministrator { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
    public virtual ICollection<AppUser> Users { get; set; } = new List<AppUser>();
}
