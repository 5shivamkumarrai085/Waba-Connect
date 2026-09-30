using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using WhatsAppCampaignApi.Models.Options;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>System mail from the platform's own SMTP account (see <see cref="SmtpOptions"/>).</summary>
public interface IEmailSender
{
    /// <summary>True when the deployment has SMTP configured; callers use it to decide whether to promise a mail.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Attempts delivery. Returns false rather than throwing on failure — a mail problem must never
    /// fail the operation that triggered it.
    /// </summary>
    Task<bool> SendAsync(string toAddress, string toName, string subject, string htmlBody, string textBody, CancellationToken ct = default);
}

/// <summary>
/// SMTP delivery over MailKit, mirroring the OmniConnect AuthService.
///
/// <para>
/// <b>Failure is never fatal.</b> Sending happens after the business operation has been saved (a
/// user created, a report built), and an unreachable mail server must not undo it. Failures are
/// logged and reported to the caller as <c>false</c>.
/// </para>
/// <para>
/// <b>Unconfigured is a normal state.</b> Without settings this reports IsEnabled = false and sends
/// nothing, so local development and any deployment that has not set up mail stay fully working.
/// </para>
/// <para>
/// A plain-text alternative accompanies every HTML body: some corporate mail clients strip HTML
/// entirely.
/// </para>
/// </summary>
public sealed class SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly SmtpOptions _smtp = options.Value;

    /// <summary>Implicit TLS port; every other port must negotiate STARTTLS.</summary>
    private const int ImplicitTlsPort = 465;

    public bool IsEnabled => _smtp.IsConfigured;

    public async Task<bool> SendAsync(
        string toAddress, string toName, string subject, string htmlBody, string textBody, CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            logger.LogInformation("Email not sent to {Recipient}: SMTP is not configured for this deployment.", toAddress);
            return false;
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_smtp.FromName, _smtp.ResolvedFromAddress));
            message.To.Add(new MailboxAddress(toName, toAddress));
            message.Subject = subject;
            message.Body = new BodyBuilder { HtmlBody = htmlBody, TextBody = textBody }.ToMessageBody();

            using var client = new SmtpClient();

            // 465 is implicit TLS; everything else negotiates STARTTLS and REQUIRES it to succeed.
            // SecureSocketOptions.Auto is avoided deliberately: it silently accepts a plaintext
            // session when a server declines to upgrade, which would put credentials on the wire.
            var secureOptions = _smtp.Port == ImplicitTlsPort
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTls;

            await client.ConnectAsync(_smtp.Host, _smtp.Port, secureOptions, ct);

            // Some internal relays accept mail from trusted hosts without authentication.
            if (!string.IsNullOrWhiteSpace(_smtp.Username))
            {
                await client.AuthenticateAsync(_smtp.Username, _smtp.Password, ct);
            }

            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);

            logger.LogInformation("Email '{Subject}' sent to {Recipient}.", subject, toAddress);
            return true;
        }
        catch (Exception ex)
        {
            // Intentionally broad: MailKit surfaces socket, TLS, authentication and protocol
            // exceptions, and the caller's response is the same for all of them.
            logger.LogError(ex, "Failed to send email '{Subject}' to {Recipient}.", subject, toAddress);
            return false;
        }
    }
}
