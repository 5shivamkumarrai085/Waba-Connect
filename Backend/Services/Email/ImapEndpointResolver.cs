using MailKit.Net.Imap;
using MailKit.Security;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Models.Options;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>Everything needed to open one IMAP mailbox, after defaults and fallbacks.</summary>
public sealed record ImapEndpoint(
    string Host,
    int Port,
    SecureSocketOptions SocketOptions,
    string Username,
    string Password,
    bool AllowInvalidCertificate,
    bool IsDerivedFromSmtp);

/// <summary>
/// Single place that turns an <see cref="EmailConfiguration"/> into an IMAP endpoint, shared by the
/// polling worker and the "Test IMAP" button so the two can never disagree about which mailbox
/// they read.
/// </summary>
public static class ImapEndpointResolver
{
    /// <summary>
    /// Resolves the endpoint, or returns null with a reason when the connection cannot receive
    /// replies (no host and derivation disabled, or no usable credentials).
    /// </summary>
    public static ImapEndpoint? Resolve(
        EmailConfiguration configuration,
        InboundOptions options,
        Func<string?, string?> decrypt,
        out string? reason)
    {
        reason = null;

        var host = configuration.ImapHost?.Trim();
        var derived = false;
        if (string.IsNullOrWhiteSpace(host))
        {
            if (!options.DeriveImapFromSmtp || configuration.Provider != EmailProviderType.Smtp)
            {
                reason = "No IMAP host is configured for this connection.";
                return null;
            }

            host = DeriveHostFromSmtp(configuration.SmtpHost);
            derived = true;
            if (host is null)
            {
                reason = "No IMAP host is configured and none can be derived from the SMTP host.";
                return null;
            }
        }

        var username = !string.IsNullOrWhiteSpace(configuration.ImapUsername)
            ? configuration.ImapUsername.Trim()
            : configuration.SmtpUsername?.Trim();

        var password = decrypt(configuration.ImapPasswordEncrypted) ?? decrypt(configuration.SmtpPasswordEncrypted);

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            reason = "IMAP credentials are incomplete. Set an IMAP username and password, or SMTP credentials to fall back to.";
            return null;
        }

        // A derived endpoint uses the conventional implicit-TLS port; an explicit one honours
        // whatever the operator saved.
        var port = derived ? options.DefaultImapPort : configuration.ImapPort ?? options.DefaultImapPort;
        var security = derived ? SmtpSecurityMode.SslOnConnect : configuration.ImapSecurity;

        return new ImapEndpoint(
            host,
            port,
            ToSocketOptions(security),
            username,
            password,
            configuration.ImapAllowInvalidCertificate,
            derived);
    }

    /// <summary>
    /// <c>smtp.example.com</c> → <c>imap.example.com</c>; anything else (e.g. <c>mail.example.com</c>,
    /// the cPanel convention) is assumed to serve IMAP on the same name.
    /// </summary>
    public static string? DeriveHostFromSmtp(string? smtpHost)
    {
        if (string.IsNullOrWhiteSpace(smtpHost)) return null;
        var host = smtpHost.Trim().ToLowerInvariant();

        return host switch
        {
            "smtp.gmail.com" => "imap.gmail.com",
            "smtp.office365.com" or "smtp-mail.outlook.com" => "outlook.office365.com",
            _ when host.StartsWith("smtp.", StringComparison.Ordinal) => "imap." + host["smtp.".Length..],
            _ => host
        };
    }

    public static SecureSocketOptions ToSocketOptions(SmtpSecurityMode mode) => mode switch
    {
        SmtpSecurityMode.SslOnConnect => SecureSocketOptions.SslOnConnect,
        SmtpSecurityMode.StartTls => SecureSocketOptions.StartTls,
        SmtpSecurityMode.None => SecureSocketOptions.None,
        _ => SecureSocketOptions.SslOnConnect
    };

    /// <summary>Opens and authenticates a client. The caller owns disposal.</summary>
    public static async Task<ImapClient> ConnectAsync(
        ImapEndpoint endpoint, int timeoutSeconds, ILogger logger, CancellationToken ct)
    {
        var client = new ImapClient { Timeout = timeoutSeconds * 1000 };

        if (endpoint.AllowInvalidCertificate)
        {
            // Opt-in per connection only. The channel is still encrypted but the server's
            // identity is not verified, so this is logged on every connect.
            logger.LogWarning(
                "IMAP certificate validation is disabled for {Host}. Install a valid certificate and turn this off.",
                endpoint.Host);
            client.ServerCertificateValidationCallback = (_, _, _, _) => true;
        }

        try
        {
            await client.ConnectAsync(endpoint.Host, endpoint.Port, endpoint.SocketOptions, ct);
            await client.AuthenticateAsync(endpoint.Username, endpoint.Password, ct);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }
}
