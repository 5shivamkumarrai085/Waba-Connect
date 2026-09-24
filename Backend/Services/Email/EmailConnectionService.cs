using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Email;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Models.Options;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services.Email;

/// <inheritdoc />
public class EmailConnectionService : IEmailConnectionService
{
    private readonly AppDbContext _dbContext;
    private readonly IEncryptionService _encryption;
    private readonly IEmailProviderFactory _providerFactory;
    private readonly IMimeMessageBuilder _mimeBuilder;
    private readonly IOptionsMonitor<EmailOptions> _options;
    private readonly IAuditService _auditService;
    private readonly IEmailDomainService _domainService;
    private readonly ILogger<EmailConnectionService> _logger;

    public EmailConnectionService(
        AppDbContext dbContext,
        IEncryptionService encryption,
        IEmailProviderFactory providerFactory,
        IMimeMessageBuilder mimeBuilder,
        IOptionsMonitor<EmailOptions> options,
        IAuditService auditService,
        IEmailDomainService domainService,
        ILogger<EmailConnectionService> logger)
    {
        _dbContext = dbContext;
        _encryption = encryption;
        _providerFactory = providerFactory;
        _mimeBuilder = mimeBuilder;
        _options = options;
        _auditService = auditService;
        _domainService = domainService;
        _logger = logger;
    }

    public async Task<List<EmailConnectionResponse>> GetAllAsync(CancellationToken ct = default)
    {
        var configurations = await LoadQuery().ToListAsync(ct);
        return configurations.Select(MapToResponse).ToList();
    }

    public async Task<EmailConnectionResponse> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var configuration = await LoadQuery().FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new KeyNotFoundException($"Email connection {id} not found.");

        return MapToResponse(configuration);
    }

    public async Task<EmailConnectionResponse> CreateAsync(
        CreateEmailConnectionRequest request,
        CancellationToken ct = default)
    {
        var name = request.Name.Trim();

        // Connection.Name is uniquely indexed, so this is checked here to report a clean conflict
        // rather than letting the database raise a constraint violation.
        var nameTaken = await _dbContext.Connections.AnyAsync(c => c.Name.ToLower() == name.ToLower(), ct);
        if (nameTaken)
        {
            throw new InvalidOperationException($"A connection named \"{name}\" already exists.");
        }

        var email = request.EmailAddress.Trim();

        var connection = new Connection
        {
            Name = name,
            // Nickname is [Required] on the entity but nullable in the database, and the WhatsApp
            // wizard collects it explicitly. Email has no equivalent field in its wizard, so one
            // is derived — leaving it null would trip validation anywhere the shared connection
            // DTOs are reused.
            Nickname = DeriveNickname(request.Nickname, name),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            IsActive = true
        };

        _dbContext.Connections.Add(connection);

        var configuration = new EmailConfiguration
        {
            Connection = connection,
            Provider = ParseProvider(_options.CurrentValue.DefaultProvider),

            // Inactive until step 2 supplies credentials. A configuration that claims to be
            // active with nothing behind it would let a campaign be created against a connection
            // that cannot send.
            IsActive = false,

            AuthMode = EmailAuthMode.IamRole,
            DefaultFromName = request.DisplayName.Trim(),
            DefaultFromEmail = email,
            DefaultReplyTo = string.IsNullOrWhiteSpace(request.ReplyToEmail) ? null : request.ReplyToEmail.Trim()
        };

        configuration.SenderIdentities.Add(new EmailSenderIdentity
        {
            DisplayName = request.DisplayName.Trim(),
            EmailAddress = email,
            ReplyTo = configuration.DefaultReplyTo,
            IsDefault = true,
            IsActive = true,

            // Nothing is verified until the provider says so. Starting at NotStarted rather than
            // Verified means the send gate blocks until the domain is genuinely authenticated,
            // instead of failing at the provider once a campaign is already running.
            VerificationStatus = EmailIdentityStatus.NotStarted
        });

        _dbContext.EmailConfigurations.Add(configuration);
        await _dbContext.SaveChangesAsync(ct);

        await _auditService.LogAsync(
            "EmailConnection.Created", "Data",
            $"Created email connection \"{name}\" for {email}.",
            nameof(EmailConfiguration), configuration.Id.ToString());

        return await GetByIdAsync(configuration.Id, ct);
    }

    public async Task<EmailConnectionResponse> SaveProviderAsync(
        int id,
        SaveEmailProviderRequest request,
        CancellationToken ct = default)
    {
        var configuration = await _dbContext.EmailConfigurations
            .Include(c => c.Connection)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new KeyNotFoundException($"Email connection {id} not found.");

        var provider = ParseProvider(request.Provider);
        configuration.Provider = provider;
        configuration.IsActive = request.IsActive;

        if (provider == EmailProviderType.AmazonSes)
        {
            configuration.Region = Trim(request.Region);
            configuration.AuthMode = ParseAuthMode(request.AuthMode);
            configuration.ConfigurationSet = Trim(request.ConfigurationSet);

            if (configuration.AuthMode == EmailAuthMode.IamRole)
            {
                // Switching to the role-based chain must not leave a usable key behind. Keeping
                // it would mean a credential nobody believes is in use is still decryptable in
                // the database.
                configuration.AccessKeyId = null;
                configuration.SecretAccessKeyEncrypted = null;
            }
            else
            {
                configuration.AccessKeyId = Trim(request.AccessKeyId);

                // Blank means "keep the stored key". This is what lets an operator change the
                // region without re-typing a secret, and is the reason the API never needs to
                // return one.
                if (!string.IsNullOrWhiteSpace(request.SecretAccessKey))
                {
                    configuration.SecretAccessKeyEncrypted = _encryption.Encrypt(request.SecretAccessKey.Trim());
                }

                if (string.IsNullOrWhiteSpace(configuration.AccessKeyId)
                    || string.IsNullOrWhiteSpace(configuration.SecretAccessKeyEncrypted))
                {
                    throw new ArgumentException(
                        "Access-key authentication requires both an access key ID and a secret access key.");
                }
            }
        }
        else
        {
            configuration.SmtpHost = Trim(request.SmtpHost);
            configuration.SmtpPort = request.SmtpPort;
            configuration.SmtpSecurity = ParseSmtpSecurity(request.SmtpSecurity);
            configuration.SmtpUsername = Trim(request.SmtpUsername);

            if (!string.IsNullOrWhiteSpace(request.SmtpPassword))
            {
                configuration.SmtpPasswordEncrypted = _encryption.Encrypt(request.SmtpPassword.Trim());
            }

            if (string.IsNullOrWhiteSpace(configuration.SmtpHost))
            {
                throw new ArgumentException("An SMTP host is required for the SMTP provider.");
            }
        }

        // Stamped once, on the first successful configuration. This is what later distinguishes a
        // disconnected connection from one that was never set up.
        configuration.ConfiguredAt ??= DateTime.UtcNow;

        configuration.MaxSendRatePerSecond = request.MaxSendRatePerSecond;
        configuration.DefaultFromName = Trim(request.DefaultFromName) ?? configuration.DefaultFromName;
        configuration.DefaultFromEmail = Trim(request.DefaultFromEmail) ?? configuration.DefaultFromEmail;
        configuration.DefaultReplyTo = Trim(request.DefaultReplyTo);

        await _dbContext.SaveChangesAsync(ct);

        // Kept in step with the configured rate so the cross-instance limiter cannot go on
        // enforcing a rate the operator has already changed.
        await SyncSendQuotaAsync(configuration, ct);

        // The event name says the provider was configured, not what the credential is. Audit
        // descriptions are read by people who should not be learning secrets from them.
        await _auditService.LogAsync(
            "EmailConnection.ProviderConfigured", "Settings",
            $"Configured {provider} for email connection \"{configuration.Connection?.Name}\".",
            nameof(EmailConfiguration), configuration.Id.ToString());

        return await GetByIdAsync(id, ct);
    }

    public async Task<EmailProviderTestResponse> TestConnectionAsync(int id, CancellationToken ct = default)
    {
        var (provider, context) = await _providerFactory.ResolveByConfigurationAsync(id, ct);
        var result = await provider.TestConnectionAsync(context, ct);

        await RecordTestResultAsync(id, result, ct);

        return new EmailProviderTestResponse
        {
            Success = result.Success,
            Message = result.Message,
            Details = result.Details?.ToDictionary(d => d.Key, d => d.Value)
        };
    }

    public async Task<EmailProviderTestResponse> TestUnsavedAsync(
        TestEmailProviderRequest request,
        CancellationToken ct = default)
    {
        var provider = _providerFactory.GetProvider(request.Provider);

        // Secrets the operator did not retype are taken from the stored configuration, so
        // re-testing after changing only a region does not force them to re-enter a key.
        string? secretAccessKey = Trim(request.SecretAccessKey);
        string? smtpPassword = Trim(request.SmtpPassword);

        if (request.EmailConfigurationId is { } configurationId
            && (secretAccessKey is null || smtpPassword is null))
        {
            var stored = await _dbContext.EmailConfigurations
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == configurationId, ct);

            if (stored is not null)
            {
                secretAccessKey ??= DecryptOrNull(stored.SecretAccessKeyEncrypted);
                smtpPassword ??= DecryptOrNull(stored.SmtpPasswordEncrypted);
            }
        }

        var context = new EmailProviderContext
        {
            // Zero, because nothing is stored yet. Only used for log correlation.
            EmailConfigurationId = request.EmailConfigurationId ?? 0,
            Provider = ParseProvider(request.Provider),
            Region = Trim(request.Region),
            AuthMode = ParseAuthMode(request.AuthMode),
            AccessKeyId = Trim(request.AccessKeyId),
            SecretAccessKey = secretAccessKey,
            ConfigurationSet = Trim(request.ConfigurationSet),
            SmtpHost = Trim(request.SmtpHost),
            SmtpPort = request.SmtpPort,
            SmtpSecurity = ParseSmtpSecurity(request.SmtpSecurity),
            SmtpUsername = Trim(request.SmtpUsername),
            SmtpPassword = smtpPassword,
            DefaultFromName = Trim(request.DefaultFromName),
            DefaultFromEmail = Trim(request.DefaultFromEmail),
            DefaultReplyTo = Trim(request.DefaultReplyTo)
        };

        var result = await provider.TestConnectionAsync(context, ct);

        if (request.EmailConfigurationId is { } storedId and > 0)
        {
            await RecordTestResultAsync(storedId, result, ct);
        }

        return new EmailProviderTestResponse
        {
            Success = result.Success,
            Message = result.Message,
            Details = result.Details?.ToDictionary(d => d.Key, d => d.Value)
        };
    }

    public async Task<EmailProviderTestResponse> SendTestEmailAsync(
        int id,
        SendTestEmailRequest request,
        CancellationToken ct = default)
    {
        var configuration = await LoadQuery().FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new KeyNotFoundException($"Email connection {id} not found.");

        var sender = request.SenderIdentityId is { } senderId
            ? configuration.SenderIdentities.FirstOrDefault(s => s.Id == senderId)
            : configuration.SenderIdentities.FirstOrDefault(s => s.IsDefault && s.IsActive)
              ?? configuration.SenderIdentities.FirstOrDefault(s => s.IsActive);

        if (sender is null)
        {
            throw new InvalidOperationException(
                "This email connection has no active sender identity to send from.");
        }

        var (provider, context) = await _providerFactory.ResolveByConfigurationAsync(id, ct);

        var fromDomain = sender.EmailAddress.Contains('@')
            ? sender.EmailAddress.Split('@')[1]
            : "localhost";

        var message = new EmailMessage
        {
            From = new EmailAddress(sender.EmailAddress, sender.DisplayName),
            ReplyTo = string.IsNullOrWhiteSpace(sender.ReplyTo) ? null : new EmailAddress(sender.ReplyTo),
            To = [new EmailAddress(request.ToAddress.Trim())],
            Subject = $"Test email from {configuration.Connection?.Name ?? "OmniConnect"}",
            HtmlBody =
                "<p>This is a test email confirming that your email connection is configured correctly.</p>"
              + $"<p>Provider: <strong>{provider.ProviderName}</strong><br>"
              + $"Connection: <strong>{configuration.Connection?.Name}</strong><br>"
              + $"Sent at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</p>",
            MessageId = _mimeBuilder.NewMessageId(fromDomain),

            // Tagged so the delivery event for this send is distinguishable from campaign
            // traffic in reporting.
            Tags = new Dictionary<string, string> { ["purpose"] = "connection_test" },
            ConfigurationSet = context.ConfigurationSet
        };

        var result = await provider.SendAsync(message, context, ct);

        // Recorded like a connection test, because that is how an operator reads it: the wizard
        // shows the last outcome either way.
        await RecordTestResultAsync(
            id,
            result.Success
                ? EmailProviderTestResult.Ok($"Test email sent to {request.ToAddress}.")
                : EmailProviderTestResult.Fail(result.ErrorMessage ?? "Send failed."),
            ct);

        await _auditService.LogAsync(
            result.Success ? "EmailConnection.TestEmailSent" : "EmailConnection.TestEmailFailed",
            "Settings",
            $"Test email to {request.ToAddress} via \"{configuration.Connection?.Name}\": "
          + (result.Success ? "sent." : result.ErrorMessage),
            nameof(EmailConfiguration), id.ToString());

        return new EmailProviderTestResponse
        {
            Success = result.Success,
            Message = result.Success
                ? $"A test email has been sent to {request.ToAddress}. Please check the inbox."
                : result.ErrorMessage ?? "The test email could not be sent.",
            Details = result.Success && result.ProviderMessageId is not null
                ? new Dictionary<string, string> { ["providerMessageId"] = result.ProviderMessageId }
                : null
        };
    }

    public async Task<List<EmailSenderIdentityResponse>> GetSendersAsync(int id, CancellationToken ct = default)
    {
        var configuration = await LoadQuery().FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new KeyNotFoundException($"Email connection {id} not found.");

        return configuration.SenderIdentities.Select(s => MapSender(s, configuration)).ToList();
    }

    public async Task<EmailSenderIdentityResponse> AddSenderAsync(
        int id,
        SaveEmailSenderIdentityRequest request,
        CancellationToken ct = default)
    {
        var configuration = await _dbContext.EmailConfigurations
            .Include(c => c.SenderIdentities)
                // See the note in LoadQuery: without this, MapSender's CanSend reads a null
                // SendingDomain and reports a domain-verified sender as unusable.
                .ThenInclude(s => s.SendingDomain)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new KeyNotFoundException($"Email connection {id} not found.");

        var email = request.EmailAddress.Trim();

        if (configuration.SenderIdentities.Any(s => s.EmailAddress.Equals(email, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"{email} is already a sender on this connection.");
        }

        var sender = new EmailSenderIdentity
        {
            EmailConfigurationId = id,
            DisplayName = request.DisplayName.Trim(),
            EmailAddress = email,
            ReplyTo = Trim(request.ReplyTo),
            IsActive = request.IsActive,

            // The first sender is the default whether or not the caller said so — a connection
            // with senders but no default has no answer to "who does a campaign send as".
            IsDefault = request.IsDefault || configuration.SenderIdentities.Count == 0,
            VerificationStatus = EmailIdentityStatus.NotStarted,
            SendingDomainId = await FindDomainForAsync(id, email, ct)
        };

        if (sender.IsDefault) ClearOtherDefaults(configuration.SenderIdentities, exceptId: null);

        _dbContext.EmailSenderIdentities.Add(sender);
        await _dbContext.SaveChangesAsync(ct);

        await _auditService.LogAsync(
            "EmailSender.Added", "Settings",
            $"Added sender {email} to email connection {id}.",
            nameof(EmailSenderIdentity), sender.Id.ToString());

        // Ask SES straight away rather than storing NotStarted and waiting to be asked. Verifying
        // an address in the SES console and then adding it here is the ordinary order of events,
        // and leaving it NotStarted made an already-verified sender unusable with no indication
        // of what to do about it.
        //
        // Deliberately not fatal: the sender is saved either way, and a transient SES failure
        // should not lose an operator's work. The gate re-checks before any send.
        await _domainService.RefreshSenderStatusAsync(sender.Id, ct);

        return MapSender(sender, configuration);
    }

    public async Task<EmailSenderIdentityResponse> UpdateSenderAsync(
        int id,
        int senderId,
        SaveEmailSenderIdentityRequest request,
        CancellationToken ct = default)
    {
        var configuration = await _dbContext.EmailConfigurations
            .Include(c => c.SenderIdentities)
                // See the note in LoadQuery: without this, MapSender's CanSend reads a null
                // SendingDomain and reports a domain-verified sender as unusable.
                .ThenInclude(s => s.SendingDomain)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new KeyNotFoundException($"Email connection {id} not found.");

        var sender = configuration.SenderIdentities.FirstOrDefault(s => s.Id == senderId)
            ?? throw new KeyNotFoundException($"Sender {senderId} not found on this connection.");

        var email = request.EmailAddress.Trim();
        var addressChanged = !sender.EmailAddress.Equals(email, StringComparison.OrdinalIgnoreCase);

        sender.DisplayName = request.DisplayName.Trim();
        sender.EmailAddress = email;
        sender.ReplyTo = Trim(request.ReplyTo);
        sender.IsActive = request.IsActive;

        if (addressChanged)
        {
            // A different address is a different identity as far as the provider is concerned, so
            // its verification cannot be inherited — carrying it over would let an unverified
            // address through the send gate.
            sender.VerificationStatus = EmailIdentityStatus.NotStarted;
            sender.SendingDomainId = await FindDomainForAsync(id, email, ct);
        }

        if (request.IsDefault)
        {
            ClearOtherDefaults(configuration.SenderIdentities, exceptId: sender.Id);
            sender.IsDefault = true;
        }

        await _dbContext.SaveChangesAsync(ct);

        // Only when the address changed: the status was just reset to NotStarted above, and a
        // rename or a reply-to edit is not a reason to call SES.
        if (addressChanged) await _domainService.RefreshSenderStatusAsync(sender.Id, ct);

        await _auditService.LogAsync(
            "EmailSender.Updated", "Settings",
            $"Updated sender {email} on email connection {id}.",
            nameof(EmailSenderIdentity), sender.Id.ToString());

        return MapSender(sender, configuration);
    }

    public async Task DeleteSenderAsync(int id, int senderId, CancellationToken ct = default)
    {
        var sender = await _dbContext.EmailSenderIdentities
            .FirstOrDefaultAsync(s => s.Id == senderId && s.EmailConfigurationId == id, ct)
            ?? throw new KeyNotFoundException($"Sender {senderId} not found on this connection.");

        // Refused rather than cascaded: an EmailCampaignDetail points at this row to record who a
        // campaign sent as, and deleting it would erase that from already-sent history.
        var inUse = await _dbContext.EmailCampaignDetails
            .IgnoreQueryFilters()
            .AnyAsync(d => d.SenderIdentityId == senderId, ct);

        if (inUse)
        {
            throw new InvalidOperationException(
                "This sender has been used by a campaign and cannot be deleted. Deactivate it instead.");
        }

        _dbContext.EmailSenderIdentities.Remove(sender);
        await _dbContext.SaveChangesAsync(ct);

        // If the default was removed, promote another so the connection still has one.
        if (sender.IsDefault)
        {
            var replacement = await _dbContext.EmailSenderIdentities
                .Where(s => s.EmailConfigurationId == id && s.IsActive)
                .OrderBy(s => s.Id)
                .FirstOrDefaultAsync(ct);

            if (replacement is not null)
            {
                replacement.IsDefault = true;
                await _dbContext.SaveChangesAsync(ct);
            }
        }

        await _auditService.LogAsync(
            "EmailSender.Deleted", "Settings",
            $"Removed sender {sender.EmailAddress} from email connection {id}.",
            nameof(EmailSenderIdentity), senderId.ToString());
    }

    public async Task DisconnectAsync(int id, CancellationToken ct = default)
    {
        var configuration = await _dbContext.EmailConfigurations
            .Include(c => c.Connection)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new KeyNotFoundException($"Email connection {id} not found.");

        configuration.IsActive = false;

        // Secrets cleared, mirroring what WhatsApp's disconnect does to its access token. A
        // "disconnected" connection that still holds a working credential is a credential nobody
        // is watching.
        configuration.SecretAccessKeyEncrypted = null;
        configuration.SmtpPasswordEncrypted = null;

        await _dbContext.SaveChangesAsync(ct);

        await _auditService.LogAsync(
            "EmailConnection.Disconnected", "Settings",
            $"Disconnected email connection \"{configuration.Connection?.Name}\" and cleared its stored credentials.",
            nameof(EmailConfiguration), id.ToString());
    }

    public async Task<EmailConnectionResponse> SaveImapSettingsAsync(
        int id,
        SaveImapSettingsRequest request,
        CancellationToken ct = default)
    {
        var configuration = await _dbContext.EmailConfigurations
            .Include(c => c.Connection)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new KeyNotFoundException($"Email connection {id} not found.");

        configuration.ImapHost = request.ImapHost.Trim();
        configuration.ImapPort = request.ImapPort;

        if (request.ImapSecurity is not null)
        {
            configuration.ImapSecurity = ParseSmtpSecurity(request.ImapSecurity);
        }

        // Blank username/password → fall back to SMTP credentials at poll time, so we only
        // store what the operator explicitly provided.
        configuration.ImapUsername = Trim(request.ImapUsername);

        if (!string.IsNullOrWhiteSpace(request.ImapPassword))
        {
            configuration.ImapPasswordEncrypted = _encryption.Encrypt(request.ImapPassword.Trim());
        }

        await _dbContext.SaveChangesAsync(ct);

        await _auditService.LogAsync(
            "EmailConnection.ImapSaved", "Settings",
            $"Saved IMAP settings for email connection \"{configuration.Connection?.Name}\" (host: {configuration.ImapHost}).",
            nameof(EmailConfiguration), id.ToString());

        return await GetByIdAsync(id, ct);
    }

    public async Task<EmailProviderTestResponse> TestImapConnectionAsync(
        int id,
        CancellationToken ct = default)
    {
        var configuration = await _dbContext.EmailConfigurations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new KeyNotFoundException($"Email connection {id} not found.");

        if (string.IsNullOrWhiteSpace(configuration.ImapHost))
        {
            return new EmailProviderTestResponse
            {
                Success = false,
                Message = "No IMAP host is configured for this connection. Save IMAP settings first."
            };
        }

        var username = !string.IsNullOrWhiteSpace(configuration.ImapUsername)
            ? configuration.ImapUsername
            : configuration.SmtpUsername;

        var password = DecryptOrNull(configuration.ImapPasswordEncrypted)
            ?? DecryptOrNull(configuration.SmtpPasswordEncrypted);

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return new EmailProviderTestResponse
            {
                Success = false,
                Message = "IMAP credentials are incomplete. Configure an IMAP username and password (or SMTP credentials to fall back to)."
            };
        }

        var port = configuration.ImapPort ?? 993;

        var socketOptions = configuration.ImapSecurity switch
        {
            SmtpSecurityMode.SslOnConnect => MailKit.Security.SecureSocketOptions.SslOnConnect,
            SmtpSecurityMode.StartTls     => MailKit.Security.SecureSocketOptions.StartTls,
            SmtpSecurityMode.None         => MailKit.Security.SecureSocketOptions.None,
            _                             => MailKit.Security.SecureSocketOptions.SslOnConnect
        };

        try
        {
            using var client = new MailKit.Net.Imap.ImapClient();
            client.Timeout = 20_000;

            // cPanel and other shared-hosting providers often use a self-signed certificate
            // (e.g. mail.rma.my serving a cert for the hosting provider's own domain).
            // MailKit rejects these by default. We bypass validation here because:
            // 1. The connection itself is still TLS-encrypted.
            // 2. This is an inbound-only poll — we receive, never send credentials over plain text.
            client.ServerCertificateValidationCallback = (s, c, h, e) => true;

            await client.ConnectAsync(configuration.ImapHost, port, socketOptions, ct);
            await client.AuthenticateAsync(username, password, ct);

            var inbox = client.Inbox;
            await inbox.OpenAsync(MailKit.FolderAccess.ReadOnly, ct);
            var messageCount = inbox.Count;

            await client.DisconnectAsync(quit: true, ct);

            var message = $"IMAP connection successful. Inbox contains {messageCount} message(s).";

            await RecordTestResultAsync(id, EmailProviderTestResult.Ok(message), ct);

            return new EmailProviderTestResponse
            {
                Success = true,
                Message = message,
                Details = new Dictionary<string, string>
                {
                    ["host"] = configuration.ImapHost,
                    ["port"] = port.ToString(),
                    ["messageCount"] = messageCount.ToString()
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "IMAP connection test failed for email configuration {ConfigId} ({Host}:{Port}).",
                id, configuration.ImapHost, port);

            var errorMessage = $"IMAP connection failed: {ex.Message}";
            await RecordTestResultAsync(id, EmailProviderTestResult.Fail(errorMessage), ct);

            return new EmailProviderTestResponse
            {
                Success = false,
                Message = errorMessage
            };
        }
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var configuration = await _dbContext.EmailConfigurations
            .Include(c => c.Connection)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new KeyNotFoundException($"Email connection {id} not found.");

        var campaignCount = await _dbContext.Campaigns
            .IgnoreQueryFilters()
            .CountAsync(c => c.ConnectionId == configuration.ConnectionId
                          && c.Channel == MessageChannel.Email, ct);

        if (campaignCount > 0)
        {
            // Deleting would take the campaign history with it. Disconnect is the non-destructive
            // action an operator almost always actually wants here.
            throw new InvalidOperationException(
                $"This connection has {campaignCount} email campaign(s) and cannot be deleted. Disconnect it instead.");
        }

        var name = configuration.Connection?.Name;

        _dbContext.EmailConfigurations.Remove(configuration);
        await _dbContext.SaveChangesAsync(ct);

        await _auditService.LogAsync(
            "EmailConnection.Deleted", "Data",
            $"Deleted email connection \"{name}\".",
            nameof(EmailConfiguration), id.ToString());
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────

    private IQueryable<EmailConfiguration> LoadQuery() =>
        _dbContext.EmailConfigurations
            .AsNoTracking()
            .Include(c => c.Connection)
            .Include(c => c.SenderIdentities)
                // The sender's own domain, explicitly. Including the configuration's SendingDomains
                // collection does NOT populate it: this query is AsNoTracking, so EF performs no
                // relationship fixup between the two collections, and sender.SendingDomain came
                // back null. CanSend then read null and hid senders whose domain was verified —
                // the API contradicting the server's own send gate.
                .ThenInclude(s => s.SendingDomain)
            .Include(c => c.SendingDomains)
            // Two collections at the same level: without this, every domain repeats once per
            // sender. Same reasoning as the campaign detail query.
            .AsSplitQuery()
            .OrderBy(c => c.Id);

    /// <summary>
    /// Keeps the rate-limiter bucket aligned with the configured send rate.
    ///
    /// <para>
    /// Without this the limiter would keep enforcing whatever rate was in effect when the bucket
    /// row was created, so raising a connection's rate would appear to do nothing.
    /// </para>
    /// </summary>
    private async Task SyncSendQuotaAsync(EmailConfiguration configuration, CancellationToken ct)
    {
        if (configuration.ConnectionId is not { } connectionId) return;

        var rate = (double)(configuration.MaxSendRatePerSecond
            ?? (decimal)_options.CurrentValue.Dispatch.DefaultSendRatePerSecond);

        var quota = await _dbContext.EmailSendQuotas.FirstOrDefaultAsync(q => q.ConnectionId == connectionId, ct);

        if (quota is null)
        {
            _dbContext.EmailSendQuotas.Add(new EmailSendQuota
            {
                ConnectionId = connectionId,
                RefillPerSecond = rate,

                // Burst capacity of one second's worth, floored at 1. A larger burst would let a
                // campaign briefly exceed the provider's per-second limit and get throttled.
                Capacity = Math.Max(1, rate),
                Tokens = Math.Max(1, rate),
                LastRefillAt = DateTime.UtcNow
            });
        }
        else
        {
            quota.RefillPerSecond = rate;
            quota.Capacity = Math.Max(1, rate);

            // Existing tokens are clamped rather than refilled: lowering the rate must take
            // effect immediately, not after a bucket full of old allowance has drained.
            quota.Tokens = Math.Min(quota.Tokens, quota.Capacity);
        }

        await _dbContext.SaveChangesAsync(ct);
    }

    private async Task RecordTestResultAsync(int configurationId, EmailProviderTestResult result, CancellationToken ct)
    {
        var configuration = await _dbContext.EmailConfigurations.FirstOrDefaultAsync(c => c.Id == configurationId, ct);
        if (configuration is null) return;

        configuration.LastTestedAt = DateTime.UtcNow;
        configuration.LastTestSucceeded = result.Success;
        configuration.LastTestMessage = result.Message.Length > 1000 ? result.Message[..1000] : result.Message;

        await _dbContext.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Links a sender to the sending domain that authorises it, when one is configured. Matching
    /// on the address's domain is what lets the send gate answer "is this sender authenticated"
    /// without a second lookup per recipient.
    /// </summary>
    private async Task<int?> FindDomainForAsync(int configurationId, string emailAddress, CancellationToken ct)
    {
        if (!emailAddress.Contains('@')) return null;

        var domain = emailAddress.Split('@')[1].Trim().ToLowerInvariant();

        return await _dbContext.EmailSendingDomains
            .Where(d => d.EmailConfigurationId == configurationId && d.DomainName.ToLower() == domain)
            .Select(d => (int?)d.Id)
            .FirstOrDefaultAsync(ct);
    }

    private static void ClearOtherDefaults(IEnumerable<EmailSenderIdentity> senders, int? exceptId)
    {
        foreach (var other in senders.Where(s => s.IsDefault && s.Id != exceptId))
        {
            other.IsDefault = false;
        }
    }

    private EmailConnectionResponse MapToResponse(EmailConfiguration configuration)
    {
        var capabilities = TryGetCapabilities(configuration.Provider);

        return new EmailConnectionResponse
        {
            Id = configuration.Id,
            ConnectionId = configuration.ConnectionId,
            ConnectionName = configuration.Connection?.Name ?? string.Empty,
            Nickname = configuration.Connection?.Nickname,
            Description = configuration.Connection?.Description,
            Provider = configuration.Provider.ToString(),
            IsActive = configuration.IsActive,
            Region = configuration.Region,
            AuthMode = configuration.AuthMode.ToString(),
            AccessKeyId = configuration.AccessKeyId,

            // The flags, never the values.
            HasSecretAccessKey = !string.IsNullOrEmpty(configuration.SecretAccessKeyEncrypted),
            HasSmtpPassword = !string.IsNullOrEmpty(configuration.SmtpPasswordEncrypted),

            ConfigurationSet = configuration.ConfigurationSet,
            SmtpHost = configuration.SmtpHost,
            SmtpPort = configuration.SmtpPort,
            SmtpSecurity = configuration.SmtpSecurity.ToString(),
            SmtpUsername = configuration.SmtpUsername,

            // IMAP — flag only for the password, same pattern as SMTP
            ImapHost = configuration.ImapHost,
            ImapPort = configuration.ImapPort,
            ImapSecurity = configuration.ImapSecurity.ToString(),
            ImapUsername = configuration.ImapUsername,
            HasImapPassword = !string.IsNullOrEmpty(configuration.ImapPasswordEncrypted),

            MaxSendRatePerSecond = configuration.MaxSendRatePerSecond,
            DefaultFromName = configuration.DefaultFromName,
            DefaultFromEmail = configuration.DefaultFromEmail,
            DefaultReplyTo = configuration.DefaultReplyTo,
            ConfiguredAt = configuration.ConfiguredAt,
            LastTestedAt = configuration.LastTestedAt,
            LastTestSucceeded = configuration.LastTestSucceeded,
            LastTestMessage = configuration.LastTestMessage,
            Status = DeriveStatus(configuration),
            Capabilities = capabilities is null ? null : new EmailProviderCapabilitiesResponse
            {
                SupportsEventWebhooks = capabilities.SupportsEventWebhooks,
                SupportsDkimProvisioning = capabilities.SupportsDkimProvisioning,
                SupportsSuppressionApi = capabilities.SupportsSuppressionApi,
                SupportsInboundReceiving = capabilities.SupportsInboundReceiving,
                MaxMessageBytes = capabilities.MaxMessageBytes
            },
            Senders = configuration.SenderIdentities
                .OrderByDescending(s => s.IsDefault)
                .ThenBy(s => s.EmailAddress)
                .Select(s => MapSender(s, configuration))
                .ToList(),
            Domains = configuration.SendingDomains
                .OrderBy(d => d.DomainName)
                .Select(d => new EmailSendingDomainResponse
                {
                    Id = d.Id,
                    DomainName = d.DomainName,
                    VerificationStatus = d.VerificationStatus.ToString(),
                    DkimStatus = d.DkimStatus.ToString(),
                    MailFromDomain = d.MailFromDomain,
                    MailFromStatus = d.MailFromStatus.ToString(),
                    LastCheckedAt = d.LastCheckedAt,
                    LastCheckMessage = d.LastCheckMessage
                })
                .ToList(),
            CreatedAt = configuration.CreatedAt,
            UpdatedAt = configuration.UpdatedAt
        };
    }

    private EmailProviderCapabilities? TryGetCapabilities(EmailProviderType provider)
    {
        try
        {
            return _providerFactory.GetProvider(provider.ToString()).Capabilities;
        }
        catch (ArgumentException)
        {
            // A configuration naming a provider this build no longer registers. Worth surfacing
            // as "unknown capabilities" rather than failing the whole listing.
            _logger.LogWarning("No provider is registered for '{Provider}'.", provider);
            return null;
        }
    }

    /// <summary>
    /// Projects a sender for the API, including whether the UI may offer it.
    /// </summary>
    /// <param name="configuration">
    /// The connection it belongs to. Required, because whether a sender may send depends on facts
    /// that live there — the provider and whether the connection is active — and computing
    /// <c>CanSend</c> without them produced an answer that disagreed with the server's own gate.
    /// </param>
    private static EmailSenderIdentityResponse MapSender(
        EmailSenderIdentity sender,
        EmailConfiguration? configuration) => new()
    {
        Id = sender.Id,
        EmailConfigurationId = sender.EmailConfigurationId,
        DisplayName = sender.DisplayName,
        EmailAddress = sender.EmailAddress,
        ReplyTo = sender.ReplyTo,
        IsDefault = sender.IsDefault,
        IsActive = sender.IsActive,
        VerificationStatus = sender.VerificationStatus.ToString(),
        SendingDomainId = sender.SendingDomainId,
        DomainName = sender.SendingDomain?.DomainName,

        // Mirrors IEmailDomainService.CanSenderSendAsync clause for clause, so the wizard can
        // explain an unselectable sender rather than letting a campaign fail at dispatch — and so
        // it never hides one the server would have accepted.
        CanSend = CanSend(sender, configuration)
    };

    /// <summary>
    /// The read-only half of the send gate: everything <see cref="IEmailDomainService.CanSenderSendAsync"/>
    /// decides without touching the provider.
    ///
    /// <para>
    /// The gate itself stays authoritative — it can additionally re-read a stale status from SES,
    /// which a projection has no business doing. This answers the same question from what is
    /// already loaded, and must not answer it differently.
    /// </para>
    /// </summary>
    private static bool CanSend(EmailSenderIdentity sender, EmailConfiguration? configuration)
    {
        if (!sender.IsActive) return false;
        if (configuration is { IsActive: false }) return false;

        // SMTP exposes no verification state to query — the relay decides what it will carry, so
        // requiring a verification that cannot exist would make every SMTP sender unusable. This
        // clause was missing here while being present in the gate.
        if (configuration?.Provider == EmailProviderType.Smtp) return true;

        return sender.VerificationStatus == EmailIdentityStatus.Verified
            || sender.SendingDomain?.VerificationStatus == EmailIdentityStatus.Verified;
    }

    /// <summary>
    /// One status string for the connections list, derived rather than stored — a stored status is
    /// a second source of truth that drifts from the fields it summarises.
    /// </summary>
    private static string DeriveStatus(EmailConfiguration configuration)
    {
        // Checked before IsActive, because a connection that has never completed step 2 of the
        // wizard is also inactive — and reporting it as "Disconnected" would both mislead the
        // operator and miscount the dashboard's Connected/Disconnected cards.
        if (configuration.ConfiguredAt is null) return "Setup pending";

        if (!configuration.IsActive) return "Disconnected";

        var hasCredentials = configuration.Provider switch
        {
            EmailProviderType.AmazonSes => configuration.AuthMode == EmailAuthMode.IamRole
                                        || !string.IsNullOrEmpty(configuration.SecretAccessKeyEncrypted),
            EmailProviderType.Smtp => !string.IsNullOrWhiteSpace(configuration.SmtpHost),
            _ => false
        };

        if (!hasCredentials) return "Setup pending";
        if (configuration.SenderIdentities.Count == 0) return "Setup pending";

        // A last test that failed is worth surfacing: the credentials are present but known not
        // to work, which is different from never having been tried.
        if (configuration.LastTestSucceeded == false) return "Needs attention";

        return "Connected";
    }

    /// <summary>
    /// Derives the 4-character nickname the shared connection UI expects. Uses initials when the
    /// name has several words, otherwise the leading characters.
    /// </summary>
    private static string DeriveNickname(string? supplied, string name)
    {
        if (!string.IsNullOrWhiteSpace(supplied))
        {
            var trimmed = supplied.Trim().ToUpperInvariant();
            return trimmed.Length > 4 ? trimmed[..4] : trimmed;
        }

        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var candidate = words.Length > 1
            ? new string(words.Take(4).Select(w => w[0]).ToArray())
            : new string(name.Where(char.IsAsciiLetterOrDigit).Take(4).ToArray());

        candidate = candidate.ToUpperInvariant();
        return string.IsNullOrWhiteSpace(candidate) ? "MAIL" : candidate;
    }

    private string? DecryptOrNull(string? cipherText)
    {
        if (string.IsNullOrWhiteSpace(cipherText)) return null;

        try
        {
            return _encryption.Decrypt(cipherText);
        }
        catch (Exception ex)
        {
            // Swallowed here, unlike in the send path: this is a convenience lookup so the
            // operator does not have to retype a secret. Failing to read the old one just means
            // they do, which is a far better outcome than blocking the test.
            _logger.LogWarning(ex, "Could not decrypt a stored secret while preparing a connection test.");
            return null;
        }
    }

    private static EmailProviderType ParseProvider(string? value) =>
        Enum.TryParse<EmailProviderType>(value, true, out var parsed)
            ? parsed
            : throw new ArgumentException(
                $"Unknown email provider '{value}'. Valid values: {string.Join(", ", Enum.GetNames<EmailProviderType>())}.");

    private static EmailAuthMode ParseAuthMode(string? value) =>
        Enum.TryParse<EmailAuthMode>(value, true, out var parsed) ? parsed : EmailAuthMode.IamRole;

    private static SmtpSecurityMode ParseSmtpSecurity(string? value) =>
        Enum.TryParse<SmtpSecurityMode>(value, true, out var parsed) ? parsed : SmtpSecurityMode.StartTls;

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
