using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppCampaignApi.Models.DTOs.Email;
using WhatsAppCampaignApi.Services.Email;
using WhatsAppCampaignApi.Services.Interfaces;

namespace EmailChannel.Tests;

/// <summary>
/// Phase 2 — provider configuration, credential handling and the MIME builder.
///
/// <para>
/// The credential assertions here are the ones worth being strict about. A stored provider secret
/// is the single most damaging thing in this feature to get wrong, so these check the database
/// column and the serialised API response directly rather than trusting the DTO's shape — the
/// question is not "does the response type have a secret property" but "can a secret reach anyone
/// who can read a response body".
/// </para>
/// </summary>
public static class Phase2_ConnectionTests
{
    private const string TestSecret = "super-secret-smtp-password-12345";

    public static async Task RunAsync(TestHarness harness, TestRun run)
    {
        run.BeginPhase("Phase 2 — providers, credentials and MIME");

        using var scope = harness.CreateScope();
        var connections = scope.ServiceProvider.GetRequiredService<IEmailConnectionService>();
        var mimeBuilder = scope.ServiceProvider.GetRequiredService<IMimeMessageBuilder>();
        var encryption = scope.ServiceProvider.GetRequiredService<IEncryptionService>();
        var providerFactory = scope.ServiceProvider.GetRequiredService<IEmailProviderFactory>();

        var name = $"{TestHarness.Prefix}-conn-{harness.Tag}";
        var configId = 0;

        try
        {
            run.Section("Wizard step 1 — create the connection");

            var created = await connections.CreateAsync(new CreateEmailConnectionRequest
            {
                Name = name,
                DisplayName = "OmniConnect Marketing",
                EmailAddress = $"marketing-{harness.Tag}@example.test",
                ReplyToEmail = $"support-{harness.Tag}@example.test",
                Description = "Integration test connection"
            });
            configId = created.Id;

            run.Check("a configuration was created", created.Id > 0);
            run.Check("it is linked to a Connection row", created.ConnectionId is > 0);
            run.Check("it starts inactive until a provider is configured", !created.IsActive);

            // "Setup pending", not "Disconnected". Those look identical in the data — both are
            // inactive with no usable credential — which is why ConfiguredAt is recorded. Getting
            // this wrong would miscount the dashboard's Connected/Disconnected cards.
            run.Check("status reads 'Setup pending', not 'Disconnected'",
                created.Status == "Setup pending", created.Status);

            run.Check("a first sender identity was created with it",
                created.Senders.Count == 1, $"{created.Senders.Count}");
            run.Check("that sender is the default", created.Senders.FirstOrDefault()?.IsDefault == true);
            run.Check("the sender starts unverified, so the send gate stays closed",
                created.Senders.FirstOrDefault()?.VerificationStatus == "NotStarted",
                created.Senders.FirstOrDefault()?.VerificationStatus);
            run.Check("and therefore cannot send yet",
                created.Senders.FirstOrDefault()?.CanSend == false);

            run.Check("a nickname was derived for the shared connection UI",
                created.Nickname is { Length: > 0 and <= 4 }, created.Nickname);

            var duplicateRejected = false;
            try
            {
                await connections.CreateAsync(new CreateEmailConnectionRequest
                {
                    Name = name, DisplayName = "x", EmailAddress = "x@example.test"
                });
            }
            catch (InvalidOperationException) { duplicateRejected = true; }
            run.Check("a duplicate connection name is rejected as a conflict", duplicateRejected);

            run.Section("Credential storage");

            var saved = await connections.SaveProviderAsync(configId, new SaveEmailProviderRequest
            {
                Provider = "Smtp",
                SmtpHost = "smtp.example.invalid",
                SmtpPort = 587,
                SmtpSecurity = "StartTls",
                SmtpUsername = "apikey",
                SmtpPassword = TestSecret,
                MaxSendRatePerSecond = 14,
                IsActive = true
            });

            var storedCipher = await harness.ScalarAsync(
                """SELECT "SmtpPasswordEncrypted" FROM "EmailConfigurations" WHERE "Id" = @id""",
                ("id", configId));

            run.Check("a secret was stored", !string.IsNullOrEmpty(storedCipher));
            run.Check("the secret is NOT stored in plain text", storedCipher != TestSecret);
            run.Check("the stored ciphertext does not contain the plaintext",
                storedCipher is not null && !storedCipher.Contains("super-secret"));
            run.Check("the ciphertext decrypts back to the original",
                storedCipher is not null && encryption.Decrypt(storedCipher) == TestSecret);
            run.Check("the response exposes only a has-secret flag", saved.HasSmtpPassword);

            var serialised = JsonSerializer.Serialize(saved);
            run.Check("the serialised response contains no secret value", !serialised.Contains(TestSecret));
            run.Check("the serialised response contains no ciphertext either",
                storedCipher is null || !serialised.Contains(storedCipher));

            run.Check("status is Connected once credentials and a sender exist",
                saved.Status == "Connected", saved.Status);
            run.Check("ConfiguredAt is now stamped", saved.ConfiguredAt is not null);

            run.Section("A blank secret means 'keep the stored one'");

            await connections.SaveProviderAsync(configId, new SaveEmailProviderRequest
            {
                Provider = "Smtp",
                SmtpHost = "smtp.example.invalid",
                SmtpPort = 2525,        // changed
                SmtpSecurity = "StartTls",
                SmtpUsername = "apikey",
                SmtpPassword = null,    // deliberately omitted
                IsActive = true
            });

            var afterBlank = await harness.ScalarAsync(
                """SELECT "SmtpPasswordEncrypted" FROM "EmailConfigurations" WHERE "Id" = @id""",
                ("id", configId));
            var portAfter = await harness.ScalarAsync(
                """SELECT "SmtpPort" FROM "EmailConfigurations" WHERE "Id" = @id""", ("id", configId));

            run.Check("the stored secret survives an edit that omits it", afterBlank == storedCipher);
            run.Check("the non-secret field did change", portAfter == "2525", portAfter);

            run.Section("Switching to an IAM role leaves no usable key behind");

            await connections.SaveProviderAsync(configId, new SaveEmailProviderRequest
            {
                Provider = "AmazonSes",
                Region = "ap-southeast-1",
                AuthMode = "AccessKey",
                AccessKeyId = "AKIAIOSFODNN7EXAMPLE",
                SecretAccessKey = "an-aws-secret-value",
                IsActive = true
            });

            var sesCipher = await harness.ScalarAsync(
                """SELECT "SecretAccessKeyEncrypted" FROM "EmailConfigurations" WHERE "Id" = @id""",
                ("id", configId));
            run.Check("the AWS secret is stored encrypted",
                !string.IsNullOrEmpty(sesCipher) && sesCipher != "an-aws-secret-value");

            await connections.SaveProviderAsync(configId, new SaveEmailProviderRequest
            {
                Provider = "AmazonSes", Region = "ap-southeast-1", AuthMode = "IamRole", IsActive = true
            });

            var afterRole = await harness.ScalarAsync(
                """SELECT "SecretAccessKeyEncrypted" FROM "EmailConfigurations" WHERE "Id" = @id""",
                ("id", configId));
            var keyIdAfterRole = await harness.ScalarAsync(
                """SELECT "AccessKeyId" FROM "EmailConfigurations" WHERE "Id" = @id""", ("id", configId));

            // A credential nobody believes is in use, still decryptable in the database, is worse
            // than no credential at all.
            run.Check("the secret is cleared when switching to an IAM role", afterRole is null, afterRole);
            run.Check("the access key id is cleared too", keyIdAfterRole is null, keyIdAfterRole);

            var incompleteRejected = false;
            try
            {
                await connections.SaveProviderAsync(configId, new SaveEmailProviderRequest
                {
                    Provider = "AmazonSes", Region = "ap-southeast-1",
                    AuthMode = "AccessKey", AccessKeyId = "AKIA", IsActive = true
                });
            }
            catch (ArgumentException) { incompleteRejected = true; }
            run.Check("access-key mode without a secret is rejected", incompleteRejected);

            run.Section("The rate-limiter bucket follows the configured rate");

            await connections.SaveProviderAsync(configId, new SaveEmailProviderRequest
            {
                Provider = "AmazonSes", Region = "ap-southeast-1", AuthMode = "IamRole",
                MaxSendRatePerSecond = 14, IsActive = true
            });

            var connectionId = created.ConnectionId!.Value;

            var refill = await harness.ScalarAsync(
                """SELECT "RefillPerSecond" FROM "EmailSendQuotas" WHERE "ConnectionId" = @id""",
                ("id", connectionId));
            run.Check("a quota row exists at the configured rate",
                refill is not null && Math.Abs(double.Parse(refill) - 14) < 0.001, refill);

            await connections.SaveProviderAsync(configId, new SaveEmailProviderRequest
            {
                Provider = "AmazonSes", Region = "ap-southeast-1", AuthMode = "IamRole",
                MaxSendRatePerSecond = 3, IsActive = true
            });

            var loweredRefill = await harness.ScalarAsync(
                """SELECT "RefillPerSecond" FROM "EmailSendQuotas" WHERE "ConnectionId" = @id""",
                ("id", connectionId));
            var loweredTokens = await harness.ScalarAsync(
                """SELECT "Tokens" FROM "EmailSendQuotas" WHERE "ConnectionId" = @id""",
                ("id", connectionId));

            run.Check("lowering the rate takes effect immediately",
                loweredRefill is not null && Math.Abs(double.Parse(loweredRefill) - 3) < 0.001, loweredRefill);
            run.Check("leftover tokens are clamped to the new capacity",
                loweredTokens is not null && double.Parse(loweredTokens) <= 3, loweredTokens);

            run.Section("A server that accepts but never speaks fails fast");

            // A listener that completes the TCP handshake and then stays silent. This is exactly
            // what a STARTTLS attempt against an implicit-TLS port looks like from the client:
            // connected, and waiting forever for a banner that will only ever be sent inside a
            // TLS session.
            using (var silent = new TcpListener(IPAddress.Loopback, 0))
            {
                silent.Start();
                var silentPort = ((IPEndPoint)silent.LocalEndpoint).Port;

                // Accept and hold, so the socket stays open rather than being refused.
                var held = new List<TcpClient>();
                var accepting = Task.Run(async () =>
                {
                    try
                    {
                        while (true) held.Add(await silent.AcceptTcpClientAsync());
                    }
                    catch (Exception) { /* listener stopped at the end of the block */ }
                });

                await connections.SaveProviderAsync(configId, new SaveEmailProviderRequest
                {
                    Provider = "Smtp", SmtpHost = "127.0.0.1", SmtpPort = silentPort,
                    SmtpSecurity = "StartTls", IsActive = true
                });

                var started = Stopwatch.StartNew();
                var silentResult = await connections.TestConnectionAsync(configId);
                started.Stop();

                run.Check("the test fails rather than hanging indefinitely", !silentResult.Success,
                    silentResult.Message);

                // The configured bound is 20s. Anything near MailKit's 120-second default means
                // the timeout is not being applied, which is the defect this pins down. The
                // allowance is generous so a slow CI box cannot fail it spuriously.
                run.Check("it gives up within the configured timeout, not MailKit's 120s default",
                    started.Elapsed < TimeSpan.FromSeconds(60),
                    $"took {started.Elapsed.TotalSeconds:F1}s");

                run.Check("the failure names the host and port so it is actionable",
                    silentResult.Message.Contains(silentPort.ToString()),
                    silentResult.Message);

                silent.Stop();
                await Task.WhenAny(accepting, Task.Delay(TimeSpan.FromSeconds(2)));
                foreach (var c in held) c.Dispose();
            }

            run.Section("An unreachable host reports rather than throws");

            await connections.SaveProviderAsync(configId, new SaveEmailProviderRequest
            {
                Provider = "Smtp", SmtpHost = "smtp.does-not-exist.invalid", SmtpPort = 587,
                SmtpSecurity = "StartTls", IsActive = true
            });

            var testResult = await connections.TestConnectionAsync(configId);
            run.Check("the test returns a failure instead of throwing", !testResult.Success);
            run.Check("the message names the host so it is actionable",
                testResult.Message.Contains("does-not-exist", StringComparison.OrdinalIgnoreCase),
                testResult.Message);

            var recordedOutcome = await harness.ScalarAsync(
                """SELECT "LastTestSucceeded" FROM "EmailConfigurations" WHERE "Id" = @id""", ("id", configId));
            run.Check("the outcome is recorded for the wizard to show",
                recordedOutcome?.ToLowerInvariant() == "false", recordedOutcome);

            var statusAfterFailedTest = (await connections.GetByIdAsync(configId)).Status;
            run.Check("status distinguishes a known-bad credential from an unconfigured one",
                statusAfterFailedTest == "Needs attention", statusAfterFailedTest);

            run.Section("MIME builder");

            var message = new EmailMessage
            {
                From = new EmailAddress("sender@example.test", "Sender Name"),
                ReplyTo = new EmailAddress("reply@example.test"),
                To = [new EmailAddress("to@example.test", "To Person")],
                Cc = [new EmailAddress("cc@example.test")],
                Bcc = [new EmailAddress("secret-bcc@example.test")],
                Subject = "Subject line",
                HtmlBody = "<h1>Hello</h1><p>First para</p><p>Second <strong>para</strong></p>",
                MessageId = mimeBuilder.NewMessageId("example.test"),
                Headers = new Dictionary<string, string>
                {
                    ["List-Unsubscribe"] = "<https://example.test/u?t=abc>",
                    ["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click",
                    ["X-Omni-Campaign-Id"] = "1"
                }
            };

            var mime = mimeBuilder.Build(message);
            using var buffer = new MemoryStream();
            await mime.WriteToAsync(buffer);
            var raw = System.Text.Encoding.UTF8.GetString(buffer.ToArray());

            run.Check("To is present", raw.Contains("to@example.test"));
            run.Check("Cc is present", raw.Contains("cc@example.test"));

            // The one that matters. MimeKit writes a Bcc header when serialising, and SES is
            // handed exactly this serialised form — so a Bcc header here would disclose every
            // blind recipient to everyone on the message.
            run.Check("Bcc is NOT disclosed in the serialised MIME",
                !raw.Contains("secret-bcc@example.test"),
                "a Bcc header here would leak blind recipients through SES raw send");

            run.Check("Reply-To is present", raw.Contains("reply@example.test"));
            run.Check("the unsubscribe header is applied", raw.Contains("List-Unsubscribe:"));
            run.Check("the one-click variant is applied", raw.Contains("List-Unsubscribe=One-Click"));
            run.Check("the correlation header is applied", raw.Contains("X-Omni-Campaign-Id"));
            run.Check("our own Message-ID is stamped",
                raw.Contains("Message-Id:", StringComparison.OrdinalIgnoreCase));

            // A message with no text part is one of the strongest spam signals there is, so one is
            // derived when the caller supplies none.
            run.Check("a multipart/alternative is produced", raw.Contains("multipart/alternative"));
            run.Check("a plain-text alternative is derived from the HTML",
                mime.TextBody is not null
                && mime.TextBody.Contains("Hello")
                && mime.TextBody.Contains("Second para"),
                mime.TextBody?.Replace("\n", "\\n"));
            run.Check("the text alternative carries no markup",
                mime.TextBody is not null && !mime.TextBody.Contains('<'));

            run.Check("the envelope still targets every recipient, Bcc included",
                message.AllRecipients.Count() == 3, $"{message.AllRecipients.Count()}");

            run.Section("Sender identities");

            var second = await connections.AddSenderAsync(configId, new SaveEmailSenderIdentityRequest
            {
                DisplayName = "Support",
                EmailAddress = $"support-{harness.Tag}@example.test",
                IsDefault = true
            });
            run.Check("a second sender can be added", second.Id > 0);

            var senders = await connections.GetSendersAsync(configId);
            run.Check("exactly one sender is the default after promotion",
                senders.Count(s => s.IsDefault) == 1, $"{senders.Count(s => s.IsDefault)}");
            run.Check("the newly promoted sender is that default",
                senders.First(s => s.IsDefault).Id == second.Id);

            var duplicateSenderRejected = false;
            try
            {
                await connections.AddSenderAsync(configId, new SaveEmailSenderIdentityRequest
                {
                    DisplayName = "Dup", EmailAddress = $"support-{harness.Tag}@example.test"
                });
            }
            catch (InvalidOperationException) { duplicateSenderRejected = true; }
            run.Check("a duplicate sender address is rejected", duplicateSenderRejected);

            run.Section("Disconnecting clears the credentials");

            await connections.SaveProviderAsync(configId, new SaveEmailProviderRequest
            {
                Provider = "Smtp", SmtpHost = "smtp.example.invalid", SmtpPort = 587,
                SmtpSecurity = "StartTls", SmtpUsername = "u", SmtpPassword = TestSecret, IsActive = true
            });

            await connections.DisconnectAsync(configId);

            var afterDisconnect = await harness.ScalarAsync(
                """SELECT "SmtpPasswordEncrypted" FROM "EmailConfigurations" WHERE "Id" = @id""",
                ("id", configId));
            var activeAfter = await harness.ScalarAsync(
                """SELECT "IsActive" FROM "EmailConfigurations" WHERE "Id" = @id""", ("id", configId));

            run.Check("credentials are cleared on disconnect", afterDisconnect is null, afterDisconnect);
            run.Check("the configuration is deactivated", activeAfter?.ToLowerInvariant() == "false", activeAfter);
            run.Check("status now reads Disconnected, not Setup pending",
                (await connections.GetByIdAsync(configId)).Status == "Disconnected");

            var resolveBlocked = false;
            try
            {
                await providerFactory.ResolveByConfigurationAsync(configId);
            }
            catch (InvalidOperationException) { resolveBlocked = true; }
            run.Check("an inactive configuration cannot be resolved for sending", resolveBlocked);
        }
        catch (Exception ex)
        {
            run.Error("connection phase threw", ex);
        }
    }
}
