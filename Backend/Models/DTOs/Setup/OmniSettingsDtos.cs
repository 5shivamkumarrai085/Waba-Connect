namespace WhatsAppCampaignApi.Models.DTOs.Setup;

/// <summary>
/// The whole settings surface: every section, the fields in it, and the values currently stored.
///
/// <para>
/// Served as one document so the page renders from data rather than from a layout the client
/// hardcodes. A field added to the catalogue appears in the UI with no frontend change, and — more
/// importantly — a field the client renders is one the server knows how to validate and persist,
/// because they are the same declaration.
/// </para>
/// </summary>
public class OmniSettingsSchemaDto
{
    public List<OmniSettingsSectionDto> Sections { get; set; } = new();
}

/// <summary>One page of the settings rail.</summary>
public class OmniSettingsSectionDto
{
    /// <summary>Stable key, also the URL slug — e.g. "whatsapp-auto-lead".</summary>
    public string Key { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Lucide icon name, resolved by the client against its own icon set.</summary>
    public string Icon { get; set; } = string.Empty;

    public List<OmniSettingsFieldDto> Fields { get; set; } = new();

    /// <summary>Callout boxes rendered under the fields. Empty for most sections.</summary>
    public List<OmniSettingsNoteDto> Notes { get; set; } = new();
}

/// <summary>
/// One configurable field: what it is, what it is called, and what it may hold.
/// </summary>
public class OmniSettingsFieldDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// toggle | text | password | number | select | multiselect | tags.
    /// The client has a renderer per type; anything unknown is skipped rather than crashing the page.
    /// </summary>
    public string Type { get; set; } = "text";

    /// <summary>Explanatory line under the label. Null for most fields.</summary>
    public string? Helper { get; set; }

    public string? Placeholder { get; set; }

    /// <summary>Suffix rendered inside the control — "Hours", "Days", "seconds".</summary>
    public string? Unit { get; set; }

    public bool Required { get; set; }

    /// <summary>
    /// Makes <see cref="Required"/> conditional on another field being on.
    ///
    /// The OpenAI key is mandatory only once OpenAI is switched on; demanding it from an account
    /// that has not enabled the feature would block saving an unrelated change on the same page.
    /// </summary>
    public string? RequiredWhenKey { get; set; }

    /// <summary>
    /// Where a select/multiselect gets its options: "leadStatuses", "users", "aiModels", … The
    /// client never carries the list; see <c>OmniSettingsService.ResolveOptionsAsync</c>.
    /// </summary>
    public string? OptionSource { get; set; }

    /// <summary>Resolved options for this field, when it has an option source.</summary>
    public List<OmniSettingsOptionDto> Options { get; set; } = new();

    /// <summary>
    /// The stored value, as the client should render it. Booleans arrive as true/false, numbers as
    /// numbers, lists as arrays of strings.
    ///
    /// A secret never arrives here — see <see cref="IsSecret"/> and <see cref="HasValue"/>.
    /// </summary>
    public object? Value { get; set; }

    /// <summary>
    /// True for a write-only credential. Its value is stored encrypted and is never returned, so
    /// the page cannot leak it to anyone who can open the settings screen or read a HAR file.
    /// </summary>
    public bool IsSecret { get; set; }

    /// <summary>
    /// For a secret: whether one is currently stored. Lets the UI say "a key is saved" and accept
    /// a replacement, without ever showing what is saved.
    /// </summary>
    public bool HasValue { get; set; }

    public int? Min { get; set; }
    public int? Max { get; set; }
}

public class OmniSettingsOptionDto
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

/// <summary>A callout under a section's fields.</summary>
public class OmniSettingsNoteDto
{
    /// <summary>warning | info. Drives the colour only.</summary>
    public string Tone { get; set; } = "info";

    public string Text { get; set; } = string.Empty;
}

/// <summary>What the client posts back when saving one section.</summary>
public class SaveOmniSettingsRequest
{
    /// <summary>
    /// Field key to value. Only the keys belonging to the section being saved are honoured —
    /// anything else is ignored rather than written, so one section cannot rewrite another's
    /// settings by posting extra keys.
    /// </summary>
    public Dictionary<string, object?> Values { get; set; } = new();
}
