using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// One grantable action, e.g. "MessageBot.Create".
///
/// <para>
/// These rows are a catalogue, not a Feature × Capability cross-product. Capabilities are ragged
/// in practice — Connect Account has View/Connect/Disconnect, Message Bot has
/// View/Create/Edit/Delete/Clone, Template has View/LoadTemplate — so generating every
/// combination would render checkboxes that grant nothing. The permission matrix UI renders
/// exactly the rows the server returns, which means adding a feature later is a seed change
/// with no frontend edit.
/// </para>
/// </summary>
public class Permission
{
    [Key]
    public int Id { get; set; }

    /// <summary>Canonical "Feature.Capability" identifier used in [RequiresPermission].</summary>
    [Required, MaxLength(150)]
    public string Key { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Feature { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string Capability { get; set; } = string.Empty;

    /// <summary>Feature label shown as the matrix row heading, e.g. "Message Bot".</summary>
    [Required, MaxLength(150)]
    public string FeatureDisplayName { get; set; } = string.Empty;

    /// <summary>Capability label shown on the checkbox, e.g. "Load template".</summary>
    [Required, MaxLength(100)]
    public string CapabilityDisplayName { get; set; } = string.Empty;

    /// <summary>Groups features into matrix sections, e.g. "Marketing", "Setup".</summary>
    [MaxLength(100)]
    public string? GroupName { get; set; }

    public int SortOrder { get; set; }
}
