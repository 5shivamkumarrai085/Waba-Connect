using System.Text.Json;
using Amazon;
using Amazon.Runtime;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
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
public class EmailDomainService : IEmailDomainService
{
    private readonly AppDbContext _dbContext;
    private readonly IEncryptionService _encryption;
    private readonly IOptionsMonitor<EmailOptions> _options;
    private readonly IAuditService _auditService;
    private readonly ILogger<EmailDomainService> _logger;

    public EmailDomainService(
        AppDbContext dbContext,
        IEncryptionService encryption,
        IOptionsMonitor<EmailOptions> options,
        IAuditService auditService,
        ILogger<EmailDomainService> logger)
    {
        _dbContext = dbContext;
        _encryption = encryption;
        _options = options;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<List<EmailSendingDomainResponse>> GetAllAsync(
        int emailConfigurationId,
        CancellationToken ct = default)
    {
        var domains = await _dbContext.EmailSendingDomains
            .AsNoTracking()
            .Where(d => d.EmailConfigurationId == emailConfigurationId)
            .OrderBy(d => d.DomainName)
            .ToListAsync(ct);

        return domains.Select(MapToResponse).ToList();
    }

    public async Task<EmailSendingDomainResponse> GetByIdAsync(int domainId, CancellationToken ct = default)
    {
        var domain = await _dbContext.EmailSendingDomains.AsNoTracking().FirstOrDefaultAsync(d => d.Id == domainId, ct)
            ?? throw new KeyNotFoundException("Sending domain not found.");

        return MapToResponse(domain);
    }

    public async Task<EmailSendingDomainResponse> ProvisionAsync(
        int emailConfigurationId,
        string domainName,
        CancellationToken ct = default)
    {
        var configuration = await _dbContext.EmailConfigurations
            .FirstOrDefaultAsync(c => c.Id == emailConfigurationId, ct)
            ?? throw new KeyNotFoundException($"Email connection {emailConfigurationId} not found.");

        var normalized = NormalizeDomain(domainName);

        if (configuration.Provider != EmailProviderType.AmazonSes)
        {
            // SMTP relays have no DKIM provisioning API — whoever runs the relay signs the mail.
            // Refused rather than silently recorded, so nobody waits for a verification that can
            // never arrive.
            throw new InvalidOperationException(
                "Domain verification is only available for Amazon SES. An SMTP relay signs mail on its own "
              + "infrastructure, so SPF and DKIM are configured with whoever operates it.");
        }

        var existing = await _dbContext.EmailSendingDomains
            .FirstOrDefaultAsync(d => d.EmailConfigurationId == emailConfigurationId
                                   && d.DomainName.ToLower() == normalized, ct);

        if (existing is not null)
        {
            // Re-provisioning is a refresh, not an error: SES returns the same DKIM tokens for an
            // identity that already exists, and an operator clicking again usually wants the
            // current status.
            return await RefreshStatusAsync(existing.Id, ct);
        }

        var domain = new EmailSendingDomain
        {
            EmailConfigurationId = emailConfigurationId,
            DomainName = normalized,
            VerificationStatus = EmailIdentityStatus.Pending,
            DkimStatus = EmailIdentityStatus.Pending
        };

        try
        {
            using var client = CreateClient(configuration);

            var response = await client.CreateEmailIdentityAsync(new CreateEmailIdentityRequest
            {
                EmailIdentity = normalized,

                // Easy DKIM: AWS generates and rotates the key pair, and we publish three CNAMEs.
                // The alternative, bring-your-own-DKIM, means handling a private key — which this
                // product has no good place to store and no reason to take on.
                DkimSigningAttributes = null
            }, ct);

            domain.DkimTokensJson = JsonSerializer.Serialize(response.DkimAttributes?.Tokens ?? []);
            domain.DkimStatus = MapDkimStatus(response.DkimAttributes?.Status);
            domain.VerificationStatus = DeriveVerificationStatus(
                response.VerifiedForSendingStatus, domain.DkimStatus);
            domain.LastCheckedAt = DateTime.UtcNow;
        }
        catch (AlreadyExistsException)
        {
            // The identity exists in SES but not in our table — a re-connected account, or a
            // domain verified directly in the AWS console. Adopt it rather than failing.
            _logger.LogInformation(
                "Domain {Domain} already exists in SES; adopting the existing identity.", normalized);
            await PopulateFromSesAsync(configuration, domain, ct);
        }
        catch (NotFoundException ex)
        {
            throw new InvalidOperationException($"SES rejected the domain '{normalized}': {ex.Message}", ex);
        }
        catch (BadRequestException ex)
        {
            throw new ArgumentException($"SES rejected the domain '{normalized}': {ex.Message}", ex);
        }

        _dbContext.EmailSendingDomains.Add(domain);
        await _dbContext.SaveChangesAsync(ct);

        // Any sender already configured on this address now has a domain backing it.
        await LinkSendersAsync(emailConfigurationId, domain, ct);

        await _auditService.LogAsync(
            "EmailDomain.Provisioned", "Settings",
            $"Started verification for sending domain '{normalized}'.",
            nameof(EmailSendingDomain), domain.Id.ToString());

        return MapToResponse(domain);
    }

    public async Task<EmailSendingDomainResponse> RefreshStatusAsync(int domainId, CancellationToken ct = default)
    {
        var domain = await _dbContext.EmailSendingDomains
            .Include(d => d.EmailConfiguration)
            .FirstOrDefaultAsync(d => d.Id == domainId, ct)
            ?? throw new KeyNotFoundException("Sending domain not found.");

        var previousStatus = domain.VerificationStatus;

        try
        {
            await PopulateFromSesAsync(domain.EmailConfiguration, domain, ct);
            domain.LastCheckMessage = null;
        }
        catch (NotFoundException)
        {
            // Deleted in the AWS console behind our back. Recording it as failed is honest, and
            // keeps the send gate closed.
            domain.VerificationStatus = EmailIdentityStatus.Failed;
            domain.DkimStatus = EmailIdentityStatus.Failed;
            domain.LastCheckMessage =
                "This domain no longer exists in SES. It may have been removed in the AWS console — re-add it here.";
        }
        catch (Exception ex)
        {
            // A failed check is not a failed domain: leaving the status alone avoids flipping a
            // verified domain to broken because of a transient AWS error.
            _logger.LogWarning(ex, "Could not refresh SES status for domain {Domain}.", domain.DomainName);
            domain.LastCheckMessage = $"Could not reach SES: {ex.Message}";
        }

        domain.LastCheckedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(ct);

        // Verification flips senders on this domain to usable, which is what opens the send gate.
        if (previousStatus != domain.VerificationStatus)
        {
            await SyncSenderVerificationAsync(domain, ct);

            await _auditService.LogAsync(
                "EmailDomain.StatusChanged", "Settings",
                $"Sending domain '{domain.DomainName}' moved from {previousStatus} to {domain.VerificationStatus}.",
                nameof(EmailSendingDomain), domain.Id.ToString());
        }

        return MapToResponse(domain);
    }

    public async Task DeleteAsync(int domainId, CancellationToken ct = default)
    {
        var domain = await _dbContext.EmailSendingDomains.FirstOrDefaultAsync(d => d.Id == domainId, ct)
            ?? throw new KeyNotFoundException("Sending domain not found.");

        // Restrict on the FK would refuse this anyway; checked here so the answer explains itself.
        var senderCount = await _dbContext.EmailSenderIdentities
            .CountAsync(s => s.SendingDomainId == domainId, ct);

        if (senderCount > 0)
        {
            throw new InvalidOperationException(
                $"{senderCount} sender(s) still use this domain. Remove them before deleting it.");
        }

        var name = domain.DomainName;

        // Deliberately not deleting the SES identity. Another connection, or another application
        // on the same AWS account, may rely on it — and re-verifying a domain means another round
        // of DNS changes. Removing it here only stops us using it.
        _dbContext.EmailSendingDomains.Remove(domain);
        await _dbContext.SaveChangesAsync(ct);

        await _auditService.LogAsync(
            "EmailDomain.Deleted", "Settings",
            $"Removed sending domain '{name}'. The SES identity itself was left in place.",
            nameof(EmailSendingDomain), domainId.ToString());
    }

    public async Task<(bool CanSend, string? Reason)> CanSenderSendAsync(
        int senderIdentityId,
        CancellationToken ct = default)
    {
        var sender = await _dbContext.EmailSenderIdentities
            .AsNoTracking()
            .Include(s => s.SendingDomain)
            .Include(s => s.EmailConfiguration)
            .FirstOrDefaultAsync(s => s.Id == senderIdentityId, ct);

        if (sender is null) return (false, "The sender identity no longer exists.");
        if (!sender.IsActive) return (false, $"Sender {sender.EmailAddress} is deactivated.");

        if (sender.EmailConfiguration is { IsActive: false })
        {
            return (false, "The email connection for this sender is disconnected.");
        }

        // SMTP has no verification concept we can query — the relay decides what it will carry.
        // Requiring a verification that cannot exist would make SMTP unusable.
        if (sender.EmailConfiguration?.Provider == EmailProviderType.Smtp) return (true, null);

        // An individually verified address is enough on its own; otherwise the domain must be
        // verified. Either satisfies SES.
        if (sender.VerificationStatus == EmailIdentityStatus.Verified) return (true, null);
        if (sender.SendingDomain?.VerificationStatus == EmailIdentityStatus.Verified) return (true, null);

        // Neither is verified *according to what we last stored*, which is not the same thing as
        // not being verified. An address is routinely verified in the AWS console after being
        // added here, and an operator has no way to know they would have to re-add it — so ask
        // SES before refusing, rather than trusting a possibly pre-verification answer.
        //
        // Bounded by the staleness window so a genuinely unverified sender on a large campaign
        // does not turn one refusal into one SES call per recipient.
        if (IsStale(sender.LastCheckedAt))
        {
            var (refreshed, _) = await RefreshSenderStatusAsync(senderIdentityId, ct);
            if (refreshed == EmailIdentityStatus.Verified) return (true, null);

            // Only the verdict is used, not the refresh's own wording. When SES cannot be reached
            // the refresh reports that, and surfacing it here would replace "this sender is not
            // verified" with a transport error — which reads as though the verification question
            // had been answered when it has not been asked. The explanations below stay the
            // gate's, and the refresh's wording belongs to the endpoint that was asked to check.
        }

        if (sender.SendingDomain is null)
        {
            var domain = sender.EmailAddress.Contains('@') ? sender.EmailAddress.Split('@')[1] : sender.EmailAddress;
            return (false,
                $"{sender.EmailAddress} is not a verified SES identity, and there is no verified sending "
              + $"domain for it. Verify the address in SES, or add and verify '{domain}'.");
        }

        return (false,
            $"Sending domain '{sender.SendingDomain.DomainName}' is {sender.SendingDomain.VerificationStatus}. "
          + "Publish its DNS records and wait for verification before sending.");
    }

    /// <summary>
    /// How long a stored verification answer is trusted before the provider is asked again.
    ///
    /// <para>
    /// Only ever consulted on the unhappy path — a sender already stored as Verified is not
    /// re-checked here, because the dispatch worker re-runs this gate per batch and a verified
    /// identity does not silently un-verify. So this window costs at most one SES call per sender
    /// per five minutes, and buys an address verified in the console becoming usable without the
    /// operator having to discover a button.
    /// </para>
    /// </summary>
    private static readonly TimeSpan SenderStatusStaleAfter = TimeSpan.FromMinutes(5);

    private static bool IsStale(DateTime? lastCheckedAt) =>
        lastCheckedAt is null || DateTime.UtcNow - lastCheckedAt.Value > SenderStatusStaleAfter;

    public async Task<(EmailIdentityStatus Status, string? Reason)> RefreshSenderStatusAsync(
        int senderIdentityId,
        CancellationToken ct = default)
    {
        var sender = await _dbContext.EmailSenderIdentities
            .Include(s => s.EmailConfiguration)
            .FirstOrDefaultAsync(s => s.Id == senderIdentityId, ct);

        if (sender is null) return (EmailIdentityStatus.NotStarted, "The sender identity no longer exists.");

        var configuration = sender.EmailConfiguration;

        if (configuration is null)
        {
            return (sender.VerificationStatus, "This sender is not linked to an email connection.");
        }

        // SMTP exposes no verification state to query — the relay decides what it will carry, and
        // inventing a status for it would be a guess dressed up as a fact.
        if (configuration.Provider != EmailProviderType.AmazonSes)
        {
            return (sender.VerificationStatus, null);
        }

        var previousStatus = sender.VerificationStatus;

        try
        {
            // The client is built from this connection's stored Region, so the lookup happens in
            // the region the identity was actually verified in. An identity is regional in SES:
            // asking us-west-2 about an address verified in us-east-1 correctly returns NotFound.
            using var client = CreateClient(configuration);

            var identity = await client.GetEmailIdentityAsync(
                new GetEmailIdentityRequest { EmailIdentity = sender.EmailAddress }, ct);

            // Read VerifiedForSendingStatus alone, and deliberately not DkimAttributes: an
            // email-address identity has no DKIM of its own, so SES reports NOT_STARTED for it.
            // Feeding that through the domain helper (DeriveVerificationStatus) would downgrade a
            // genuinely verified address to Pending — which is exactly the bug this method exists
            // to fix, reintroduced one layer down.
            //
            // Sandbox is not consulted either. VerifiedForSendingStatus is per-identity and is
            // true in the sandbox; the sandbox limits who may be *written to* and how much, which
            // is an account fact and says nothing about whether this address is verified.
            sender.VerificationStatus = identity.VerifiedForSendingStatus == true
                ? EmailIdentityStatus.Verified
                : EmailIdentityStatus.Pending;
        }
        catch (NotFoundException)
        {
            // The address is not an identity in this region at all. Distinct from Pending, which
            // means SES knows it and is waiting — this means nobody has started.
            sender.VerificationStatus = EmailIdentityStatus.NotStarted;

            _logger.LogInformation(
                "Sender {Email} is not an SES identity in region {Region}.",
                sender.EmailAddress, configuration.Region ?? "(default)");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A credential or network failure is not evidence about the address, so the stored
            // status is left alone rather than being downgraded by an unrelated outage.
            _logger.LogWarning(ex,
                "Could not read SES verification for sender {Email} in region {Region}.",
                sender.EmailAddress, configuration.Region ?? "(default)");

            return (sender.VerificationStatus,
                sender.VerificationStatus == EmailIdentityStatus.Verified
                    ? null
                    : $"Could not reach SES to check {sender.EmailAddress}: {ex.Message}");
        }

        sender.LastCheckedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(ct);

        if (previousStatus != sender.VerificationStatus)
        {
            _logger.LogInformation(
                "Sender {Email} moved from {Previous} to {Current}.",
                sender.EmailAddress, previousStatus, sender.VerificationStatus);
        }

        return sender.VerificationStatus switch
        {
            EmailIdentityStatus.Verified => (EmailIdentityStatus.Verified, null),
            EmailIdentityStatus.NotStarted => (EmailIdentityStatus.NotStarted,
                $"{sender.EmailAddress} is not a verified identity in SES region "
              + $"{configuration.Region ?? "(default)"}. Verify the address in SES, or verify its domain."),
            _ => (sender.VerificationStatus,
                $"{sender.EmailAddress} is {sender.VerificationStatus} in SES. "
              + "Confirm the verification email, then re-check.")
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────

    private async Task PopulateFromSesAsync(
        EmailConfiguration configuration,
        EmailSendingDomain domain,
        CancellationToken ct)
    {
        using var client = CreateClient(configuration);

        var identity = await client.GetEmailIdentityAsync(
            new GetEmailIdentityRequest { EmailIdentity = domain.DomainName }, ct);

        domain.DkimStatus = MapDkimStatus(identity.DkimAttributes?.Status);
        domain.VerificationStatus = DeriveVerificationStatus(
            identity.VerifiedForSendingStatus, domain.DkimStatus);

        if (identity.DkimAttributes?.Tokens is { Count: > 0 } tokens)
        {
            domain.DkimTokensJson = JsonSerializer.Serialize(tokens);
        }

        // A custom MAIL FROM subdomain is what makes SPF pass under DMARC *alignment*, rather
        // than only DKIM carrying the domain. Recorded so the UI can show its own records.
        if (identity.MailFromAttributes is { } mailFrom)
        {
            domain.MailFromDomain = mailFrom.MailFromDomain;
            domain.MailFromStatus = MapMailFromStatus(mailFrom.MailFromDomainStatus);
        }
    }

    /// <summary>
    /// Points senders on this domain at it, so the send gate can resolve verification in one hop
    /// rather than re-parsing addresses per recipient.
    /// </summary>
    private async Task LinkSendersAsync(int configurationId, EmailSendingDomain domain, CancellationToken ct)
    {
        var suffix = "@" + domain.DomainName;

        var senders = await _dbContext.EmailSenderIdentities
            .Where(s => s.EmailConfigurationId == configurationId
                     && s.SendingDomainId == null
                     && s.EmailAddress.ToLower().EndsWith(suffix))
            .ToListAsync(ct);

        if (senders.Count == 0) return;

        foreach (var sender in senders) sender.SendingDomainId = domain.Id;
        await _dbContext.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Mirrors a newly verified domain onto its senders.
    ///
    /// <para>
    /// Only ever promotes. A domain check that comes back Pending must not demote a sender that
    /// SES verified individually — those are independent facts, and conflating them would close
    /// the send gate on a sender that is genuinely fine.
    /// </para>
    /// </summary>
    private async Task SyncSenderVerificationAsync(EmailSendingDomain domain, CancellationToken ct)
    {
        if (domain.VerificationStatus != EmailIdentityStatus.Verified) return;

        var senders = await _dbContext.EmailSenderIdentities
            .Where(s => s.SendingDomainId == domain.Id && s.VerificationStatus != EmailIdentityStatus.Verified)
            .ToListAsync(ct);

        if (senders.Count == 0) return;

        foreach (var sender in senders) sender.VerificationStatus = EmailIdentityStatus.Verified;
        await _dbContext.SaveChangesAsync(ct);
    }

    private EmailSendingDomainResponse MapToResponse(EmailSendingDomain domain) => new()
    {
        Id = domain.Id,
        DomainName = domain.DomainName,
        VerificationStatus = domain.VerificationStatus.ToString(),
        DkimStatus = domain.DkimStatus.ToString(),
        MailFromDomain = domain.MailFromDomain,
        MailFromStatus = domain.MailFromStatus.ToString(),
        LastCheckedAt = domain.LastCheckedAt,
        LastCheckMessage = domain.LastCheckMessage,
        RequiredDnsRecords = BuildDnsRecords(domain)
    };

    /// <summary>
    /// The DNS records an administrator has to publish.
    ///
    /// <para>
    /// Returned as data rather than rendered into prose, so the UI can offer copy buttons per
    /// record — transcribing a DKIM CNAME by hand is where domain setup usually goes wrong. The
    /// SPF include and the DMARC policy come from configuration, not from literals here, because
    /// they differ by provider and by how strict the customer wants to be.
    /// </para>
    /// </summary>
    private List<DnsRecordResponse> BuildDnsRecords(EmailSendingDomain domain)
    {
        var records = new List<DnsRecordResponse>();
        var ses = _options.CurrentValue.Ses;

        // DKIM: three CNAMEs pointing at AWS-managed keys. Required — without them SES cannot
        // sign, and unsigned mail fails DMARC.
        if (!string.IsNullOrWhiteSpace(domain.DkimTokensJson))
        {
            try
            {
                var tokens = JsonSerializer.Deserialize<List<string>>(domain.DkimTokensJson) ?? [];
                records.AddRange(tokens.Select(token => new DnsRecordResponse(
                    "CNAME",
                    $"{token}._domainkey.{domain.DomainName}",
                    $"{token}.dkim.amazonses.com",
                    "DKIM")));
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Stored DKIM tokens for {Domain} could not be read.", domain.DomainName);
            }
        }

        // SPF: authorises the provider to send as this domain.
        records.Add(new DnsRecordResponse(
            "TXT",
            domain.DomainName,
            $"v=spf1 include:{ses.SpfInclude} ~all",
            "SPF"));

        // DMARC: tells receivers what to do when SPF and DKIM disagree, and asks for reports.
        // The template starts at p=none deliberately — a new domain moving straight to p=reject
        // will reject its own legitimate mail before anyone has read a report.
        var dmarc = ses.DmarcPolicyTemplate.Replace("{rua}", $"dmarc-reports@{domain.DomainName}");
        records.Add(new DnsRecordResponse("TXT", $"_dmarc.{domain.DomainName}", dmarc, "DMARC"));

        // Custom MAIL FROM, when configured. Not required to send, but it is what lets SPF pass
        // in alignment rather than relying on DKIM alone.
        if (!string.IsNullOrWhiteSpace(domain.MailFromDomain))
        {
            records.Add(new DnsRecordResponse(
                "MX",
                domain.MailFromDomain,
                "10 feedback-smtp.{region}.amazonses.com",
                "MAIL FROM",
                Required: false));

            records.Add(new DnsRecordResponse(
                "TXT",
                domain.MailFromDomain,
                $"v=spf1 include:{ses.SpfInclude} ~all",
                "MAIL FROM",
                Required: false));
        }

        return records;
    }

    private AmazonSimpleEmailServiceV2Client CreateClient(EmailConfiguration configuration)
    {
        var region = string.IsNullOrWhiteSpace(configuration.Region)
            ? null
            : RegionEndpoint.GetBySystemName(configuration.Region);

        if (configuration.AuthMode == EmailAuthMode.IamRole)
        {
            return region is null
                ? new AmazonSimpleEmailServiceV2Client()
                : new AmazonSimpleEmailServiceV2Client(region);
        }

        if (string.IsNullOrWhiteSpace(configuration.AccessKeyId)
            || string.IsNullOrWhiteSpace(configuration.SecretAccessKeyEncrypted))
        {
            throw new InvalidOperationException(
                "This email connection has no stored AWS credentials. Configure the provider before adding a domain.");
        }

        var credentials = new BasicAWSCredentials(
            configuration.AccessKeyId,
            _encryption.Decrypt(configuration.SecretAccessKeyEncrypted));

        return region is null
            ? new AmazonSimpleEmailServiceV2Client(credentials)
            : new AmazonSimpleEmailServiceV2Client(credentials, region);
    }

    /// <summary>
    /// Combines SES's two separate answers into the one status this product shows.
    ///
    /// <para>
    /// SES reports "is this identity allowed to send" as a plain boolean and DKIM progress as its
    /// own enum, and the two can disagree — a domain can be sendable while DKIM is still
    /// propagating. The boolean is authoritative for the send gate, so a true there means
    /// Verified; otherwise the DKIM status is the informative one, because DKIM is what the
    /// operator is actually waiting on.
    /// </para>
    /// </summary>
    private static EmailIdentityStatus DeriveVerificationStatus(bool? verifiedForSending, EmailIdentityStatus dkimStatus)
    {
        if (verifiedForSending == true) return EmailIdentityStatus.Verified;

        return dkimStatus switch
        {
            // DKIM finished but SES still will not send: almost always a brand-new identity
            // mid-propagation, so Pending is the honest answer rather than Verified.
            EmailIdentityStatus.Verified => EmailIdentityStatus.Pending,
            EmailIdentityStatus.NotStarted => EmailIdentityStatus.Pending,
            _ => dkimStatus
        };
    }

    private static EmailIdentityStatus MapDkimStatus(DkimStatus? status) => status?.Value switch
    {
        "SUCCESS" => EmailIdentityStatus.Verified,
        "PENDING" => EmailIdentityStatus.Pending,
        "FAILED" => EmailIdentityStatus.Failed,
        "TEMPORARY_FAILURE" => EmailIdentityStatus.TemporaryFailure,
        "NOT_STARTED" => EmailIdentityStatus.NotStarted,
        _ => EmailIdentityStatus.Pending
    };

    private static EmailIdentityStatus MapMailFromStatus(MailFromDomainStatus? status) => status?.Value switch
    {
        "SUCCESS" => EmailIdentityStatus.Verified,
        "PENDING" => EmailIdentityStatus.Pending,
        "FAILED" => EmailIdentityStatus.Failed,
        "TEMPORARY_FAILURE" => EmailIdentityStatus.TemporaryFailure,
        _ => EmailIdentityStatus.NotStarted
    };

    /// <summary>
    /// Normalises operator input into a bare domain. People paste "https://example.com/",
    /// "user@example.com" and "EXAMPLE.COM"; SES accepts exactly one of those.
    /// </summary>
    private static string NormalizeDomain(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) throw new ArgumentException("A domain name is required.");

        var value = input.Trim().ToLowerInvariant();

        if (value.Contains("://")) value = value.Split("://", 2)[1];
        if (value.Contains('@')) value = value.Split('@')[^1];

        value = value.Split('/')[0].Split(':')[0].Trim('.');

        if (!value.Contains('.') || value.Contains(' '))
        {
            throw new ArgumentException($"'{input}' is not a valid domain name.");
        }

        return value;
    }
}
