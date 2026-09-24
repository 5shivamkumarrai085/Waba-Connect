using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Email;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Email;

namespace EmailChannel.Tests;

/// <summary>
/// Phase 3 — the send gate and the DNS records an operator has to publish.
///
/// <para>
/// Provisioning itself calls AWS, so it cannot be exercised without real credentials. What can be
/// verified without them — and is what actually protects the customer — is the decision logic:
/// that an unverified SES sender is refused, that SMTP is not held to a verification it has no
/// concept of, and that the DNS records offered are the right ones. Domain rows are inserted
/// directly to set up each state, which is the only way to test the Verified branch without a
/// real verified domain.
/// </para>
/// </summary>
public static class Phase3_DomainTests
{
    public static async Task RunAsync(TestHarness harness, TestRun run)
    {
        run.BeginPhase("Phase 3 — domain authentication and the send gate");

        using var scope = harness.CreateScope();
        var connections = scope.ServiceProvider.GetRequiredService<IEmailConnectionService>();
        var domains = scope.ServiceProvider.GetRequiredService<IEmailDomainService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var domainName = $"{harness.Tag}.example.test";

        try
        {
            var created = await connections.CreateAsync(new CreateEmailConnectionRequest
            {
                Name = $"{TestHarness.Prefix}-domain-{harness.Tag}",
                DisplayName = "Domain Test",
                EmailAddress = $"sender@{domainName}"
            });

            var senderId = (await connections.GetSendersAsync(created.Id)).Single().Id;

            run.Section("SMTP is not held to a verification it cannot have");

            await connections.SaveProviderAsync(created.Id, new SaveEmailProviderRequest
            {
                Provider = "Smtp", SmtpHost = "smtp.example.invalid", SmtpPort = 587,
                SmtpSecurity = "StartTls", IsActive = true
            });

            var (smtpAllowed, smtpReason) = await domains.CanSenderSendAsync(senderId);
            run.Check("an SMTP sender may send without a verified domain", smtpAllowed, smtpReason);

            run.Section("An unverified SES sender is refused");

            await connections.SaveProviderAsync(created.Id, new SaveEmailProviderRequest
            {
                Provider = "AmazonSes", Region = "ap-southeast-1", AuthMode = "IamRole", IsActive = true
            });

            var (unverifiedAllowed, unverifiedReason) = await domains.CanSenderSendAsync(senderId);
            run.Check("an unverified SES sender cannot send", !unverifiedAllowed);
            run.Check("the refusal says what to do about it",
                unverifiedReason is not null
                && unverifiedReason.Contains("verif", StringComparison.OrdinalIgnoreCase),
                unverifiedReason);
            run.Check("the refusal names the domain that needs verifying",
                unverifiedReason is not null && unverifiedReason.Contains(domainName), unverifiedReason);

            run.Section("A pending domain still blocks");

            // Inserted directly: reaching Pending through the service would require a real SES
            // call, and the state itself is what is being tested.
            var domain = new EmailSendingDomain
            {
                EmailConfigurationId = created.Id,
                DomainName = domainName,
                VerificationStatus = EmailIdentityStatus.Pending,
                DkimStatus = EmailIdentityStatus.Pending,
                DkimTokensJson = """["tokenaaa","tokenbbb","tokenccc"]"""
            };
            db.EmailSendingDomains.Add(domain);
            await db.SaveChangesAsync();

            var sender = await db.EmailSenderIdentities.FirstAsync(s => s.Id == senderId);
            sender.SendingDomainId = domain.Id;
            await db.SaveChangesAsync();

            var (pendingAllowed, pendingReason) = await domains.CanSenderSendAsync(senderId);
            run.Check("a domain mid-verification does not open the gate", !pendingAllowed);
            run.Check("the reason reports the actual status",
                pendingReason is not null && pendingReason.Contains("Pending"), pendingReason);

            run.Section("A verified domain opens the gate");

            domain.VerificationStatus = EmailIdentityStatus.Verified;
            domain.DkimStatus = EmailIdentityStatus.Verified;
            await db.SaveChangesAsync();

            var (verifiedAllowed, verifiedReason) = await domains.CanSenderSendAsync(senderId);
            run.Check("a verified domain lets its senders send", verifiedAllowed, verifiedReason);

            run.Section("A deactivated sender is refused regardless");

            sender.IsActive = false;
            await db.SaveChangesAsync();

            var (inactiveAllowed, inactiveReason) = await domains.CanSenderSendAsync(senderId);
            run.Check("a deactivated sender cannot send even on a verified domain", !inactiveAllowed);
            run.Check("the reason says the sender is deactivated",
                inactiveReason is not null
                && inactiveReason.Contains("deactivated", StringComparison.OrdinalIgnoreCase),
                inactiveReason);

            sender.IsActive = true;
            await db.SaveChangesAsync();

            run.Section("A disconnected connection closes the gate");

            await connections.DisconnectAsync(created.Id);

            var (disconnectedAllowed, disconnectedReason) = await domains.CanSenderSendAsync(senderId);
            run.Check("a sender on a disconnected connection cannot send", !disconnectedAllowed);
            run.Check("the reason blames the connection, not the domain",
                disconnectedReason is not null
                && disconnectedReason.Contains("disconnected", StringComparison.OrdinalIgnoreCase),
                disconnectedReason);

            run.Section("An individually verified address needs no verified domain");

            // The section above deliberately left the connection disconnected. Reconnect it, or
            // every check below would be measuring that instead of what it means to.
            await connections.SaveProviderAsync(created.Id, new SaveEmailProviderRequest
            {
                Provider = "AmazonSes", Region = "ap-southeast-1", AuthMode = "IamRole", IsActive = true
            });

            // The defect this section exists for: nothing ever asked SES about an *email-address*
            // identity. VerificationStatus was written NotStarted at creation and never updated,
            // so an address verified in SES — the normal way to send from a mailbox on a domain
            // you do not control, and the only way while in the sandbox — was refused forever.
            //
            // Set directly here because reaching Verified through the service needs a real SES
            // call; what is under test is that the gate honours the address status at all.
            var addressOnly = new EmailSenderIdentity
            {
                EmailConfigurationId = created.Id,
                DisplayName = "Address Identity",
                EmailAddress = $"{harness.Tag}@gmail.test",
                IsActive = true,
                // No SendingDomainId: gmail.test is not a domain this account could ever verify.
                SendingDomainId = null,
                VerificationStatus = EmailIdentityStatus.Verified,
                // Fresh, so the gate trusts it rather than trying to re-read it from SES.
                LastCheckedAt = DateTime.UtcNow
            };
            db.EmailSenderIdentities.Add(addressOnly);
            await db.SaveChangesAsync();

            var (addressAllowed, addressReason) =
                await domains.CanSenderSendAsync(addressOnly.Id);
            run.Check("a verified address can send with no sending domain at all",
                addressAllowed, addressReason);

            // Sandbox restricts recipients and quota — an account-level fact. It must not change
            // the answer to "is this identity verified", which is per-identity.
            run.Check("the verdict does not depend on production access",
                addressAllowed, addressReason);

            addressOnly.VerificationStatus = EmailIdentityStatus.NotStarted;
            await db.SaveChangesAsync();

            var (unverifiedAddressAllowed, unverifiedAddressReason) =
                await domains.CanSenderSendAsync(addressOnly.Id);
            run.Check("an unverified address is still refused", !unverifiedAddressAllowed);
            run.Check("and the refusal names the address rather than blaming a domain",
                unverifiedAddressReason is not null
                && unverifiedAddressReason.Contains(addressOnly.EmailAddress),
                unverifiedAddressReason);

            run.Section("The API's CanSend agrees with the send gate");

            // MapSender carried its own copy of the gate that omitted the SMTP exemption and the
            // connection-inactive check, so the wizard hid senders the server would have accepted.
            // These assertions pin the two together.
            async Task CheckAgreementAsync(string label)
            {
                var projected = (await connections.GetSendersAsync(created.Id)).ToList();

                foreach (var dto in projected)
                {
                    var (gateAllows, _) = await domains.CanSenderSendAsync(dto.Id);
                    run.Check($"{label}: {dto.EmailAddress} — API CanSend matches the gate",
                        dto.CanSend == gateAllows,
                        $"api={dto.CanSend} gate={gateAllows}");
                }
            }

            addressOnly.VerificationStatus = EmailIdentityStatus.Verified;
            await db.SaveChangesAsync();
            await CheckAgreementAsync("SES, verified address");

            // The case the old copy got wrong outright.
            await connections.SaveProviderAsync(created.Id, new SaveEmailProviderRequest
            {
                Provider = "Smtp", SmtpHost = "smtp.example.invalid", SmtpPort = 587,
                SmtpSecurity = "StartTls", IsActive = true
            });

            var smtpProjected = await connections.GetSendersAsync(created.Id);
            run.Check("an SMTP sender is offered by the API, not hidden as unverified",
                smtpProjected.All(dto => dto.CanSend),
                string.Join(" | ", smtpProjected.Select(d => $"{d.EmailAddress}={d.CanSend}")));

            await CheckAgreementAsync("SMTP");

            // Put it back for the sections that follow.
            await connections.SaveProviderAsync(created.Id, new SaveEmailProviderRequest
            {
                Provider = "AmazonSes", Region = "ap-southeast-1", AuthMode = "IamRole", IsActive = true
            });

            run.Section("Refreshing a sender records when it was asked");

            // SMTP has no verification state to query, so a refresh must not invent one.
            await connections.SaveProviderAsync(created.Id, new SaveEmailProviderRequest
            {
                Provider = "Smtp", SmtpHost = "smtp.example.invalid", SmtpPort = 587,
                SmtpSecurity = "StartTls", IsActive = true
            });

            var beforeStatus = addressOnly.VerificationStatus;
            var (smtpRefreshStatus, _) = await domains.RefreshSenderStatusAsync(addressOnly.Id);
            run.Check("refreshing an SMTP sender leaves its stored status alone",
                smtpRefreshStatus == beforeStatus, $"{beforeStatus} -> {smtpRefreshStatus}");

            await connections.SaveProviderAsync(created.Id, new SaveEmailProviderRequest
            {
                Provider = "AmazonSes", Region = "ap-southeast-1", AuthMode = "IamRole", IsActive = true
            });

            run.Section("DNS records offered to the operator");

            var response = await domains.GetByIdAsync(domain.Id);
            var records = response.RequiredDnsRecords;

            var dkim = records.Where(r => r.Purpose == "DKIM").ToList();
            run.Check("three DKIM CNAMEs are offered, one per SES token", dkim.Count == 3, $"{dkim.Count}");
            run.Check("DKIM records are CNAMEs at the _domainkey host",
                dkim.All(r => r.Type == "CNAME" && r.Name.Contains("._domainkey.")),
                string.Join(" | ", dkim.Select(r => r.Name)));
            run.Check("DKIM records point at the provider's signing host",
                dkim.All(r => r.Value.EndsWith(".dkim.amazonses.com")),
                string.Join(" | ", dkim.Select(r => r.Value)));

            var spf = records.SingleOrDefault(r => r.Purpose == "SPF");
            run.Check("an SPF record is offered", spf is not null);
            run.Check("SPF is a TXT record on the domain itself",
                spf is { Type: "TXT" } && spf.Name == domainName, $"{spf?.Type} {spf?.Name}");
            run.Check("SPF authorises the configured provider include",
                spf is not null && spf.Value.Contains("include:amazonses.com"), spf?.Value);

            var dmarc = records.SingleOrDefault(r => r.Purpose == "DMARC");
            run.Check("a DMARC record is offered", dmarc is not null);
            run.Check("DMARC is published at _dmarc",
                dmarc is not null && dmarc.Name == $"_dmarc.{domainName}", dmarc?.Name);

            // p=none deliberately. A brand-new domain moving straight to p=reject starts
            // rejecting its own legitimate mail before anyone has read a single report.
            run.Check("DMARC starts at p=none rather than rejecting immediately",
                dmarc is not null && dmarc.Value.Contains("p=none"), dmarc?.Value);
            run.Check("DMARC asks for aggregate reports",
                dmarc is not null && dmarc.Value.Contains("rua=mailto:"), dmarc?.Value);

            run.Check("every offered record is marked required",
                records.Where(r => r.Purpose is "DKIM" or "SPF" or "DMARC").All(r => r.Required));

            run.Section("Domain input is normalised");

            // Put the connection back on SMTP for this section. ProvisionAsync normalises the
            // input first and only then rejects a non-SES provider, so that refusal is a clean
            // signal that parsing succeeded — and, unlike the SES path, it reaches no network.
            //
            // Changed through EF rather than with a raw UPDATE: the service shares this scope's
            // DbContext, which already has the configuration tracked, so a direct SQL write would
            // leave it reading the stale cached Provider and calling AWS anyway.
            var configuration = await db.EmailConfigurations.FirstAsync(c => c.Id == created.Id);
            configuration.Provider = EmailProviderType.Smtp;
            await db.SaveChangesAsync();

            // Operators paste URLs and mailbox addresses; SES accepts neither.
            foreach (var input in new[]
            {
                "HTTPS://Example.Test/path",
                "user@Example.Test",
                "  example.test.  ",
                "example.test:443"
            })
            {
                var parsedThenRefusedByProvider = false;
                string? message = null;

                try
                {
                    await domains.ProvisionAsync(created.Id, input);
                }
                catch (InvalidOperationException ex)
                {
                    // Reached the provider check, which means normalisation accepted it.
                    parsedThenRefusedByProvider = true;
                    message = ex.Message;
                }
                catch (ArgumentException ex)
                {
                    // Normalisation rejected it, which for these inputs is the failure.
                    message = ex.Message;
                }

                run.Check($"'{input.Trim()}' is normalised into a usable domain",
                    parsedThenRefusedByProvider,
                    message ?? "expected normalisation to succeed and the provider check to refuse");
            }

            foreach (var malformed in new[] { "not-a-domain", "two words.test", "" })
            {
                var rejectedAsMalformed = false;
                try
                {
                    await domains.ProvisionAsync(created.Id, malformed);
                }
                catch (ArgumentException) { rejectedAsMalformed = true; }
                catch (InvalidOperationException) { /* parsed, so it was not treated as malformed */ }

                run.Check($"'{malformed}' is rejected as malformed", rejectedAsMalformed);
            }
        }
        catch (Exception ex)
        {
            run.Error("domain phase threw", ex);
        }
    }
}
