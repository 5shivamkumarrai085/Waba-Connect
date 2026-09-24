using WhatsAppCampaignApi.Models.DTOs.Setup;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// Reads and writes the email templates that every outgoing email renders from.
///
/// <para>
/// Introduced because EmailTemplatesController previously talked to the DbContext directly. That
/// was defensible while the feature was four seeded rows with an on/off switch; now that campaign
/// sending renders from the same rows, the rules about what may be edited, deleted and rendered
/// need one home rather than being spread across a controller and a worker.
/// </para>
/// </summary>
public interface IEmailTemplateService
{
    /// <param name="enabledOnly">
    /// True for the campaign template picker, which must not offer a disabled template.
    /// </param>
    Task<List<EmailTemplateResponse>> GetAllAsync(bool enabledOnly = false, CancellationToken ct = default);

    Task<EmailTemplateResponse> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>Looks a template up by its stable key, the way a system notification does.</summary>
    Task<EmailTemplateResponse?> GetByKeyAsync(string key, CancellationToken ct = default);

    Task<EmailTemplateResponse> CreateAsync(SaveEmailTemplateRequest request, CancellationToken ct = default);

    Task<EmailTemplateResponse> UpdateAsync(int id, SaveEmailTemplateRequest request, CancellationToken ct = default);

    Task<EmailTemplateResponse> ToggleAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Deletes a template. Refuses for seeded system templates, and for any template a campaign
    /// references — a sent campaign must stay able to explain what it sent.
    /// </summary>
    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Renders a template with sample or supplied values, for the editor's preview pane.
    /// Reports any placeholder left unresolved rather than hiding it.
    /// </summary>
    /// <param name="useSampleData">
    /// Whether an unsupplied field may be filled with a plausible stand-in. Off by default, so a
    /// preview of a real send never shows content the send will not produce.
    /// </param>
    Task<EmailTemplatePreviewResponse> PreviewAsync(
        int id,
        IReadOnlyDictionary<string, string?>? values = null,
        bool useSampleData = false,
        CancellationToken ct = default);
}
