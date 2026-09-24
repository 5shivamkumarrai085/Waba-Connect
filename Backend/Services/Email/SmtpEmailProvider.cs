using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Models.Options;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// Plain SMTP, via MailKit.
///
/// <para>
/// The fallback path, and the reason the provider abstraction earns its keep: it makes any
/// mailbox a usable sender without new code — a Google Workspace relay, Microsoft 365, an
/// on-premise relay, or a local capture server like MailHog during development.
/// </para>
/// <para>
/// What it cannot do is report what happened afterwards. SMTP tells us only whether the next hop
/// accepted the message; delivery, bounces, complaints, opens and clicks are all invisible, which
/// is why <see cref="Capabilities"/> says so and the UI degrades rather than showing tracking
/// that will never populate.
/// </para>
/// </summary>
public class SmtpEmailProvider : IEmailProvider
{
    /// <summary>
    /// Conservative: most relays cap well below SES, and exceeding a relay's limit typically
    /// produces a mid-transfer disconnect rather than a clear rejection.
    /// </summary>
    private const int SmtpMaxMessageBytes = 25 * 1024 * 1024;

    private readonly IMimeMessageBuilder _mimeBuilder;
    private readonly IOptionsMonitor<EmailOptions> _options;
    private readonly ILogger<SmtpEmailProvider> _logger;

    public SmtpEmailProvider(
        IMimeMessageBuilder mimeBuilder,
        IOptionsMonitor<EmailOptions> options,
        ILogger<SmtpEmailProvider> logger)
    {
        _mimeBuilder = mimeBuilder;
        _options = options;
        _logger = logger;
    }

    public string ProviderName => nameof(EmailProviderType.Smtp);

    public EmailProviderCapabilities Capabilities => new()
    {
        // All false, and honestly so. SMTP has no event feedback channel, no DKIM provisioning
        // API and no suppression list — claiming otherwise would leave operators waiting for
        // delivery reports that cannot arrive.
        SupportsEventWebhooks = false,
        SupportsDkimProvisioning = false,
        SupportsSuppressionApi = false,
        SupportsInboundReceiving = false,
        MaxMessageBytes = SmtpMaxMessageBytes
    };

    public async Task<EmailSendResult> SendAsync(
        EmailMessage message,
        EmailProviderContext context,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(context.SmtpHost))
        {
            return EmailSendResult.Failed(
                "No SMTP host is configured for this email connection.",
                isTransient: false, errorCode: "NotConfigured");
        }

        // A fresh client per send. MailKit's SmtpClient is explicitly not thread-safe, and the
        // dispatch worker runs several sends concurrently — sharing one would interleave
        // protocol commands and corrupt the session. Connection reuse would be a real saving,
        // but it needs a pool keyed by configuration with its own lifetime handling, and
        // correctness comes first.
        using var client = new SmtpClient
        {
            Timeout = _options.CurrentValue.Dispatch.SmtpTimeoutSeconds * 1000
        };

        try
        {
            var mime = _mimeBuilder.Build(message);

            var port = context.SmtpPort ?? DefaultPortFor(context.SmtpSecurity);
            var socketOptions = ResolveSocketOptions(context.SmtpSecurity, port);

            if (socketOptions == SecureSocketOptions.None)
            {
                _logger.LogWarning(
                    "SMTP connection {ConfigId} is sending without TLS. Credentials and message content are "
                  + "transmitted in clear text.",
                    context.EmailConfigurationId);
            }

            await client.ConnectAsync(context.SmtpHost, port, socketOptions, ct);

            if (!string.IsNullOrWhiteSpace(context.SmtpUsername))
            {
                await client.AuthenticateAsync(context.SmtpUsername, context.SmtpPassword ?? string.Empty, ct);
            }

            // Envelope recipients passed explicitly, which is what makes Bcc work without the
            // MIME carrying a Bcc header — see the note in MimeMessageBuilder.
            var recipients = message.AllRecipients
                .Select(a => new MailboxAddress(a.DisplayName ?? string.Empty, a.Address))
                .ToList();

            if (recipients.Count == 0)
            {
                return EmailSendResult.Failed("The message has no recipients.", isTransient: false,
                    errorCode: "NoRecipients");
            }

            var sender = new MailboxAddress(message.From.DisplayName ?? string.Empty, message.From.Address);

            // MailKit returns the server's acceptance response, which is where most relays put
            // their own queue id.
            var response = await client.SendAsync(mime, sender, recipients, ct);
            await client.DisconnectAsync(quit: true, ct);

            // SMTP has no message id of its own. The Message-ID we generated is used instead,
            // which keeps the rest of the pipeline uniform — and unlike a provider id, it is
            // also what an inbound reply will reference.
            var messageId = mime.MessageId ?? ExtractQueueId(response) ?? Guid.NewGuid().ToString("N");
            return EmailSendResult.Sent(messageId);
        }
        catch (AuthenticationException ex)
        {
            // Wrong credentials. Permanent: retrying the same password four more times can only
            // fail, and on some providers repeated failures lock the account.
            return EmailSendResult.Failed(
                $"SMTP authentication failed: {ex.Message}", isTransient: false, errorCode: "AuthenticationFailed");
        }
        catch (SmtpCommandException ex)
        {
            // The server made a decision, and its status code says whether it is worth retrying.
            // 4xx is a temporary refusal — greylisting, mailbox busy, over quota; 5xx is
            // permanent — no such user, message refused.
            var transient = ex.StatusCode is >= SmtpStatusCode.ServiceNotAvailable and < (SmtpStatusCode)500;

            return EmailSendResult.Failed(
                $"SMTP rejected the message ({(int)ex.StatusCode} {ex.ErrorCode}): {ex.Message}",
                transient, ex.StatusCode.ToString());
        }
        catch (SmtpProtocolException ex)
        {
            // The conversation broke down mid-flight. The message is fine, so retry.
            return EmailSendResult.Failed(
                $"SMTP protocol error: {ex.Message}", isTransient: true, errorCode: "ProtocolError");
        }
        catch (SslHandshakeException ex)
        {
            // Almost always a misconfiguration — wrong port for the chosen security mode, or an
            // untrusted certificate. Permanent, because it will fail identically every time.
            return EmailSendResult.Failed(
                $"TLS handshake failed: {ex.Message}. Check the port and security mode for this connection.",
                isTransient: false, errorCode: "SslHandshakeFailed");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return EmailSendResult.Failed("Send cancelled during shutdown.", isTransient: true);
        }
        catch (Exception ex)
        {
            // Networking: unreachable host, DNS failure, timeout. The message is fine.
            _logger.LogError(ex, "Unexpected failure sending via SMTP host {Host}.", context.SmtpHost);
            return EmailSendResult.Failed(
                DescribeConnectFailure(ex, context.SmtpHost, context.SmtpPort, context.SmtpSecurity),
                isTransient: true, errorCode: ex.GetType().Name);
        }
        finally
        {
            // Best effort: if the send already threw, the socket may be unusable, and failing to
            // close it cleanly must not replace the real error with a less useful one.
            if (client.IsConnected)
            {
                try
                {
                    await client.DisconnectAsync(quit: true, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Ignoring error while disconnecting from SMTP host.");
                }
            }
        }
    }

    public async Task<EmailProviderTestResult> TestConnectionAsync(
        EmailProviderContext context,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(context.SmtpHost))
        {
            return EmailProviderTestResult.Fail("No SMTP host is configured.");
        }

        // Bounded explicitly. This is the call an operator sits and waits on, and MailKit's
        // 120-second default meant a wrong port/mode pair looked like the product hanging.
        using var client = new SmtpClient
        {
            Timeout = _options.CurrentValue.Dispatch.SmtpTimeoutSeconds * 1000
        };

        try
        {
            var port = context.SmtpPort ?? DefaultPortFor(context.SmtpSecurity);
            var socketOptions = ResolveSocketOptions(context.SmtpSecurity, port);

            // Connect and authenticate, but send nothing. That proves the host, port, TLS mode
            // and credentials, which is everything an operator can get wrong on the form, at no
            // cost to anyone's inbox.
            await client.ConnectAsync(context.SmtpHost, port, socketOptions, ct);

            var details = new Dictionary<string, string>
            {
                ["host"] = context.SmtpHost,
                ["port"] = port.ToString(),
                ["security"] = context.SmtpSecurity.ToString(),
                ["tlsActive"] = client.IsSecure.ToString()
            };

            if (!string.IsNullOrWhiteSpace(context.SmtpUsername))
            {
                await client.AuthenticateAsync(context.SmtpUsername, context.SmtpPassword ?? string.Empty, ct);
                details["authenticated"] = "true";
            }
            else
            {
                // Worth stating rather than passing silently: an unauthenticated relay that
                // accepts our mail will usually accept anyone's.
                details["authenticated"] = "false (no username configured)";
            }

            if (client.MaxSize > 0) details["serverMaxMessageSize"] = $"{client.MaxSize / 1024 / 1024}MB";

            await client.DisconnectAsync(quit: true, ct);

            var warning = socketOptions == SecureSocketOptions.None
                ? " Warning: this connection is not encrypted."
                : string.Empty;

            return EmailProviderTestResult.Ok(
                $"Connected to {context.SmtpHost}:{port}.{warning}", details);
        }
        catch (AuthenticationException ex)
        {
            return EmailProviderTestResult.Fail(
                $"Connected to {context.SmtpHost}, but authentication failed: {ex.Message}");
        }
        catch (SslHandshakeException ex)
        {
            return EmailProviderTestResult.Fail(
                $"TLS handshake failed: {ex.Message}. The port and security mode are most likely mismatched — "
              + "587 expects STARTTLS and 465 expects implicit SSL.");
        }
        catch (Exception ex)
        {
            // Through the same describer as the send path, so a timeout on the wizard's Test
            // button names the port it actually tried. "Could not connect to mail.example.com"
            // leaves the operator with nothing to check.
            return EmailProviderTestResult.Fail(
                DescribeConnectFailure(ex, context.SmtpHost, context.SmtpPort, context.SmtpSecurity));
        }
        finally
        {
            if (client.IsConnected)
            {
                try
                {
                    await client.DisconnectAsync(quit: true, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Ignoring error while disconnecting from SMTP host.");
                }
            }
        }
    }

    /// <summary>
    /// Picks the socket mode, letting the port overrule a contradictory security setting.
    ///
    /// <para>
    /// Port 465 is implicit TLS: the server expects a TLS handshake as the very first thing on
    /// the socket. Attempting STARTTLS there is not an error the server can report — it waits for
    /// a ClientHello while MailKit waits for a plaintext banner, and the connection simply hangs
    /// until it times out. The reverse pair (587 with SSL-on-connect) deadlocks the same way.
    /// </para>
    /// <para>
    /// So the port wins. It is the value a mail host actually tells you ("SMTP Port: 465"),
    /// whereas the security mode is a dropdown someone has to know to change — and a silent
    /// two-minute hang is a far worse outcome than quietly using the mode the port implies. The
    /// override is logged, because a connection behaving differently from what its stored
    /// configuration says should be discoverable.
    /// </para>
    /// </summary>
    private SecureSocketOptions ResolveSocketOptions(SmtpSecurityMode security, int port)
    {
        var requested = security switch
        {
            SmtpSecurityMode.SslOnConnect => SecureSocketOptions.SslOnConnect,
            SmtpSecurityMode.StartTls => SecureSocketOptions.StartTls,

            // "None" means no TLS at all, which sends credentials and message content in clear
            // text. Allowed, because a local capture server during development is a legitimate
            // use, but never inferred — only honoured when explicitly chosen.
            SmtpSecurityMode.None => SecureSocketOptions.None,
            _ => SecureSocketOptions.StartTls
        };

        var impliedByPort = port switch
        {
            465 => SecureSocketOptions.SslOnConnect,
            587 or 25 or 2525 => SecureSocketOptions.StartTls,
            _ => (SecureSocketOptions?)null
        };

        // Only for the two modes that can deadlock against each other. An explicit "None" is left
        // alone: someone who turned TLS off meant it, and quietly turning it back on would be a
        // surprise in the opposite direction.
        if (requested == SecureSocketOptions.None
            || impliedByPort is null
            || impliedByPort == requested)
        {
            return requested;
        }

        _logger.LogWarning(
            "SMTP port {Port} implies {Implied}, but this connection is configured for {Requested}. "
          + "Using {Implied} — the other pairing would hang until the connection timed out.",
            port, impliedByPort, requested, impliedByPort);

        return impliedByPort.Value;
    }

    /// <summary>
    /// Turns a connect failure into something an operator can act on.
    ///
    /// <para>
    /// A bare "operation timed out" is the least useful true statement available here. The
    /// overwhelmingly common cause is a port the server is not listening on, or a firewall in
    /// between, so the message says which host and port were tried.
    /// </para>
    /// </summary>
    private static string DescribeConnectFailure(
        Exception ex, string? host, int? port, SmtpSecurityMode security)
    {
        var resolvedPort = port ?? DefaultPortFor(security);

        var isTimeout = ex is TimeoutException
            || ex is OperationCanceledException
            || ex.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase);

        if (isTimeout)
        {
            return $"Timed out connecting to {host}:{resolvedPort}. The server did not respond — check "
                 + "that the host and port are correct and reachable from this server, and that no "
                 + $"firewall is blocking outbound port {resolvedPort}.";
        }

        // Still prefixed with the endpoint. Several of the exceptions that land here carry no
        // context at all — a DNS failure is the bare string "No such host is known." — which
        // leaves the operator without even the name that failed to resolve.
        return $"Could not connect to {host}:{resolvedPort}: {ex.Message}";
    }

    /// <summary>
    /// The conventional port for each security mode. Used only when none is configured —
    /// ResolveSocketOptions handles the reverse case, where a port is given and contradicts the
    /// stored mode.
    /// </summary>
    private static int DefaultPortFor(SmtpSecurityMode security) => security switch
    {
        SmtpSecurityMode.SslOnConnect => 465,
        SmtpSecurityMode.StartTls => 587,
        SmtpSecurityMode.None => 25,
        _ => 587
    };

    /// <summary>
    /// Pulls a queue id out of the server's acceptance response when there is one. Many relays
    /// answer "250 2.0.0 OK 1234567890 abc123" and that trailing token is the only handle for
    /// tracing a message in their logs. Best effort by nature: the format is not standardised.
    /// </summary>
    private static string? ExtractQueueId(string? response)
    {
        if (string.IsNullOrWhiteSpace(response)) return null;

        var token = response
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault(t => t.Length >= 8 && t.All(char.IsAsciiLetterOrDigit));

        return token;
    }
}
