using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// Per-user permission override. Only consulted when <see cref="AppUser.UsesCustomPermissions"/>
/// is true, in which case these rows REPLACE the role's grants rather than adding to them —
/// so unchecking a box the role granted actually revokes it, which is the only sane reading
/// of a security checkbox.
/// </summary>
public class UserPermission
{
    [Key]
    public int Id { get; set; }

    public int UserId { get; set; }
    public int PermissionId { get; set; }

    public virtual AppUser User { get; set; } = null!;
    public virtual Permission Permission { get; set; } = null!;
}
