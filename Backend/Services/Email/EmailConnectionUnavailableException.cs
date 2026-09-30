namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// The email connection itself cannot be used right now: its stored credential cannot be read,
/// or it is switched off. A fault of the connection, not of any recipient, so the send path holds
/// the campaign until the connection is fixed instead of failing every recipient one by one.
/// </summary>
/// <remarks>
/// Derives from <see cref="InvalidOperationException"/> so every caller that already maps that to
/// a 409 (proofs, test emails, chat replies) keeps behaving exactly as before.
/// </remarks>
public sealed class EmailConnectionUnavailableException(int emailConfigurationId, string message, Exception? inner = null)
    : InvalidOperationException(message, inner)
{
    public int EmailConfigurationId { get; } = emailConfigurationId;
}
