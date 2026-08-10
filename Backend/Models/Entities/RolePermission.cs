using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>Join row granting a <see cref="Permission"/> to a <see cref="Role"/>.</summary>
public class RolePermission
{
    [Key]
    public int Id { get; set; }

    public int RoleId { get; set; }
    public int PermissionId { get; set; }

    public virtual Role Role { get; set; } = null!;
    public virtual Permission Permission { get; set; } = null!;
}
