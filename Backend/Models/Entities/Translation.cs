using System.ComponentModel.DataAnnotations;
using WhatsAppCampaignApi.Helpers;

namespace WhatsAppCampaignApi.Models.Entities;

/// <summary>
/// One translated string for a language.
///
/// <para>
/// This is the storage and API layer only. The app's own interface text is not yet wired to
/// read from here — extracting every hardcoded string across the UI is a separate project, and
/// was scoped out deliberately. What exists is the durable half: keys and values can be
/// managed now and consumed whenever that work happens.
/// </para>
/// </summary>
public class Translation
{
    [Key]
    public int Id { get; set; }

    public int LanguageId { get; set; }

    /// <summary>Dotted lookup key, e.g. "contacts.title".</summary>
    [Required, MaxLength(200)]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// The translated text. Exempt from the global input sanitizer, which collapses whitespace
    /// runs and strips anything resembling a tag — both of which corrupt legitimate copy.
    /// </summary>
    [SkipSanitization]
    public string Value { get; set; } = string.Empty;

    public virtual Language Language { get; set; } = null!;
}
