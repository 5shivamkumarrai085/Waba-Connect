using System;
using System.ComponentModel.DataAnnotations;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// One stored value for one configurable setting.
///
/// <para>
/// A key/value row rather than a column per setting. The settings surface grows by a field at a
/// time and always will — a column per field would mean a migration for every checkbox, and a
/// wide table of mostly-null columns that no query ever filters on. What each key *means* — its
/// type, label, validation and options — lives in <c>OmniSettingsCatalog</c>, which is the thing
/// worth version-controlling; this table only remembers what was chosen.
/// </para>
/// <para>
/// Values are stored as text and parsed against the catalogue's declared type on read. That keeps
/// one storage shape for booleans, numbers, lists and secrets alike, and means an unrecognised or
/// removed key is inert rather than a deserialisation failure.
/// </para>
/// </summary>
public class AppSetting
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Catalogue key, e.g. "autoLead.enabled". Unique — one stored value per setting.
    /// </summary>
    [Required, MaxLength(150)]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// The raw stored value. Uncapped because a webhook event subscription is a list and a prompt
    /// is prose; the catalogue bounds what any individual field may contain.
    ///
    /// Secrets are stored here encrypted, never in plaintext — see <c>OmniSettingsService</c>.
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// Who last changed it and when. Settings change behaviour for everyone, so the row carries
    /// its own provenance rather than relying on the audit log alone — the audit trail can be
    /// filtered or aged out, and "who turned this on" is the first question asked about a setting.
    /// </summary>
    public int? UpdatedByUserId { get; set; }

    [MaxLength(200)]
    public string? UpdatedByName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
