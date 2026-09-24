namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// One way of actually putting an email on the wire.
///
/// <para>
/// Implementations are registered against this interface together and selected by
/// <see cref="ProviderName"/>, the same pattern the codebase already uses for <c>IAiProvider</c>
/// with Groq and OpenAI. That is what makes the provider a per-connection setting rather than a
/// deployment decision, and what lets a third implementation — Gmail, Postmark, whatever a client
/// requires — be added without touching the workers, the queue or the campaign service.
/// </para>
/// <para>
/// Implementations must not throw for ordinary send failures. A provider that throws forces every
/// caller to decide, from an exception type, whether retrying is sensible — which is exactly the
/// judgement the provider is best placed to make. Return <see cref="EmailSendResult.Failed"/>
/// with an honest <c>isTransient</c> instead.
/// </para>
/// </summary>
public interface IEmailProvider
{
    /// <summary>Matches <c>EmailConfiguration.Provider</c>, e.g. "AmazonSes" or "Smtp".</summary>
    string ProviderName { get; }

    EmailProviderCapabilities Capabilities { get; }

    /// <summary>
    /// Sends one message. Returns a result rather than throwing, including for failures — see the
    /// note on the interface.
    /// </summary>
    Task<EmailSendResult> SendAsync(
        EmailMessage message,
        EmailProviderContext context,
        CancellationToken ct = default);

    /// <summary>
    /// Verifies that the configured credentials and endpoint work, without sending anything.
    ///
    /// <para>
    /// Separate from sending a test email on purpose: this answers "are these credentials valid"
    /// with no cost and no reputation impact, which is what an operator wants while filling in
    /// the connection form. Sending real mail to a real inbox is a second, explicit step.
    /// </para>
    /// </summary>
    Task<EmailProviderTestResult> TestConnectionAsync(
        EmailProviderContext context,
        CancellationToken ct = default);
}

/// <summary>
/// Resolves the right <see cref="IEmailProvider"/> and its decrypted settings for a connection.
/// </summary>
public interface IEmailProviderFactory
{
    /// <summary>
    /// Looks up the connection's email configuration, decrypts its credentials and pairs it with
    /// the matching provider. Throws when the connection has no usable email configuration —
    /// callers treat that as a permanent failure, since no amount of retrying will supply one.
    /// </summary>
    Task<(IEmailProvider Provider, EmailProviderContext Context)> ResolveAsync(
        int connectionId,
        CancellationToken ct = default);

    /// <summary>Resolves by configuration id, for the test endpoints on the connection wizard.</summary>
    Task<(IEmailProvider Provider, EmailProviderContext Context)> ResolveByConfigurationAsync(
        int emailConfigurationId,
        CancellationToken ct = default);

    /// <summary>
    /// Pairs an unsaved configuration with its provider, so the wizard can test credentials
    /// before committing them. Without this, an operator would have to save a possibly-wrong
    /// secret in order to find out whether it works.
    /// </summary>
    IEmailProvider GetProvider(string providerName);
}
