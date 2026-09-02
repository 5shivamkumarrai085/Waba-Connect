using WhatsAppCampaignApi.Models.DTOs.Setup;

namespace WhatsAppCampaignApi.Services.Interfaces;

/// <summary>
/// Reads and writes the OmniConnect settings surface.
///
/// The schema and the stored values travel together: a caller that can render the page can also
/// tell what is currently set, without a second round trip per section.
/// </summary>
public interface IOmniSettingsService
{
    /// <summary>
    /// Every section, its fields, their resolved option lists and the values currently stored.
    /// Secrets report only whether one exists — never what it is.
    /// </summary>
    Task<OmniSettingsSchemaDto> GetSchemaAsync();

    /// <summary>
    /// Validates and persists one section, returning the whole schema as it now stands.
    ///
    /// Returns everything rather than the saved section alone because sections are not independent
    /// — a status added elsewhere changes another section's options — and one response keeps the
    /// page consistent after a save.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The section key is not one we publish.</exception>
    /// <exception cref="InvalidOperationException">A field failed validation.</exception>
    Task<OmniSettingsSchemaDto> SaveSectionAsync(string sectionKey, SaveOmniSettingsRequest request);

    /// <summary>
    /// One setting's value, for the features that act on it. Secrets are decrypted here — this is
    /// the only path that returns one in plaintext, and it never leaves the server.
    /// </summary>
    Task<string?> GetValueAsync(string key);

    /// <summary>A boolean setting, with an explicit fallback for "never configured".</summary>
    Task<bool> GetFlagAsync(string key, bool fallback = false);

    /// <summary>A whole-number setting, or the fallback when unset or unparseable.</summary>
    Task<int> GetNumberAsync(string key, int fallback);

    /// <summary>
    /// A list setting — the "tags" and "multiselect" field types, stored as a JSON array.
    /// Returns an empty list when unset, never null, so callers can iterate unconditionally.
    /// </summary>
    Task<IReadOnlyList<string>> GetListAsync(string key);

    /// <summary>
    /// Drops the cached values.
    ///
    /// Saving a section does this automatically. It is exposed because the settings are cached
    /// process-wide while the rows they come from are shared, so a caller that changes an
    /// AppSetting by any other route has to be able to say so.
    /// </summary>
    void InvalidateCache();
}
