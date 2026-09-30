using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Models.Options;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// Picks the provider for a connection and hands it decrypted credentials.
///
/// <para>
/// The selection mechanism is the same one the codebase already uses for <c>IAiProvider</c>: every
/// implementation is registered against the interface, and the right one is chosen by name at
/// call time. That keeps "which provider does this connection send through" a stored setting
/// rather than a deployment decision, and means adding a third provider requires no change here
/// beyond registering it.
/// </para>
/// <para>
/// This is also the only place a stored secret is decrypted. Everything upstream — controllers,
/// DTOs, the campaign service — deals in configuration ids, so a plaintext credential exists only
/// inside the <see cref="EmailProviderContext"/> handed to a single provider call.
/// </para>
/// </summary>
public class EmailProviderFactory : IEmailProviderFactory
{
    private readonly AppDbContext _dbContext;
    private readonly IEncryptionService _encryption;
    private readonly IOptionsMonitor<EmailOptions> _options;
    private readonly IReadOnlyDictionary<string, IEmailProvider> _providers;
    private readonly ILogger<EmailProviderFactory> _logger;

    public EmailProviderFactory(
        AppDbContext dbContext,
        IEncryptionService encryption,
        IOptionsMonitor<EmailOptions> options,
        IEnumerable<IEmailProvider> providers,
        ILogger<EmailProviderFactory> logger)
    {
        _dbContext = dbContext;
        _encryption = encryption;
        _options = options;
        _logger = logger;

        // Case-insensitive, because the provider name round-trips through the database as a
        // string and through the API as JSON, and a casing mismatch would surface as "unknown
        // provider" on a value that is plainly correct.
        _providers = providers.ToDictionary(p => p.ProviderName, StringComparer.OrdinalIgnoreCase);
    }

    public IEmailProvider GetProvider(string providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            providerName = nameof(EmailProviderType.Smtp);
        }

        if (_providers.TryGetValue(providerName, out var provider)) return provider;

        // ArgumentException, which the exception middleware maps to 400 — this is a bad value,
        // not a server fault.
        throw new ArgumentException(
            $"Unknown email provider '{providerName}'. Available providers: {string.Join(", ", _providers.Keys)}.");
    }

    public async Task<(IEmailProvider Provider, EmailProviderContext Context)> ResolveAsync(
        int connectionId,
        CancellationToken ct = default)
    {
        var configuration = await _dbContext.EmailConfigurations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ConnectionId == connectionId, ct);

        if (configuration is null)
        {
            // KeyNotFoundException maps to 404, and callers in the send path treat it as
            // permanent: no number of retries will conjure a configuration.
            throw new KeyNotFoundException(
                $"Connection {connectionId} has no email configuration. Add an email connection before sending.");
        }

        return Resolve(configuration);
    }

    public async Task<(IEmailProvider Provider, EmailProviderContext Context)> ResolveByConfigurationAsync(
        int emailConfigurationId,
        CancellationToken ct = default)
    {
        var configuration = await _dbContext.EmailConfigurations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == emailConfigurationId, ct);

        if (configuration is null)
        {
            throw new KeyNotFoundException($"Email configuration {emailConfigurationId} not found.");
        }

        return Resolve(configuration);
    }

    private (IEmailProvider Provider, EmailProviderContext Context) Resolve(EmailConfiguration configuration)
    {
        if (!configuration.IsActive)
        {
            // InvalidOperationException maps to 409 Conflict, which is the honest answer: the
            // request is well-formed, the resource just is not in a state that allows it.
            throw new EmailConnectionUnavailableException(configuration.Id,
                $"Email configuration {configuration.Id} is inactive and cannot be used to send.");
        }

        var provider = GetProvider(configuration.Provider.ToString());

        return (provider, new EmailProviderContext
        {
            EmailConfigurationId = configuration.Id,
            ConnectionId = configuration.ConnectionId,
            Provider = configuration.Provider,
            SmtpHost = configuration.SmtpHost,
            SmtpPort = configuration.SmtpPort,
            SmtpSecurity = configuration.SmtpSecurity,
            SmtpUsername = configuration.SmtpUsername,
            SmtpPassword = Decrypt(configuration.SmtpPasswordEncrypted, configuration.Id, "SMTP password"),
            DefaultFromName = configuration.DefaultFromName,
            DefaultFromEmail = configuration.DefaultFromEmail,
            DefaultReplyTo = configuration.DefaultReplyTo
        });
    }

    /// <summary>
    /// Decrypts a stored secret, turning a failure into a clear error rather than a corrupt value.
    ///
    /// <para>
    /// The realistic cause of a decryption failure is the <c>Encryption:Key</c> having changed
    /// since the secret was written — after which every stored credential is unreadable. Returning
    /// garbage would surface as a baffling authentication error from the provider; saying so
    /// plainly points at the actual problem. The exception is deliberately not swallowed, because
    /// sending with no credential is worse than failing loudly.
    /// </para>
    /// </summary>
    private string? Decrypt(string? cipherText, int configurationId, string label)
    {
        if (string.IsNullOrWhiteSpace(cipherText)) return null;

        try
        {
            return _encryption.Decrypt(cipherText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Could not decrypt the {Label} for email configuration {ConfigId}. This usually means "
              + "Encryption:Key has changed since the secret was saved; the credential must be re-entered.",
                label, configurationId);

            throw new EmailConnectionUnavailableException(configurationId,
                $"The stored {label} for this email connection could not be decrypted. "
              + "Re-enter it on the connection to fix this.", ex);
        }
    }
}
