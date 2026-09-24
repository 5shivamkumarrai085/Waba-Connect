using WhatsAppCampaignApi.Models.DTOs.Email;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// Manages email-channel connections: their provider credentials, sender identities and tests.
///
/// <para>
/// Deliberately separate from <c>IConnectionService</c>, which stays WhatsApp's. The two share the
/// <c>Connection</c> row as a label and nothing else — WhatsApp connects through an OAuth
/// handshake and syncs phone numbers, email stores a provider credential and verifies domains.
/// Folding them together would mean one service with two disjoint halves and a channel switch in
/// every method.
/// </para>
/// </summary>
public interface IEmailConnectionService
{
    Task<List<EmailConnectionResponse>> GetAllAsync(CancellationToken ct = default);

    Task<EmailConnectionResponse> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Step 1 of the wizard: creates the connection, an inactive provider configuration and the
    /// first sender identity in one transaction.
    /// </summary>
    Task<EmailConnectionResponse> CreateAsync(CreateEmailConnectionRequest request, CancellationToken ct = default);

    /// <summary>
    /// Step 2: stores provider credentials. Secrets left blank keep their stored values.
    /// </summary>
    Task<EmailConnectionResponse> SaveProviderAsync(int id, SaveEmailProviderRequest request, CancellationToken ct = default);

    /// <summary>Verifies stored credentials without sending anything.</summary>
    Task<EmailProviderTestResponse> TestConnectionAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Verifies credentials supplied in the request, so the wizard can test before saving.
    /// Secrets omitted from the request fall back to the referenced stored configuration.
    /// </summary>
    Task<EmailProviderTestResponse> TestUnsavedAsync(TestEmailProviderRequest request, CancellationToken ct = default);

    /// <summary>Sends one real email, to prove end-to-end delivery.</summary>
    Task<EmailProviderTestResponse> SendTestEmailAsync(int id, SendTestEmailRequest request, CancellationToken ct = default);

    Task<List<EmailSenderIdentityResponse>> GetSendersAsync(int id, CancellationToken ct = default);

    Task<EmailSenderIdentityResponse> AddSenderAsync(int id, SaveEmailSenderIdentityRequest request, CancellationToken ct = default);

    Task<EmailSenderIdentityResponse> UpdateSenderAsync(int id, int senderId, SaveEmailSenderIdentityRequest request, CancellationToken ct = default);

    Task DeleteSenderAsync(int id, int senderId, CancellationToken ct = default);

    /// <summary>
    /// Deactivates the configuration and clears its stored secrets, mirroring what WhatsApp's
    /// disconnect does — a disconnected connection should not leave a usable credential behind.
    /// </summary>
    Task DisconnectAsync(int id, CancellationToken ct = default);

    /// <summary>Stores IMAP credentials for inbound reply polling.</summary>
    Task<EmailConnectionResponse> SaveImapSettingsAsync(int id, SaveImapSettingsRequest request, CancellationToken ct = default);

    /// <summary>Tests IMAP connectivity without storing anything.</summary>
    Task<EmailProviderTestResponse> TestImapConnectionAsync(int id, CancellationToken ct = default);

    Task DeleteAsync(int id, CancellationToken ct = default);
}
