namespace WhatsAppCampaignApi.Models.Options;

/// <summary>
/// The platform's own SMTP account, for system mail (welcome emails, scheduled reports that name
/// no sender). Supplied entirely from configuration — the <c>Smtp</c> section, set through
/// environment variables (<c>Smtp__Host</c>, …) or the git-ignored local secrets file, never
/// committed values. Mirrors the OmniConnect AuthService.
///
/// <para>
/// Provider-agnostic on purpose: the same settings drive Google Workspace, Microsoft 365, cPanel
/// hosting or a corporate relay, so choosing or changing a mail provider never needs a code
/// change.
/// </para>
/// <para>
/// Unconfigured is a supported state rather than a startup failure: <see cref="IsConfigured"/> is
/// false, nothing is sent, and the application runs normally. Campaign, chat and proof mail do not
/// use this account at all — each email connection has its own SMTP settings.
/// </para>
/// </summary>
public class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; set; } = string.Empty;

    /// <summary>587 (STARTTLS) suits almost every provider; 465 selects implicit TLS.</summary>
    public int Port { get; set; } = 587;

    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>Envelope sender. Falls back to <see cref="Username"/>, which is correct for most providers.</summary>
    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = "OmniConnect";

    /// <summary>
    /// Absolute base URL of the frontend, for links in system mail (e.g. https://app.example.com).
    /// Configured rather than taken from the request, because the Host header is attacker-controllable.
    /// Optional: mail that needs a link says so when it is missing.
    /// </summary>
    public string AppBaseUrl { get; set; } = string.Empty;

    public string ResolvedFromAddress => string.IsNullOrWhiteSpace(FromAddress) ? Username : FromAddress;

    /// <summary>Mail is attempted only when there is a host and a sender address.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host)
        && !string.IsNullOrWhiteSpace(ResolvedFromAddress);
}
