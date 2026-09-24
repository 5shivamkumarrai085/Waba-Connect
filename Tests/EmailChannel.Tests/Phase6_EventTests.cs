using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Email;
using WhatsAppCampaignApi.Models.DTOs.Setup;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Email;

namespace EmailChannel.Tests;

/// <summary>
/// Phase 6 — SES delivery events, suppression automation and one-click unsubscribe.
///
/// <para>
/// The SNS validator is exercised against forged input, because the endpoint it guards is
/// anonymous and its notifications suppress customers' addresses. An attacker who could get a
/// bounce accepted could silently stop mail reaching a real recipient, so "a forged signature is
/// refused" is a functional requirement, not a hardening detail.
/// </para>
/// </summary>
public static class Phase6_EventTests
{
    public static async Task RunAsync(TestHarness harness, TestRun run)
    {
        run.BeginPhase("Phase 6 — delivery events, suppression and unsubscribe");

        var contactId = 0;
        var campaignId = 0;
        var recipientId = 0;
        var configId = 0;
        var templateId = 0;
        var providerMessageId = $"ses-{harness.Tag}-0001";
        var recipientEmail = $"events-{harness.Tag}@example.test";

        try
        {
            run.Section("SNS envelope validation");

            using (var scope = harness.CreateScope())
            {
                var validator = scope.ServiceProvider.GetRequiredService<ISnsMessageValidator>();

                // No signature at all.
                run.Check("an unsigned notification is refused",
                    !await validator.IsValidAsync(new SnsEnvelope
                    {
                        Type = "Notification",
                        MessageId = "m1",
                        TopicArn = "arn:aws:sns:us-east-1:123456789012:ses-events",
                        Message = "{}"
                    }));

                // Signed, but the certificate would be fetched from an attacker-controlled host —
                // where they hold the matching private key, so the signature would verify.
                run.Check("a notification whose signing certificate is not an Amazon host is refused",
                    !await validator.IsValidAsync(new SnsEnvelope
                    {
                        Type = "Notification",
                        MessageId = "m2",
                        TopicArn = "arn:aws:sns:us-east-1:123456789012:ses-events",
                        Message = "{}",
                        Signature = Convert.ToBase64String(new byte[256]),
                        SigningCertURL = "https://evil.example.com/cert.pem",
                        SignatureVersion = "1"
                    }));

                // The classic substring-check bypass: an Amazon host name in the query string of
                // a URL that points somewhere else entirely.
                run.Check("a certificate URL that only mentions an Amazon host is refused",
                    !await validator.IsValidAsync(new SnsEnvelope
                    {
                        Type = "Notification",
                        MessageId = "m3",
                        TopicArn = "arn:aws:sns:us-east-1:123456789012:ses-events",
                        Message = "{}",
                        Signature = Convert.ToBase64String(new byte[256]),
                        SigningCertURL = "https://evil.example.com/x.pem?host=sns.us-east-1.amazonaws.com",
                        SignatureVersion = "1"
                    }));

                // A real Amazon host, but a topic that is not ours. Signature verification alone
                // proves only that *some* AWS account sent it.
                run.Check("a notification from a topic outside the allowlist is refused",
                    !await validator.IsValidAsync(new SnsEnvelope
                    {
                        Type = "Notification",
                        MessageId = "m4",
                        TopicArn = "arn:aws:sns:us-east-1:999999999999:someone-elses-topic",
                        Message = "{}",
                        Signature = Convert.ToBase64String(new byte[256]),
                        SigningCertURL = "https://sns.us-east-1.amazonaws.com/cert.pem",
                        SignatureVersion = "1"
                    }));

                // The suite configures no allowlist, which must fail closed rather than open.
                run.Check("an empty allowlist refuses everything rather than accepting everything",
                    !await validator.IsValidAsync(new SnsEnvelope
                    {
                        Type = "Notification",
                        MessageId = "m5",
                        TopicArn = "arn:aws:sns:us-east-1:123456789012:ses-events",
                        Message = "{}",
                        Signature = Convert.ToBase64String(new byte[256]),
                        SigningCertURL = "https://sns.us-east-1.amazonaws.com/cert.pem",
                        SignatureVersion = "1"
                    }));

                run.Check("a subscription confirmation with no SubscribeURL is refused",
                    !await validator.ConfirmSubscriptionAsync(new SnsEnvelope
                    {
                        Type = "SubscriptionConfirmation",
                        TopicArn = "arn:aws:sns:us-east-1:123456789012:ses-events"
                    }));

                // Fetching an arbitrary URL supplied by an anonymous caller is a server-side
                // request forgery, so the host is checked before any request is made.
                run.Check("a SubscribeURL pointing somewhere other than SNS is never fetched",
                    !await validator.ConfirmSubscriptionAsync(new SnsEnvelope
                    {
                        Type = "SubscriptionConfirmation",
                        TopicArn = "arn:aws:sns:us-east-1:123456789012:ses-events",
                        SubscribeURL = "http://169.254.169.254/latest/meta-data/"
                    }));
            }

            run.Section("Fixtures for event processing");

            await harness.InScopeAsync(async services =>
            {
                var db = services.GetRequiredService<AppDbContext>();
                var connections = services.GetRequiredService<IEmailConnectionService>();
                var templates = services.GetRequiredService<IEmailTemplateService>();

                var contact = new Contact
                {
                    Name = $"{TestHarness.Prefix} Events Recipient {harness.Tag}",
                    Phone = $"+9199{Random.Shared.Next(10000000, 99999999)}",
                    Email = recipientEmail,
                    Type = "Lead",
                    Status = "New",
                    Source = "Import",
                    IsActive = true
                };
                db.Contacts.Add(contact);
                await db.SaveChangesAsync();
                contactId = contact.Id;

                var created = await connections.CreateAsync(new CreateEmailConnectionRequest
                {
                    Name = $"{TestHarness.Prefix}-events-{harness.Tag}",
                    DisplayName = "Events Test",
                    EmailAddress = $"sender-events-{harness.Tag}@example.test"
                });
                configId = created.Id;

                var template = await templates.CreateAsync(new SaveEmailTemplateRequest
                {
                    Name = $"{TestHarness.Prefix} events template {harness.Tag}",
                    Subject = "Events {{name}}",
                    BodyHtml = "<p>Events body</p>",
                    IsEnabled = true
                });
                templateId = template.Id;

                // Built directly rather than through the campaign service: this phase is about
                // what happens to an already-sent message, and going through the send pipeline
                // again would only re-test phase 5.
                var campaign = new Campaign
                {
                    Name = $"{TestHarness.Prefix} events campaign {harness.Tag}",
                    Channel = MessageChannel.Email,
                    EmailTemplateId = templateId,
                    ConnectionId = created.ConnectionId,
                    RelationType = "Lead",
                    ScheduleType = ScheduleType.Immediate,
                    Status = CampaignStatus.Sending,
                    TotalRecipients = 1
                };

                campaign.CampaignContacts.Add(new CampaignContact
                {
                    ContactId = contactId,
                    Status = MessageStatus.Sent,
                    ProviderMessageId = providerMessageId,
                    SentAt = DateTime.UtcNow
                });

                db.Campaigns.Add(campaign);
                await db.SaveChangesAsync();

                campaignId = campaign.Id;
                recipientId = campaign.CampaignContacts.First().Id;

                run.Check("a sent recipient exists to receive events", recipientId > 0);
            });

            run.Section("Delivery moves the recipient forward");

            await ProcessAsync(harness, run, "delivery-1", BuildEvent("Delivery", providerMessageId, recipientEmail),
                expectProcessed: true, label: "a delivery event is processed");

            var afterDelivery = await harness.ScalarAsync(
                """SELECT "Status" FROM "CampaignContacts" WHERE "Id" = @id""", ("id", recipientId));
            run.Check("the recipient is marked Delivered", afterDelivery == "Delivered", afterDelivery);

            var deliveredAt = await harness.ScalarAsync(
                """SELECT "DeliveredAt" FROM "CampaignContacts" WHERE "Id" = @id""", ("id", recipientId));
            run.Check("DeliveredAt is stamped", deliveredAt is not null);

            run.Section("Redelivered notifications are ignored");

            await ProcessAsync(harness, run, "delivery-1", BuildEvent("Delivery", providerMessageId, recipientEmail),
                expectProcessed: false, label: "the same SNS message id is not processed twice");

            var eventCount = await harness.CountAsync("""
                SELECT count(*) FROM "EmailDeliveryEvents" WHERE "ProviderMessageId" = @id
                """, ("id", providerMessageId));
            run.Check("only one event row was written", eventCount == 1, $"{eventCount}");

            run.Section("Opens and clicks are engagement, not delivery");

            await ProcessAsync(harness, run, "open-1", BuildEvent("Open", providerMessageId, recipientEmail),
                expectProcessed: true, label: "an open event is recorded");

            var afterOpen = await harness.ScalarAsync(
                """SELECT "Status" FROM "CampaignContacts" WHERE "Id" = @id""", ("id", recipientId));

            // The guard that matters: an open arriving after a delivery must not regress the
            // recipient's state, and must never be treated as a delivery status of its own.
            run.Check("an open does not change the delivery status", afterOpen == "Delivered", afterOpen);

            await ProcessAsync(harness, run, "click-1",
                BuildEvent("Click", providerMessageId, recipientEmail, link: "https://example.test/offer"),
                expectProcessed: true, label: "a click event is recorded");

            var clickedLink = await harness.ScalarAsync("""
                SELECT "LinkUrl" FROM "EmailDeliveryEvents"
                 WHERE "ProviderMessageId" = @id AND "EventType" = 'Click'
                """, ("id", providerMessageId));
            run.Check("the clicked link is recorded",
                clickedLink == "https://example.test/offer", clickedLink);

            var afterClick = await harness.ScalarAsync(
                """SELECT "Status" FROM "CampaignContacts" WHERE "Id" = @id""", ("id", recipientId));
            run.Check("a click does not change the delivery status either", afterClick == "Delivered", afterClick);

            run.Section("A transient bounce is a deferral, not a write-off");

            var transientEmail = $"transient-{harness.Tag}@example.test";
            await ProcessAsync(harness, run, "bounce-transient",
                BuildBounce(providerMessageId, transientEmail, "Transient", "MailboxFull", "452 Mailbox full"),
                expectProcessed: true, label: "a transient bounce is recorded");

            var afterTransient = await harness.ScalarAsync(
                """SELECT "Status" FROM "CampaignContacts" WHERE "Id" = @id""", ("id", recipientId));
            run.Check("a transient bounce does not mark the recipient Bounced",
                afterTransient == "Delivered", afterTransient);

            await harness.InScopeAsync(async services =>
            {
                var suppression = services.GetRequiredService<IEmailSuppressionService>();

                // Suppressing here would permanently lose a recipient because their mailbox was
                // briefly full.
                run.Check("a transient bounce does not suppress the address",
                    !await suppression.IsSuppressedAsync(transientEmail, null));
            });

            run.Section("A permanent bounce is terminal and suppresses");

            await ProcessAsync(harness, run, "bounce-permanent",
                BuildBounce(providerMessageId, recipientEmail, "Permanent", "NoEmail", "550 5.1.1 User unknown"),
                expectProcessed: true, label: "a permanent bounce is recorded");

            var afterPermanent = await harness.ScalarAsync(
                """SELECT "Status" FROM "CampaignContacts" WHERE "Id" = @id""", ("id", recipientId));
            run.Check("the recipient is marked Bounced", afterPermanent == "Bounced", afterPermanent);

            var bounceReason = await harness.ScalarAsync(
                """SELECT "ErrorMessage" FROM "CampaignContacts" WHERE "Id" = @id""", ("id", recipientId));
            run.Check("the remote server's own diagnostic is kept",
                bounceReason is not null && bounceReason.Contains("550"), bounceReason);

            // The stored id carries the run tag that ProcessAsync appends, so concurrent runs
            // cannot collide on the unique SnsMessageId index.
            var recordedBounceType = await harness.ScalarAsync("""
                SELECT "BounceType" FROM "EmailDeliveryEvents" WHERE "SnsMessageId" = @sns
                """, ("sns", $"bounce-permanent-{harness.Tag}"));
            run.Check("the bounce type is recorded", recordedBounceType == "Permanent", recordedBounceType);

            await harness.InScopeAsync(async services =>
            {
                var suppression = services.GetRequiredService<IEmailSuppressionService>();
                run.Check("a permanent bounce suppresses the address globally",
                    await suppression.IsSuppressedAsync(recipientEmail, null));
            });

            var suppressionReason = await harness.ScalarAsync("""
                SELECT "Reason" FROM "EmailSuppressions" WHERE "EmailAddressNormalized" = @e
                """, ("e", recipientEmail));
            run.Check("it is suppressed for the right reason", suppressionReason == "Bounce", suppressionReason);

            var failedCount = await harness.ScalarAsync(
                """SELECT "FailedCount" FROM "Campaigns" WHERE "Id" = @id""", ("id", campaignId));
            run.Check("the campaign's failure count includes the bounce", failedCount == "1", failedCount);

            run.Section("A complaint is terminal regardless of what came before");

            var complaintEmail = $"complaint-{harness.Tag}@example.test";
            await ProcessAsync(harness, run, "complaint-1",
                BuildComplaint(providerMessageId, complaintEmail, "abuse"),
                expectProcessed: true, label: "a complaint event is recorded");

            var afterComplaint = await harness.ScalarAsync(
                """SELECT "Status" FROM "CampaignContacts" WHERE "Id" = @id""", ("id", recipientId));
            run.Check("a complaint overrides even a bounced status",
                afterComplaint == "Complained", afterComplaint);

            await harness.InScopeAsync(async services =>
            {
                var suppression = services.GetRequiredService<IEmailSuppressionService>();
                run.Check("the complaining address is suppressed",
                    await suppression.IsSuppressedAsync(complaintEmail, null));
            });

            run.Section("Unattributable and malformed events");

            await ProcessAsync(harness, run, "orphan-1",
                BuildEvent("Delivery", $"unknown-{harness.Tag}", "nobody@example.test"),
                expectProcessed: true, label: "an event for an unknown message is still recorded");

            var orphanEvent = await harness.CountAsync("""
                SELECT count(*) FROM "EmailDeliveryEvents"
                 WHERE "SnsMessageId" = @sns AND "CampaignContactId" IS NULL
                """, ("sns", $"orphan-1-{harness.Tag}"));
            run.Check("it is recorded with no recipient attached rather than dropped",
                orphanEvent == 1, $"{orphanEvent}");

            await ProcessAsync(harness, run, "malformed-1", "{ not json",
                expectProcessed: false, label: "a malformed payload is discarded, not retried");

            await ProcessAsync(harness, run, "no-message-id", """{"eventType":"Delivery","mail":{}}""",
                expectProcessed: false, label: "an event with no message id is discarded");

            run.Section("Unsubscribe tokens");

            using (var scope = harness.CreateScope())
            {
                var tokens = scope.ServiceProvider.GetRequiredService<IUnsubscribeTokenService>();
                var unsubscribeEmail = $"unsub-{harness.Tag}@example.test";

                var url = tokens.BuildUnsubscribeUrl(unsubscribeEmail, campaignId, contactId);
                run.Check("the unsubscribe URL is absolute and public",
                    url.StartsWith("https://example.test/api/public/email/unsubscribe?t="), url);

                var token = url.Split("t=")[1];
                var payload = tokens.Validate(token);

                run.Check("a freshly issued token validates", payload is not null);
                run.Check("it carries the address it authorises",
                    payload?.Email == unsubscribeEmail, payload?.Email);
                run.Check("it carries the campaign for attribution",
                    payload?.CampaignId == campaignId, payload?.CampaignId?.ToString());

                // Signed, not stored. A tampered payload must fail even though it is
                // structurally valid — otherwise anyone could unsubscribe anyone.
                var parts = token.Split('.');
                var forgedPayload = Convert.ToBase64String(
                    System.Text.Encoding.UTF8.GetBytes(
                        $$"""{"Email":"victim@example.test","CampaignId":null,"ContactId":null,"Exp":{{DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds()}}}"""))
                    .TrimEnd('=').Replace('+', '-').Replace('/', '_');

                run.Check("a token with a swapped payload is rejected",
                    tokens.Validate($"{forgedPayload}.{parts[1]}") is null);

                run.Check("a token with a swapped signature is rejected",
                    tokens.Validate($"{parts[0]}.{forgedPayload}") is null);

                run.Check("an empty token is rejected", tokens.Validate("") is null);
                run.Check("a token with no signature part is rejected", tokens.Validate(parts[0]) is null);
                run.Check("arbitrary text is rejected", tokens.Validate("not-a-token-at-all") is null);
            }

            run.Section("Unsubscribing suppresses and is enforced on the next send");

            await harness.InScopeAsync(async services =>
            {
                var suppression = services.GetRequiredService<IEmailSuppressionService>();
                var unsubEmail = $"willunsub-{harness.Tag}@example.test";

                run.Check("the address starts unsuppressed",
                    !await suppression.IsSuppressedAsync(unsubEmail, null));

                await suppression.SuppressAsync(
                    unsubEmail, SuppressionReason.Unsubscribe, "UnsubscribeLink (one-click)");

                run.Check("it is suppressed afterwards",
                    await suppression.IsSuppressedAsync(unsubEmail, null));

                // The set-based filter is the one expansion actually uses, so it is what gets
                // tested rather than the single-address convenience method.
                var filtered = await suppression.FilterSuppressedAsync(
                    [unsubEmail, $"never-suppressed-{harness.Tag}@example.test"], null);

                run.Check("the bulk filter reports it as suppressed", filtered.Contains(unsubEmail));
                run.Check("the bulk filter leaves other addresses alone", filtered.Count == 1, $"{filtered.Count}");

                // Casing must not be a way past the list.
                run.Check("suppression is case-insensitive",
                    await suppression.IsSuppressedAsync(unsubEmail.ToUpperInvariant(), null));

                var removed = await suppression.UnsuppressAsync(unsubEmail, null, "integration-test");
                run.Check("an address can be removed from the list", removed);
                run.Check("and is then sendable again",
                    !await suppression.IsSuppressedAsync(unsubEmail, null));
            });

            run.Section("Suppression scope");

            await harness.InScopeAsync(async services =>
            {
                var suppression = services.GetRequiredService<IEmailSuppressionService>();
                var db = services.GetRequiredService<AppDbContext>();

                var connectionId = await db.EmailConfigurations
                    .Where(c => c.Id == configId)
                    .Select(c => c.ConnectionId)
                    .FirstAsync();

                var scopedEmail = $"scoped-{harness.Tag}@example.test";

                await suppression.SuppressAsync(
                    scopedEmail, SuppressionReason.Bounce, "test", connectionId: connectionId);

                run.Check("a connection-scoped suppression applies to that connection",
                    await suppression.IsSuppressedAsync(scopedEmail, connectionId));

                // A hard bounce against one sending identity does not always mean the address is
                // dead for another, which is why scope exists at all.
                run.Check("it does not apply to a different connection",
                    !await suppression.IsSuppressedAsync(scopedEmail, connectionId + 10_000));
            });
        }
        catch (Exception ex)
        {
            run.Error("event phase threw", ex);
        }
    }

    private static async Task ProcessAsync(
        TestHarness harness,
        TestRun run,
        string snsMessageId,
        string rawMessage,
        bool expectProcessed,
        string label)
    {
        await harness.InScopeAsync(async services =>
        {
            var processor = services.GetRequiredService<IEmailEventProcessor>();
            var processed = await processor.ProcessAsync($"{snsMessageId}-{harness.Tag}", rawMessage);
            run.Check(label, processed == expectProcessed, $"processed={processed}");
        });
    }

    // ── Payload builders, shaped as SES actually publishes them ──────────────────────────────

    private static string BuildEvent(string eventType, string messageId, string recipient, string? link = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["eventType"] = eventType,
            ["mail"] = new Dictionary<string, object?>
            {
                ["messageId"] = messageId,
                ["timestamp"] = DateTime.UtcNow,
                ["destination"] = new[] { recipient }
            }
        };

        payload[eventType.ToLowerInvariant()] = eventType switch
        {
            "Delivery" => new Dictionary<string, object?>
            {
                ["timestamp"] = DateTime.UtcNow,
                ["recipients"] = new[] { recipient },
                ["smtpResponse"] = "250 2.0.0 OK"
            },
            "Open" => new Dictionary<string, object?>
            {
                ["timestamp"] = DateTime.UtcNow,
                ["ipAddress"] = "203.0.113.10",
                ["userAgent"] = "Mozilla/5.0"
            },
            "Click" => new Dictionary<string, object?>
            {
                ["timestamp"] = DateTime.UtcNow,
                ["ipAddress"] = "203.0.113.10",
                ["userAgent"] = "Mozilla/5.0",
                ["link"] = link
            },
            _ => new Dictionary<string, object?>()
        };

        return JsonSerializer.Serialize(payload);
    }

    private static string BuildBounce(
        string messageId,
        string recipient,
        string bounceType,
        string subType,
        string diagnostic) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["eventType"] = "Bounce",
            ["mail"] = new Dictionary<string, object?>
            {
                ["messageId"] = messageId,
                ["timestamp"] = DateTime.UtcNow,
                ["destination"] = new[] { recipient }
            },
            ["bounce"] = new Dictionary<string, object?>
            {
                ["bounceType"] = bounceType,
                ["bounceSubType"] = subType,
                ["timestamp"] = DateTime.UtcNow,
                ["bouncedRecipients"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["emailAddress"] = recipient,
                        ["diagnosticCode"] = diagnostic,
                        ["status"] = "5.1.1"
                    }
                }
            }
        });

    private static string BuildComplaint(string messageId, string recipient, string feedbackType) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["eventType"] = "Complaint",
            ["mail"] = new Dictionary<string, object?>
            {
                ["messageId"] = messageId,
                ["timestamp"] = DateTime.UtcNow,
                ["destination"] = new[] { recipient }
            },
            ["complaint"] = new Dictionary<string, object?>
            {
                ["complaintFeedbackType"] = feedbackType,
                ["timestamp"] = DateTime.UtcNow,
                ["complainedRecipients"] = new[]
                {
                    new Dictionary<string, object?> { ["emailAddress"] = recipient }
                }
            }
        });
}
