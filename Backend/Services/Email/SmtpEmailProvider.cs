using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Models.Options;

namespace WhatsAppCampaignApi.Services.Email;

/// <summary>
/// Plain SMTP, via MailKit — with connection pooling.
///
/// <para>
/// The fallback path, and the reason the provider abstraction earns its keep: it makes any
/// mailbox a usable sender without new code — a Google Workspace relay, Microsoft 365, an
/// on-premise relay, or a local capture server like MailHog during development.
/// </para>
/// <para>
/// What it cannot do is report what happened afterwards. SMTP tells us only whether the next hop
/// accepted the message; delivery, bounces, complaints, opens and clicks are all invisible, which
/// is why <see cref="Capabilities"/> says so and the UI degrades gracefully rather than showing
/// tracking that will never populate.
/// </para>
/// <para>
/// Connection reuse: the <see cref="SmtpConnectionPoolManager"/> provides a per-key pool so that
/// concurrent dispatch workers sending for the same connection share authenticated sessions
/// instead of each paying the full TCP + TLS + AUTH round-trip per email.
/// </para>
/// </summary>
public class SmtpEmailProvider : IEmailProvider
{
    /// <summary>
    /// Conservative: most relays cap well below bulk-mail services, and exceeding a relay's limit typically
    /// produces a mid-transfer disconnect rather than a clear rejection.
    /// </summary>
    private const int SmtpMaxMessageBytes = 25 * 1024 * 1024;

    private readonly IMimeMessageBuilder _mimeBuilder;
    private readonly SmtpConnectionPoolManager _poolManager;
    private readonly IOptionsMonitor<EmailOptions> _options;
    private readonly ILogger<SmtpEmailProvider> _logger;

    public SmtpEmailProvider(
        IMimeMessageBuilder mimeBuilder,
        SmtpConnectionPoolManager poolManager,
        IOptionsMonitor<EmailOptions> options,
        ILogger<SmtpEmailProvider> logger)
    {
        _mimeBuilder = mimeBuilder;
        _poolManager = poolManager;
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

        var port          = context.SmtpPort ?? DefaultPortFor(context.SmtpSecurity);
        var socketOptions = ResolveSocketOptions(context.SmtpSecurity, port);

        if (socketOptions == SecureSocketOptions.None)
        {
            _logger.LogWarning(
                "SMTP connection {ConfigId} is sending without TLS. Credentials and message content are " +
                "transmitted in clear text.",
                context.EmailConfigurationId);
        }

        // Get or create a pooled connection for this SMTP server/credential combination
        var poolKey = SmtpConnectionPoolManager.BuildKey(
            context.SmtpHost, port,
            context.SmtpUsername ?? string.Empty,
            context.SmtpPassword ?? string.Empty,
            socketOptions);

        var pool = _poolManager.GetPool(poolKey);

        // Auth callback — set once per pool, executed when creating new connections
        pool._authCallback ??= async (client, token) =>
        {
            if (!string.IsNullOrWhiteSpace(context.SmtpUsername))
                await client.AuthenticateAsync(context.SmtpUsername, context.SmtpPassword ?? string.Empty, token);
        };

        PooledSmtpClient? rented = null;
        var poolHealthy = true;

        try
        {
            var mime = _mimeBuilder.Build(message);

            // Rent an authenticated, connected client from the pool
            rented = await pool.RentAsync(ct);

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
            var response = await rented.Inner.SendAsync(mime, sender, recipients, ct);

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
            poolHealthy = false;  // Discard this connection — auth failure persists until reconfigured
            return EmailSendResult.Failed(
                $"SMTP authentication failed: {ex.Message}", isTransient: false, errorCode: "AuthenticationFailed");
        }
        catch (SmtpCommandException ex)
        {
            // The server made a decision, and its status code says whether it is worth retrying.
            // 4xx is a temporary refusal — greylisting, mailbox busy, over quota; 5xx is
            // permanent — no such user, message refused.
            var transient = ex.StatusCode is >= SmtpStatusCode.ServiceNotAvailable and < (SmtpStatusCode)500;
            if (!transient) poolHealthy = false;
            return EmailSendResult.Failed(
                $"SMTP rejected the message ({(int)ex.StatusCode} {ex.ErrorCode}): {ex.Message}",
                transient, ex.StatusCode.ToString());
        }
        catch (SmtpProtocolException ex)
        {
            // The conversation broke down mid-flight. The message is fine, so retry.
            poolHealthy = false;  // Must reconnect after a protocol breakdown
            return EmailSendResult.Failed(
                $"SMTP protocol error: {ex.Message}", isTransient: true, errorCode: "ProtocolError");
        }
        catch (SslHandshakeException ex)
        {
            // Almost always a misconfiguration — wrong port for the chosen security mode, or an
            // untrusted certificate. Permanent, because it will fail identically every time.
            poolHealthy = false;
            return EmailSendResult.Failed(
                $"TLS handshake failed: {ex.Message}. Check the port and security mode for this connection.",
                isTransient: false, errorCode: "SslHandshakeFailed");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            poolHealthy = false;
            return EmailSendResult.Failed("Send cancelled during shutdown.", isTransient: true);
        }
        catch (Exception ex)
        {
            // Networking: unreachable host, DNS failure, timeout. The message is fine.
            poolHealthy = false;
            _logger.LogError(ex, "Unexpected failure sending via SMTP host {Host}.", context.SmtpHost);
            return EmailSendResult.Failed(
                DescribeConnectFailure(ex, context.SmtpHost, context.SmtpPort, context.SmtpSecurity),
                isTransient: true, errorCode: ex.GetType().Name);
        }
        finally
        {
            if (rented is not null)
                await pool.ReturnAsync(rented, poolHealthy);
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
                ["host"]     = context.SmtpHost,
                ["port"]     = port.ToString(),
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
                $"TLS handshake failed: {ex.Message}. The port and security mode are most likely mismatched — " +
                "587 expects STARTTLS and 465 expects implicit SSL.");
        }
        catch (Exception ex)
        {
            // Through the same describer as the send path, so a timeout on the wizard's Test
            // button names the port it actually tried.
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
    /// Port 465 = implicit TLS; port 587/25 = STARTTLS.
    /// </summary>
    private SecureSocketOptions ResolveSocketOptions(SmtpSecurityMode security, int port)
    {
        var requested = security switch
        {
            SmtpSecurityMode.SslOnConnect => SecureSocketOptions.SslOnConnect,
            SmtpSecurityMode.StartTls     => SecureSocketOptions.StartTls,
            SmtpSecurityMode.None         => SecureSocketOptions.None,
            _                             => SecureSocketOptions.StartTls
        };

        var impliedByPort = port switch
        {
            465              => SecureSocketOptions.SslOnConnect,
            587 or 25 or 2525 => SecureSocketOptions.StartTls,
            _                => (SecureSocketOptions?)null
        };

        if (requested == SecureSocketOptions.None
            || impliedByPort is null
            || impliedByPort == requested)
        {
            return requested;
        }

        _logger.LogWarning(
            "SMTP port {Port} implies {Implied}, but this connection is configured for {Requested}. " +
            "Using {Implied} — the other pairing would hang until the connection timed out.",
            port, impliedByPort, requested, impliedByPort);

        return impliedByPort.Value;
    }

    private static string DescribeConnectFailure(
        Exception ex, string? host, int? port, SmtpSecurityMode security)
    {
        var resolvedPort = port ?? DefaultPortFor(security);

        var isTimeout = ex is TimeoutException
            || ex is OperationCanceledException
            || ex.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase);

        if (isTimeout)
        {
            return $"Timed out connecting to {host}:{resolvedPort}. The server did not respond — check " +
                   "that the host and port are correct and reachable from this server, and that no " +
                   $"firewall is blocking outbound port {resolvedPort}.";
        }

        return $"Could not connect to {host}:{resolvedPort}: {ex.Message}";
    }

    private static int DefaultPortFor(SmtpSecurityMode security) => security switch
    {
        SmtpSecurityMode.SslOnConnect => 465,
        SmtpSecurityMode.StartTls     => 587,
        SmtpSecurityMode.None         => 25,
        _                             => 587
    };

    private static string? ExtractQueueId(string? response)
    {
        if (string.IsNullOrWhiteSpace(response)) return null;

        var token = response
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault(t => t.Length >= 8 && t.All(char.IsAsciiLetterOrDigit));

        return token;
    }
}
