using System.Collections.Generic;

namespace WhatsAppCampaignApi.Models.DTOs.Setup;

public class ContactStatusResponse
{
    public int Id { get; set; }
    public string Value { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
    public bool IsActive { get; set; }
    public bool IsSystem { get; set; }
    public int SortOrder { get; set; }
    /// <summary>How many contacts currently hold this status. Drives the delete guard.</summary>
    public int UsageCount { get; set; }
}

public class SaveContactStatusRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

/// <summary>Mirrors <see cref="ContactStatusResponse"/> — the two lookups are the same shape.</summary>
public class ContactTypeResponse
{
    public int Id { get; set; }
    public string Value { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
    public bool IsActive { get; set; }
    public bool IsSystem { get; set; }
    public int SortOrder { get; set; }
    /// <summary>How many contacts currently hold this type. Drives the delete guard.</summary>
    public int UsageCount { get; set; }
}

public class SaveContactTypeRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public class ContactSourceResponse
{
    public int Id { get; set; }
    public string Value { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
    public bool IsActive { get; set; }
    public bool IsSystem { get; set; }
    public int SortOrder { get; set; }
    public int UsageCount { get; set; }
}

public class SaveContactSourceRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public class LanguageResponse
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
    public bool IsActive { get; set; }
    public bool IsDefault { get; set; }
    public int SortOrder { get; set; }
    public int TranslationCount { get; set; }
}

public class SaveLanguageRequest
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool IsDefault { get; set; }
    public int SortOrder { get; set; }
}

public class TranslationResponse
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class SaveTranslationsRequest
{
    public List<TranslationEntry> Entries { get; set; } = new();
}

public class TranslationEntry
{
    public string Key { get; set; } = string.Empty;

    // Exempt from the global sanitizer, which trims and collapses whitespace runs — both of
    // which silently corrupt legitimate translated copy.
    [Helpers.SkipSanitization]
    public string Value { get; set; } = string.Empty;
}
