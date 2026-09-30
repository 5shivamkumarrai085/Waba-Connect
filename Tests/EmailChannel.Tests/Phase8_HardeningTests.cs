using Microsoft.Extensions.DependencyInjection;
using MimeKit;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Email;
using WhatsAppCampaignApi.Models.DTOs.Setup;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services;
using WhatsAppCampaignApi.Services.Email;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Services.Queue;

namespace EmailChannel.Tests;

/// <summary>
/// Phase 8 — the behaviour the reliability pass depends on.
///
/// <para>
/// Each section pins one fix that would otherwise regress silently: a rate-limited job must not
/// burn its retries, a reply must reach Chat and count once against its campaign, engagement must
/// count people rather than events, the IMAP host must be derivable from SMTP, and stored secrets
/// must be encrypted with the current cipher while older values still read.
/// </para>
/// </summary>
public static class Phase8_HardeningTests
{
    public static async Task RunAsync(TestHarness harness, TestRun run)
    {
        run.BeginPhase("Phase 8 — reliability: queue defer, replies, unique engagement, secrets");

        await QueueDeferAsync(harness, run);
        ImapHostDerivation(run);
        FieldCipher(run);
        await RepliesAndEngagementAsync(harness, run);
    }

    private static async Task QueueDeferAsync(TestHarness harness, TestRun run)
    {
        run.Section("A deferred job keeps its attempts");

        using var scope = harness.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IJobQueue>();
        var queueName = $"zz-{harness.Tag}-defer";

        try
        {
            await queue.EnqueueAsync([new QueueMessage { QueueName = queueName, Payload = "{}", MaxAttempts = 2 }]);

            var request = new QueueClaimRequest { QueueName = queueName, WorkerId = "defer-test", BatchSize = 1 };
            var first = (await queue.ClaimAsync(request)).FirstOrDefault();
            run.Check("the job is claimed", first is not null);
            if (first is null) return;

            // Deferring three times would dead-letter a job with two attempts if a defer counted
            // as a failure — which is exactly what used to happen to rate-limited sends.
            QueueLease? lease = first;
            for (var i = 0; i < 3 && lease is not null; i++)
            {
                run.Check($"defer {i + 1} is accepted", await queue.DeferAsync(lease, TimeSpan.Zero, "rate limited"));
                lease = (await queue.ClaimAsync(request)).FirstOrDefault();
            }

            run.Check("the job is still claimable after repeated defers", lease is not null);
            run.Check("its attempt counter did not grow", lease?.Attempt == 1, $"{lease?.Attempt}");

            var status = await harness.ScalarAsync(
                """SELECT "Status" FROM "JobQueue" WHERE "QueueName" = @q""", ("q", queueName));
            run.Check("it was never dead-lettered", status == "Leased", status);

            run.Section("A job whose last attempt lost its lease is dead-lettered");

            // Simulate a worker that died holding the final attempt.
            await harness.ExecAsync("""
                UPDATE "JobQueue" SET "Attempt" = "MaxAttempts", "LeaseExpiresAt" = now() - interval '1 minute'
                 WHERE "QueueName" = @q
                """, ("q", queueName));

            var reclaimed = await queue.ClaimAsync(request);
            run.Check("the claim does not hand it out again", reclaimed.Count == 0, $"{reclaimed.Count}");

            await queue.DeadLetterExhaustedLeasesAsync();
            var afterSweep = await harness.ScalarAsync(
                """SELECT "Status" FROM "JobQueue" WHERE "QueueName" = @q""", ("q", queueName));
            run.Check("the sweep dead-letters it", afterSweep == "DeadLettered", afterSweep);
        }
        finally
        {
            await harness.ExecAsync("""DELETE FROM "JobQueue" WHERE "QueueName" = @q""", ("q", queueName));
        }
    }

    private static void ImapHostDerivation(TestRun run)
    {
        run.Section("The reply mailbox is derived from the SMTP host");

        // The mailbox that stopped replies reaching Chat had SMTP configured and no IMAP host.
        run.Check("mail.example.com serves both", ImapEndpointResolver.DeriveHostFromSmtp("mail.example.com") == "mail.example.com");
        run.Check("smtp.example.com becomes imap.example.com", ImapEndpointResolver.DeriveHostFromSmtp("smtp.example.com") == "imap.example.com");
        run.Check("Gmail maps to its IMAP host", ImapEndpointResolver.DeriveHostFromSmtp("smtp.gmail.com") == "imap.gmail.com");
        run.Check("Office 365 maps to its IMAP host", ImapEndpointResolver.DeriveHostFromSmtp("smtp.office365.com") == "outlook.office365.com");
        run.Check("no SMTP host, no guess", ImapEndpointResolver.DeriveHostFromSmtp("  ") is null);
    }

    private static void FieldCipher(TestRun run)
    {
        run.Section("Stored secrets use the versioned cipher");

        if (!SecretCipher.IsInitialized)
        {
            run.Skip("field cipher", "Encryption:Key is not configured for this run");
            return;
        }

        const string secret = "smtp-password-Ω-123";
        var first = SecretCipher.Encrypt(secret);
        var second = SecretCipher.Encrypt(secret);

        run.Check("ciphertext carries the version prefix", SecretCipher.IsEncrypted(first), first[..Math.Min(8, first.Length)]);
        run.Check("it round-trips", SecretCipher.Decrypt(first) == secret);
        run.Check("each encryption uses a fresh nonce", first != second);
        run.Check("plaintext is passed through on read", SecretCipher.DecryptOrPassThrough("legacy-plain") == "legacy-plain");

        var tampered = first[..^4] + (first[^4] == 'A' ? "B" : "A") + first[^3..];
        var tamperRejected = false;
        try { SecretCipher.Decrypt(tampered); }
        catch { tamperRejected = true; }
        run.Check("tampered ciphertext is rejected", tamperRejected);
    }

    private static async Task RepliesAndEngagementAsync(TestHarness harness, TestRun run)
    {
        run.Section("Fixtures: a sent campaign message with a known Message-ID");

        var recipientEmail = $"reply-{harness.Tag}@example.test";
        var outboundMessageId = $"<campaign-{harness.Tag}@example.test>";
        int campaignId = 0, recipientId = 0, connectionId = 0, contactId = 0;

        await harness.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var connections = services.GetRequiredService<IEmailConnectionService>();
            var templates = services.GetRequiredService<IEmailTemplateService>();

            var contact = new Contact
            {
                Name = $"{TestHarness.Prefix} Reply Recipient {harness.Tag}",
                Phone = $"+9198{Random.Shared.Next(10000000, 99999999)}",
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
                Name = $"{TestHarness.Prefix}-replies-{harness.Tag}",
                DisplayName = "Replies Test",
                EmailAddress = $"sender-replies-{harness.Tag}@example.test"
            });
            connectionId = created.ConnectionId ?? 0;

            var template = await templates.CreateAsync(new SaveEmailTemplateRequest
            {
                Name = $"{TestHarness.Prefix} replies template {harness.Tag}",
                Subject = "Hello",
                BodyHtml = "<p>Hello</p>",
                IsEnabled = true
            });

            var campaign = new Campaign
            {
                Name = $"{TestHarness.Prefix} replies campaign {harness.Tag}",
                Channel = MessageChannel.Email,
                EmailTemplateId = template.Id,
                ConnectionId = connectionId,
                RelationType = "Lead",
                ScheduleType = ScheduleType.Immediate,
                Status = CampaignStatus.Sending,
                TotalRecipients = 1
            };
            campaign.CampaignContacts.Add(new CampaignContact
            {
                ContactId = contactId,
                Status = MessageStatus.Sent,
                SentAt = DateTime.UtcNow
            });
            db.Campaigns.Add(campaign);
            await db.SaveChangesAsync();
            campaignId = campaign.Id;
            recipientId = campaign.CampaignContacts.First().Id;

            // The outbound copy in Chat, as the dispatcher records it.
            var conversation = new ChatConversation
            {
                Channel = MessageChannel.Email,
                ContactId = contactId,
                ConnectionId = connectionId,
                LastMessageAt = DateTime.UtcNow
            };
            db.ChatConversations.Add(conversation);
            await db.SaveChangesAsync();

            db.ChatMessages.Add(new ChatMessage
            {
                ConversationId = conversation.Id,
                ContactId = contactId,
                ConnectionId = connectionId,
                CampaignId = campaignId,
                CampaignContactId = recipientId,
                Channel = MessageChannel.Email,
                Direction = ChatMessageDirection.Outgoing,
                Status = ChatMessageStatus.Sent,
                Text = "Hello",
                SentAt = DateTime.UtcNow,
                EmailDetail = new EmailMessageDetail
                {
                    Subject = "Hello",
                    FromAddress = $"sender-replies-{harness.Tag}@example.test",
                    ToAddresses = recipientEmail,
                    MessageIdHeader = outboundMessageId
                }
            });
            await db.SaveChangesAsync();
        });

        run.Check("the campaign fixture exists", campaignId > 0 && recipientId > 0);

        try
        {
            run.Section("A reply is threaded into Chat and linked to its campaign");

            MimeMessage Reply(string id)
            {
                var mime = new MimeMessage();
                mime.From.Add(new MailboxAddress("Reply Recipient", recipientEmail));
                mime.To.Add(new MailboxAddress("Sender", $"sender-replies-{harness.Tag}@example.test"));
                mime.Subject = "Re: Hello";
                mime.MessageId = id;
                mime.InReplyTo = outboundMessageId;
                mime.References.Add(outboundMessageId);
                mime.Body = new TextPart("plain") { Text = "Thanks, I am interested." };
                return mime;
            }

            var replyId = $"reply-{harness.Tag}@mail.example.test";
            InboundThreadResult? threaded = null;
            await harness.InScopeAsync(async services =>
            {
                var threader = services.GetRequiredService<IInboundEmailThreader>();
                threaded = await threader.ThreadInboundMessageAsync(Reply(replyId), connectionId);
            });

            run.Check("the reply is stored", threaded is not null);
            run.Check("it is attributed to the campaign", threaded?.CampaignId == campaignId, $"{threaded?.CampaignId}");
            run.Check("and to the recipient who replied", threaded?.CampaignContactId == recipientId, $"{threaded?.CampaignContactId}");

            var unread = await harness.ScalarAsync("""
                SELECT "UnreadCount" FROM "ChatConversations"
                 WHERE "ContactId" = @c AND "ConnectionId" = @conn AND "Channel" = 'Email'
                """, ("c", contactId), ("conn", connectionId));
            run.Check("it lands in the existing conversation as unread", unread == "1", unread);

            InboundThreadResult? again = null;
            await harness.InScopeAsync(async services =>
            {
                var threader = services.GetRequiredService<IInboundEmailThreader>();
                again = await threader.ThreadInboundMessageAsync(Reply(replyId), connectionId);
            });
            run.Check("the same Message-ID is not stored twice", again is null);

            run.Section("Replies, opens and unsubscribes count people, not events");

            async Task RaiseAsync(EmailEventKind kind, string key) =>
                await harness.InScopeAsync(async services =>
                {
                    var processor = services.GetRequiredService<ICampaignEmailEventProcessor>();
                    await processor.ProcessAsync(kind, $"{key}-{harness.Tag}", "test", campaignId, recipientId,
                        recipientAddress: recipientEmail);
                });

            await RaiseAsync(EmailEventKind.Replied, "replied-1");
            await RaiseAsync(EmailEventKind.Replied, "replied-1");   // redelivery
            await RaiseAsync(EmailEventKind.Opened, "opened-1");
            await RaiseAsync(EmailEventKind.Opened, "opened-2");     // a second, distinct open
            await RaiseAsync(EmailEventKind.Unsubscribed, "unsub-1");
            await RaiseAsync(EmailEventKind.Unsubscribed, "unsub-2");

            async Task<string?> CounterAsync(string column) => await harness.ScalarAsync(
                $"""SELECT "{column}" FROM "Campaigns" WHERE "Id" = @id""", ("id", campaignId));

            run.Check("a reply counts once", await CounterAsync("RepliedCount") == "1", await CounterAsync("RepliedCount"));
            run.Check("two opens by one person count once", await CounterAsync("OpenedCount") == "1", await CounterAsync("OpenedCount"));
            run.Check("an unsubscribe counts once", await CounterAsync("UnsubscribedCount") == "1", await CounterAsync("UnsubscribedCount"));

            var repliedAt = await harness.ScalarAsync(
                """SELECT "RepliedAt" FROM "CampaignContacts" WHERE "Id" = @id""", ("id", recipientId));
            run.Check("the recipient's RepliedAt is stamped", repliedAt is not null);

            run.Section("Reconciliation agrees with the live counters");

            await harness.InScopeAsync(async services =>
            {
                var db = services.GetRequiredService<AppDbContext>();
                await CampaignFinalizer.ReconcileEmailCountersAsync(db, campaignId, CancellationToken.None);
            });

            run.Check("replies are unchanged by a reconcile", await CounterAsync("RepliedCount") == "1", await CounterAsync("RepliedCount"));
            run.Check("opens are unchanged by a reconcile", await CounterAsync("OpenedCount") == "1", await CounterAsync("OpenedCount"));
            run.Check("unsubscribes are unchanged by a reconcile", await CounterAsync("UnsubscribedCount") == "1", await CounterAsync("UnsubscribedCount"));
            run.Check("sent is recomputed from the recipient rows", await CounterAsync("SentCount") == "1", await CounterAsync("SentCount"));
        }
        finally
        {
            // Events are keyed to the campaign; everything else is removed by the prefix cleanup.
            await harness.ExecAsync("""DELETE FROM "EmailEvents" WHERE "CampaignId" = @id""", ("id", campaignId));
        }
    }
}
