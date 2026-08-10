using WhatsAppCampaignApi.Helpers;

namespace WhatsAppCampaignApi.Models.DTOs.Setup;

public class EmailTemplateResponse
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string BodyHtml { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool IsSystem { get; set; }
    /// <summary>Comma-separated placeholder names the editor offers as insertable chips.</summary>
    public string? AvailableVariables { get; set; }
}

public class SaveEmailTemplateRequest
{
    public string Name { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;

    // The global sanitizer deletes every HTML tag, which would flatten a formatted template to
    // plain text. The controller applies allowlist-based sanitization instead.
    [SkipSanitization]
    public string BodyHtml { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;
}
