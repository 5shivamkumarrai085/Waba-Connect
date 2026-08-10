using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

public class ContactGroup
{
    public int Id { get; set; }
    
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;
    
    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Hex colour for the group badge, e.g. "#8B5CF6". Same contract as the other lookups.
    ///
    /// <para>
    /// Nullable because groups created before this column existed have no colour; the UI falls
    /// back to the previous name-hash palette for those, so nothing renders unstyled.
    /// </para>
    /// </summary>
    [MaxLength(9)]
    public string? Color { get; set; }
    
    public DateTime CreatedAt { get; set; }

    public ICollection<ContactGroupMember> Members { get; set; } = [];
}
