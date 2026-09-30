using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Campaigns;
using WhatsAppCampaignApi.Models.DTOs.Email;
using WhatsAppCampaignApi.Models.DTOs.Setup;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Email;
using WhatsAppCampaignApi.Services.Interfaces;

namespace EmailChannel.Tests;

/// <summary>
/// Phase 5 — the whole send pipeline, end to end.
///
/// <para>
/// Campaign creation through the execution gate, into the queue, through expansion with its
/// suppression and eligibility filtering, past the rate limiter, out through a real SMTP
/// conversation, and back into the recorded state. The real background workers are started, so
/// this exercises the actual concurrency and the actual recording path rather than a rehearsal
/// of them.
/// </para>
/// </summary>
public static class Phase5_PipelineTests
{
    public static async Task RunAsync(TestHarness harness, TestRun run)
    {
        run.BeginPhase("Phase 5 — campaign send pipeline (end to end)");

        await using var smtp = new FakeSmtpServer();
        Console.WriteLine($"    note  fake SMTP listening on 127.0.0.1:{smtp.Port}");

        var contactIds = new List<int>();
        var campaignId = 0;
        var configId = 0;
        var templateId = 0;
        var senderId = 0;

        // A phrase that appears only in the rendered body, so a match cannot come from a header.
        var bodyMarker = $"marker-{harness.Tag}-in-body";

        try
        {
            run.Section("Fixtures");

            await harness.InScopeAsync(async services =>
            {
                var db = services.GetRequiredService<AppDbContext>();
                var connections = services.GetRequiredService<IEmailConnectionService>();
                var templates = services.GetRequiredService<IEmailTemplateService>();
                var suppression = services.GetRequiredService<IEmailSuppressionService>();

                // Four contacts, one for each path expansion has to handle.
                foreach (var (label, email, isActive) in new[]
                {
                    ("Valid Recipient", $"valid-{harness.Tag}@example.test", true),
                    ("No Email Recipient", (string?)null, true),
                    ("Suppressed Recipient", $"suppressed-{harness.Tag}@example.test", true),
                    ("Inactive Recipient", $"inactive-{harness.Tag}@example.test", false)
                })
                {
                    var contact = new Contact
                    {
                        Name = $"{TestHarness.Prefix} {label} {harness.Tag}",
                        Phone = $"+9199{Random.Shared.Next(10000000, 99999999)}",
                        Email = email,
                        Type = "Lead",
                        Status = "New",
                        Source = "Import",
                        IsActive = isActive
                    };

                    db.Contacts.Add(contact);
                    await db.SaveChangesAsync();
                    contactIds.Add(contact.Id);
                }

                run.Check("four test contacts created", contactIds.Count == 4);

                await suppression.SuppressAsync(
                    $"suppressed-{harness.Tag}@example.test",
                    SuppressionReason.Unsubscribe,
                    "integration-test");

                run.Check("one address is pre-suppressed",
                    await suppression.IsSuppressedAsync($"suppressed-{harness.Tag}@example.test", null));

                var created = await connections.CreateAsync(new CreateEmailConnectionRequest
                {
                    Name = $"{TestHarness.Prefix}-pipeline-{harness.Tag}",
                    DisplayName = "Pipeline Test",
                    EmailAddress = $"sender-{harness.Tag}@example.test",
                    ReplyToEmail = $"reply-{harness.Tag}@example.test"
                });
                configId = created.Id;

                await connections.SaveProviderAsync(configId, new SaveEmailProviderRequest
                {
                    Provider = "Smtp",
                    SmtpHost = "127.0.0.1",
                    SmtpPort = smtp.Port,
                    SmtpSecurity = "None",
                    MaxSendRatePerSecond = 50,
                    IsActive = true
                });

                senderId = (await connections.GetSendersAsync(configId)).Single().Id;

                var template = await templates.CreateAsync(new SaveEmailTemplateRequest
                {
                    Name = $"{TestHarness.Prefix} pipeline template {harness.Tag}",
                    Subject = "Hello {{name}} from {{company_name}}",
                    BodyHtml =
                        $"<p>Hi {{{{name}}}},</p><p>{bodyMarker}</p>" +
                        "<p>Your email is {{email}}.</p><p>Regards,<br>{{company_name}}</p>",
                    Language = "en",
                    IsEnabled = true
                });
                templateId = template.Id;

                run.Check("the template body survived sanitization",
                    template.BodyHtml.Contains(bodyMarker), $"length {template.BodyHtml.Length}");
            });

            run.Section("Campaign creation routes to the queue");

            await harness.InScopeAsync(async services =>
            {
                var campaigns = services.GetRequiredService<ICampaignService>();

                var campaign = await campaigns.CreateAsync(new CreateCampaignRequest
                {
                    Name = $"{TestHarness.Prefix} campaign {harness.Tag}",
                    Channel = "Email",
                    EmailTemplateId = templateId,
                    SenderIdentityId = senderId,
                    RelationType = "Lead",
                    ScheduleType = "Immediate",
                    ContactIds = contactIds,
                    Variables =
                    [
                        new CampaignVariableRequest
                        {
                            VariableName = "company_name",
                            VariableValue = "OmniConnect"
                        }
                    ]
                });

                campaignId = campaign.Id;

                run.Check("the campaign is on the Email channel", campaign.Channel == "Email", campaign.Channel);
                run.Check("it reports the email template's name",
                    campaign.TemplateName.Contains("pipeline template"), campaign.TemplateName);

                // Only three, and that is correct: campaign creation has always filtered to
                // active, non-deleted contacts, and the inactive one is excluded there rather
                // than being attached and failed later. Unchanged WhatsApp-era behaviour.
                run.Check("the inactive contact is excluded at creation, not attached and failed",
                    campaign.TotalRecipients == 3, $"{campaign.TotalRecipients}");

                var whatsappTemplate = await harness.ScalarAsync(
                    """SELECT "TemplateId" FROM "Campaigns" WHERE "Id" = @id""", ("id", campaignId));
                run.Check("no WhatsApp template is attached to an email campaign",
                    whatsappTemplate is null, whatsappTemplate);

                var queued = await harness.CountAsync("""
                    SELECT count(*) FROM "JobQueue"
                     WHERE "QueueName" = @q AND "IdempotencyKey" = @key
                    """, ("q", WhatsAppCampaignApi.Services.Queue.QueueNames.CampaignExpansion), ("key", $"expand:campaign:{campaignId}"));
                run.Check("an expansion job was enqueued", queued == 1, $"{queued}");
            });

            run.Section("Workers drain the queue");

            var workerCts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var expansion = ActivatorUtilities.CreateInstance<CampaignExpansionWorker>(harness.RootServices);
            var dispatch = ActivatorUtilities.CreateInstance<EmailDispatchWorker>(harness.RootServices);

            await expansion.StartAsync(workerCts.Token);
            await dispatch.StartAsync(workerCts.Token);

            var drained = await harness.WaitUntilAsync(
                async () => await harness.CountAsync("""
                    SELECT count(*) FROM "CampaignContacts" WHERE "CampaignId" = @id AND "Status" = 'Pending'
                    """, ("id", campaignId)) == 0,
                TimeSpan.FromSeconds(75));

            await expansion.StopAsync(CancellationToken.None);
            await dispatch.StopAsync(CancellationToken.None);
            workerCts.Dispose();

            run.Check("every recipient reached a terminal state", drained,
                "still pending after 75s — the workers did not finish");

            run.Section("Per-recipient outcomes");

            async Task<string?> StatusOfAsync(string email) => await harness.ScalarAsync("""
                SELECT cc."Status" FROM "CampaignContacts" cc
                  JOIN "Contacts" c ON c."Id" = cc."ContactId"
                 WHERE cc."CampaignId" = @id AND c."Email" = @email
                """, ("id", campaignId), ("email", email));

            var validStatus = await StatusOfAsync($"valid-{harness.Tag}@example.test");
            run.Check("the eligible recipient was sent", validStatus == "Sent", validStatus);

            // Its own status, distinct from Failed. An unsubscribed recipient is a correct
            // outcome, and reporting it as a failure makes a healthy campaign look broken.
            var suppressedStatus = await StatusOfAsync($"suppressed-{harness.Tag}@example.test");
            run.Check("the suppressed recipient is marked Suppressed, not Failed",
                suppressedStatus == "Suppressed", suppressedStatus);

            var noEmailStatus = await harness.ScalarAsync("""
                SELECT cc."Status" FROM "CampaignContacts" cc
                  JOIN "Contacts" c ON c."Id" = cc."ContactId"
                 WHERE cc."CampaignId" = @id AND c."Email" IS NULL
                """, ("id", campaignId));
            run.Check("the contact with no address is marked Failed", noEmailStatus == "Failed", noEmailStatus);

            var noEmailReason = await harness.ScalarAsync("""
                SELECT cc."ErrorMessage" FROM "CampaignContacts" cc
                  JOIN "Contacts" c ON c."Id" = cc."ContactId"
                 WHERE cc."CampaignId" = @id AND c."Email" IS NULL
                """, ("id", campaignId));
            run.Check("the reason is on the recipient row, not only in the logs",
                noEmailReason is not null && noEmailReason.Contains("no email"), noEmailReason);

            var inactiveAttached = await harness.CountAsync("""
                SELECT count(*) FROM "CampaignContacts" cc
                  JOIN "Contacts" c ON c."Id" = cc."ContactId"
                 WHERE cc."CampaignId" = @id AND c."IsActive" = false
                """, ("id", campaignId));
            run.Check("the inactive contact was never attached", inactiveAttached == 0, $"{inactiveAttached}");

            run.Section("What actually went on the wire");

            run.Check("exactly one message was transmitted", smtp.MessageCount == 1, $"{smtp.MessageCount}");

            var raw = smtp.Messages.FirstOrDefault() ?? string.Empty;

            run.Check("the subject was rendered with merge fields",
                raw.Contains("Hello") && raw.Contains("OmniConnect"),
                raw.Split('\n').FirstOrDefault(l => l.StartsWith("Subject", StringComparison.OrdinalIgnoreCase)));

            // Checked against a marker that appears only in the body. Asserting on the recipient
            // address would pass on the To header alone even with an empty body — which is
            // precisely how the empty-body defect first hid from this test.
            run.Check("the body was rendered and is not empty", raw.Contains(bodyMarker),
                $"body marker missing; message length {raw.Length}");

            run.Check("the recipient's own field was substituted into the body",
                raw.Contains($"Your email is valid-{harness.Tag}@example.test")
                || raw.Contains("Your email is =")   // quoted-printable soft line break
                , "the {{email}} placeholder did not resolve in the body");

            run.Check("no placeholder was left unsubstituted",
                !raw.Contains("{{name}}") && !raw.Contains("{{company_name}}") && !raw.Contains("{{email}}"));

            run.Check("one-click unsubscribe headers are present",
                raw.Contains("List-Unsubscribe:") && raw.Contains("List-Unsubscribe=One-Click"));
            run.Check("correlation headers are present",
                raw.Contains("X-Omni-Campaign-Id") && raw.Contains("X-Omni-Recipient-Id"));
            run.Check("a plain-text alternative was included", raw.Contains("multipart/alternative"));

            run.Check("the suppressed address was never transmitted",
                !raw.Contains($"suppressed-{harness.Tag}@example.test"));

            run.Section("Recorded state");

            var providerMessageId = await harness.ScalarAsync("""
                SELECT "ProviderMessageId" FROM "CampaignContacts"
                 WHERE "CampaignId" = @id AND "Status" = 'Sent'
                """, ("id", campaignId));
            run.Check("the provider message id is stored on the channel-neutral column",
                !string.IsNullOrEmpty(providerMessageId));

            var whatsappId = await harness.ScalarAsync("""
                SELECT "WhatsAppMessageId" FROM "CampaignContacts"
                 WHERE "CampaignId" = @id AND "Status" = 'Sent'
                """, ("id", campaignId));
            run.Check("the WhatsApp id column was left untouched", whatsappId is null, whatsappId);

            var chatMessages = await harness.CountAsync("""
                SELECT count(*) FROM "ChatMessages" WHERE "CampaignId" = @id AND "Channel" = 'Email'
                """, ("id", campaignId));
            run.Check("a chat message was written for the unified inbox", chatMessages == 1, $"{chatMessages}");

            var emailDetail = await harness.CountAsync("""
                SELECT count(*) FROM "EmailMessageDetails" d
                  JOIN "ChatMessages" m ON m."Id" = d."ChatMessageId"
                 WHERE m."CampaignId" = @id
                """, ("id", campaignId));
            run.Check("the email envelope detail was written", emailDetail == 1, $"{emailDetail}");

            var messageIdHeader = await harness.ScalarAsync("""
                SELECT d."MessageIdHeader" FROM "EmailMessageDetails" d
                  JOIN "ChatMessages" m ON m."Id" = d."ChatMessageId"
                 WHERE m."CampaignId" = @id
                """, ("id", campaignId));
            run.Check("our own Message-ID was stored, for reply threading",
                messageIdHeader is not null && messageIdHeader.Contains('@'), messageIdHeader);

            var storedSubject = await harness.ScalarAsync("""
                SELECT d."Subject" FROM "EmailMessageDetails" d
                  JOIN "ChatMessages" m ON m."Id" = d."ChatMessageId"
                 WHERE m."CampaignId" = @id
                """, ("id", campaignId));
            run.Check("the rendered subject was stored", !string.IsNullOrWhiteSpace(storedSubject), storedSubject);

            var conversationChannel = await harness.ScalarAsync("""
                SELECT c."Channel" FROM "ChatConversations" c
                  JOIN "ChatMessages" m ON m."ConversationId" = c."Id"
                 WHERE m."CampaignId" = @id LIMIT 1
                """, ("id", campaignId));
            run.Check("the thread was created on the Email channel",
                conversationChannel == "Email", conversationChannel);

            var activityLogs = await harness.CountAsync("""
                SELECT count(*) FROM "MessageActivityLogs" WHERE "Name" LIKE @name
                """, ("name", $"%{harness.Tag}%"));
            run.Check("an activity log row was written, in the same table WhatsApp uses",
                activityLogs == 1, $"{activityLogs}");

            // An email request body is the whole rendered message. Copying it into a log table
            // would put every recipient's personalised content there in full.
            var loggedPayload = await harness.ScalarAsync("""
                SELECT "RequestPayload" FROM "MessageActivityLogs" WHERE "Name" LIKE @name
                """, ("name", $"%{harness.Tag}%"));
            run.Check("the rendered message was not copied into the activity log", loggedPayload is null);

            run.Section("Campaign aggregate");

            var status = await harness.ScalarAsync(
                """SELECT "Status" FROM "Campaigns" WHERE "Id" = @id""", ("id", campaignId));
            run.Check("the campaign reports a partial outcome",
                status is "PartiallyFailed" or "Sent" or "Sending", status);

            // One failure: the contact with no address. The suppressed recipient is deliberately
            // not counted as a failure.
            var failedCount = await harness.ScalarAsync(
                """SELECT "FailedCount" FROM "Campaigns" WHERE "Id" = @id""", ("id", campaignId));
            run.Check("the failed count excludes the suppressed recipient", failedCount == "1", failedCount);

            var totalRecipients = await harness.ScalarAsync(
                """SELECT "TotalRecipients" FROM "Campaigns" WHERE "Id" = @id""", ("id", campaignId));
            run.Check("the recipient total matches the attached rows", totalRecipients == "3", totalRecipients);

            run.Section("Re-submitting must not send twice");

            await harness.InScopeAsync(async services =>
            {
                var dispatcher = services.GetRequiredService<IEmailCampaignDispatcher>();
                await dispatcher.SubmitAsync(campaignId, null);
            });

            var expansionJobs = await harness.CountAsync("""
                SELECT count(*) FROM "JobQueue" WHERE "IdempotencyKey" = @key
                """, ("key", $"expand:campaign:{campaignId}"));
            run.Check("re-submission did not create a second expansion job",
                expansionJobs == 1, $"{expansionJobs}");

            var sendJobs = await harness.CountAsync("""
                SELECT count(*) FROM "JobQueue" WHERE "QueueName" = @q AND "PartitionKey" = @p
                """, ("q", WhatsAppCampaignApi.Services.Queue.QueueNames.EmailSend), ("p", campaignId.ToString()));
            run.Check("exactly one send job exists, for the one eligible recipient",
                sendJobs == 1, $"{sendJobs}");

            // Re-running the workers over an already-sent campaign is the real duplicate-send
            // risk, so it is exercised rather than reasoned about.
            var rerunCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var rerunExpansion = ActivatorUtilities.CreateInstance<CampaignExpansionWorker>(harness.RootServices);
            var rerunDispatch = ActivatorUtilities.CreateInstance<EmailDispatchWorker>(harness.RootServices);

            await rerunExpansion.StartAsync(rerunCts.Token);
            await rerunDispatch.StartAsync(rerunCts.Token);
            await Task.Delay(TimeSpan.FromSeconds(6));
            await rerunExpansion.StopAsync(CancellationToken.None);
            await rerunDispatch.StopAsync(CancellationToken.None);
            rerunCts.Dispose();

            run.Check("still exactly one message transmitted after a full re-run",
                smtp.MessageCount == 1, $"{smtp.MessageCount}");

            run.Section("Rate limiting is enforced across callers");

            await harness.InScopeAsync(async services =>
            {
                var limiter = services.GetRequiredService<IEmailRateLimiter>();
                var connectionId = int.Parse((await harness.ScalarAsync(
                    """SELECT "ConnectionId" FROM "EmailConfigurations" WHERE "Id" = @id""",
                    ("id", configId)))!);

                // Drain the bucket, then confirm the next reservation is refused rather than
                // quietly allowed. An in-process limiter would pass this while still permitting
                // N times the rate across N instances — which is why the bucket lives in the
                // database.
                await harness.ExecAsync("""
                    UPDATE "EmailSendQuotas"
                       SET "Tokens" = 2, "Capacity" = 2, "RefillPerSecond" = 0.001, "LastRefillAt" = now()
                     WHERE "ConnectionId" = @id
                    """, ("id", connectionId));

                var firstGrant = await limiter.TryReserveAsync(connectionId, 2);
                run.Check("a reservation within capacity is granted in full", firstGrant == 2, $"{firstGrant}");

                var secondGrant = await limiter.TryReserveAsync(connectionId, 2);
                run.Check("a reservation beyond capacity is refused", secondGrant == 0, $"{secondGrant}");

                await limiter.ReleaseAsync(connectionId, 1);
                var afterRelease = await limiter.TryReserveAsync(connectionId, 1);
                run.Check("releasing an unused reservation returns the capacity",
                    afterRelease == 1, $"{afterRelease}");
            });
        }
        catch (Exception ex)
        {
            run.Error("pipeline phase threw", ex);
        }
    }
}
