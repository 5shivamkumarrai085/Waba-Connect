using System.ComponentModel.DataAnnotations;
using WhatsAppCampaignApi.Helpers;

namespace WhatsAppCampaignApi.Models.DTOs.Setup;

public class EmailTemplateResponse
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string BodyHtml { get; set; } = string.Empty;
    public string? TextBody { get; set; }
    public string? PreheaderText { get; set; }
    public string? Language { get; set; }
    public string? Description { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsSystem { get; set; }

    /// <summary>Comma-separated placeholder names the editor offers as insertable chips.</summary>
    public string? AvailableVariables { get; set; }

    /// <summary>
    /// The placeholders the body and subject actually reference, extracted from the content
    /// itself. This is what the campaign wizard builds its variable inputs from — the declared
    /// <see cref="AvailableVariables"/> list goes stale the moment somebody edits the body.
    /// </summary>
    public List<string> DetectedVariables { get; set; } = [];

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class SaveEmailTemplateRequest
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Stable lookup key. Ignored when updating: a template's key is what system notifications
    /// and existing campaigns resolve it by, so letting an edit change it would silently break
    /// whatever depended on the old one.
    /// </summary>
    [MaxLength(100)]
    public string? Key { get; set; }

    [Required, MaxLength(300)]
    public string Subject { get; set; } = string.Empty;

    // The global sanitizer deletes every HTML tag, which would flatten a formatted template to
    // plain text. The service applies allowlist-based sanitization instead.
    [Required]
    [SkipSanitization]
    public string BodyHtml { get; set; } = string.Empty;

    /// <summary>
    /// Optional plain-text alternative. Skips sanitization because it is not HTML — running the
    /// tag stripper over it would mangle any literal angle bracket.
    /// </summary>
    [SkipSanitization]
    public string? TextBody { get; set; }

    [MaxLength(300)]
    public string? PreheaderText { get; set; }

    [MaxLength(10)]
    public string? Language { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(500)]
    public string? AvailableVariables { get; set; }

    public bool IsEnabled { get; set; } = true;
}

public class EmailTemplatePreviewRequest
{
    /// <summary>Values to substitute.</summary>
    public Dictionary<string, string?>? Values { get; set; }

    /// <summary>
    /// Whether an omitted field may be filled with a plausible sample.
    ///
    /// <para>
    /// True for the template editor's "Preview with Sample Data", where the point is to see the
    /// shape of the mail. False — the default — everywhere that previews a real send: filling a
    /// missing {confirmation_link} with a working-looking example.com URL shows the operator a
    /// link that will not be in the delivered message.
    /// </para>
    /// </summary>
    public bool UseSampleData { get; set; }
}

public class EmailTemplatePreviewResponse
{
    public string Subject { get; set; } = string.Empty;
    public string BodyHtml { get; set; } = string.Empty;
    public string? TextBody { get; set; }

    /// <summary>
    /// Placeholders still present after substitution. Surfaced rather than blanked: a campaign
    /// that goes out reading "Dear {{first_name}}" is worse than one that refuses to send.
    /// </summary>
    public List<string> UnresolvedVariables { get; set; } = [];
}
