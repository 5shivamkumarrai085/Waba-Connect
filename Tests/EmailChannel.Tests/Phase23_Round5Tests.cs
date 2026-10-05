using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Chat;
using WhatsAppCampaignApi.Models.DTOs.Contacts;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Services;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Services.Queue;
using WhatsAppCampaignApi.Services.Catalogs;
using WhatsAppCampaignApi.Services.Email;

namespace EmailChannel.Tests;

/// <summary>
/// Phase 23 — round 5: the public address tracking depends on, opens and clicks end to end,
/// bounces from plain-text reports and SMTP rejections, the A/B "no signal" rule, and audit
/// coverage.
/// </summary>
public static class Phase23_Round5Tests
{
    public static async Task RunAsync(TestHarness harness, TestRun run)
    {
        run.BeginPhase("Phase 23 — round 5: tracking, bounces, A/B no signal, audit");

        run.Section("Public address");
        run.Check("an empty address is reported as empty", PublicEndpointProbe.Classify("") == PublicEndpointCatalog.Empty);
        run.Check("localhost is not reachable by recipients", PublicEndpointProbe.Classify("http://localhost:5155") == PublicEndpointCatalog.Localhost);
        run.Check("a private network address is not reachable", PublicEndpointProbe.Classify("https://192.168.1.4") == PublicEndpointCatalog.PrivateNetwork);
        run.Check("plain http is refused (mail clients and Gmail one-click need https)", PublicEndpointProbe.Classify("http://example.com") == PublicEndpointCatalog.NotHttps);
        run.Check("a public https address passes the static checks", PublicEndpointProbe.Classify("https://example.com") is null);

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Email:Tracking:BaseUrl"] = "https://unreachable.invalid"
        }).Build();
        var probe = new PublicEndpointProbe(config, new HttpClient(), new MemoryCache(new MemoryCacheOptions()), NullLogger<PublicEndpointProbe>.Instance);
        var started = DateTime.UtcNow;
        var status = await probe.CheckAsync();
        run.Check("an address that does not answer is reported unreachable", !status.Reachable && status.Reason == PublicEndpointCatalog.Unreachable, status.Reason);
        run.Check("and the probe gives up quickly", DateTime.UtcNow - started < TimeSpan.FromSeconds(PublicEndpointCatalog.ProbeTimeoutSeconds + 1));
        run.Check("the reason is explained in words", !string.IsNullOrWhiteSpace(PublicEndpointCatalog.Describe(status.Reason)));

        run.Section("Pre-flight says when tracking cannot work");
        const string linked = "<p>Hi</p><a href=\"https://rma.my\">Site</a> <a href='https://rma.my/?a=1&amp;b=2'>Offer</a> <a href=\"{{unsubscribe_url}}\">Unsubscribe</a>";
        run.Check("links count the way the dispatcher rewrites them (opt-out links excluded)", EmailDispatchWorker.CountTrackableLinks(linked) == 2, EmailDispatchWorker.CountTrackableLinks(linked).ToString());
        run.Check("a body without links has none", EmailDispatchWorker.CountTrackableLinks("<p>Hi {{first_name}}</p>") == 0);
        await harness.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var template = new EmailTemplate { Key = $"{TestHarness.Prefix}-links-{harness.Tag}", Name = $"{TestHarness.Prefix} links {harness.Tag}", Subject = "Links", BodyHtml = "<p>No links here</p>", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            db.EmailTemplates.Add(template);
            await db.SaveChangesAsync();
            try
            {
                var items = await services.GetRequiredService<IDeliverabilityService>().PrecheckAsync(new PrecheckRequest { Channel = "Email", EmailTemplateId = template.Id });
                var tracking = items.FirstOrDefault(i => i.Key == "tracking");
                var links = items.FirstOrDefault(i => i.Key == "click-tracking");
                run.Check("a non-public tracking address is a warning in pre-flight", tracking is { Level: "warn" }, tracking?.Detail);
                run.Check("a message without links is called out", links is not null && links.Detail.Contains("no links"), links?.Detail);
            }
            finally
            {
                await db.EmailTemplates.Where(t => t.Id == template.Id).ExecuteDeleteAsync();
            }
        });

        await OpensAndClicksAsync(harness, run);
        await BouncesAsync(harness, run);
        await AuditAsync(harness, run);
        await TemplateDeleteAsync(harness, run);
        await ThreadingAsync(harness, run);
        await ActorAsync(harness, run);
        await InboxCountsAsync(harness, run);
    }

    /// <summary>One email campaign with one sent recipient, built directly so each check controls its own events.</summary>
    private static async Task<(int CampaignId, int RecipientId)> SentCampaignAsync(TestHarness harness, string label, DateTime updatedAt)
    {
        var ids = (0, 0);
        await harness.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var contact = new Contact
            {
                Name = $"{TestHarness.Prefix} r5 {label} {harness.Tag}",
                Phone = $"+9193{Random.Shared.Next(10000000, 99999999)}",
                Email = $"r5-{label}-{harness.Tag}@example.test",
                Type = "Lead", Status = "New", Source = "Import", IsActive = true,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            };
            db.Contacts.Add(contact);
            var campaign = new Campaign
            {
                Name = $"{TestHarness.Prefix} r5 {label} {harness.Tag}",
                Channel = MessageChannel.Email,
                RelationType = "Lead",
                ScheduleType = ScheduleType.Immediate,
                Status = CampaignStatus.Sent,
                TotalRecipients = 1
            };
            campaign.CampaignContacts.Add(new CampaignContact { Contact = contact, Status = MessageStatus.Sent, SentAt = DateTime.UtcNow.AddHours(-1) });
            db.Campaigns.Add(campaign);
            await db.SaveChangesAsync();
            // SaveChanges stamps UpdatedAt; put back the age the check needs.
            await db.Campaigns.IgnoreQueryFilters().Where(c => c.Id == campaign.Id).ExecuteUpdateAsync(u => u.SetProperty(c => c.UpdatedAt, updatedAt));
            ids = (campaign.Id, campaign.CampaignContacts.First().Id);
        });
        return ids;
    }

    private static async Task OpensAndClicksAsync(TestHarness harness, TestRun run)
    {
        run.Section("Opens and clicks count people, and survive a crash");
        var (campaignId, recipientId) = await SentCampaignAsync(harness, "opens", DateTime.UtcNow);
        var (staleId, staleRecipientId) = await SentCampaignAsync(harness, "stale", DateTime.UtcNow.AddDays(-3));
        try
        {
            async Task RaiseAsync(EmailEventKind kind, string key, string? url = null) =>
                await harness.InScopeAsync(async services =>
                    await services.GetRequiredService<ICampaignEmailEventProcessor>().ProcessAsync(
                        kind, $"{key}-{harness.Tag}", "test", campaignId, recipientId, originalUrl: url));

            async Task<string?> CounterAsync(int id, string column) =>
                await harness.ScalarAsync($"""SELECT "{column}" FROM "Campaigns" WHERE "Id" = @id""", ("id", id));

            await RaiseAsync(EmailEventKind.Opened, $"pixel:{recipientId}:unique");
            await RaiseAsync(EmailEventKind.Opened, $"pixel:{recipientId}:unique");     // Gmail's proxy fetching again
            await RaiseAsync(EmailEventKind.Clicked, $"click:{recipientId}:1", "https://example.test/offer");
            await RaiseAsync(EmailEventKind.Opened, $"pixel:{recipientId}:unique");     // the open a click implies
            run.Check("repeated opens by one person count once", await CounterAsync(campaignId, "OpenedCount") == "1", await CounterAsync(campaignId, "OpenedCount"));
            run.Check("the click counts", await CounterAsync(campaignId, "ClickedCount") == "1", await CounterAsync(campaignId, "ClickedCount"));

            await harness.InScopeAsync(async services =>
            {
                var report = await services.GetRequiredService<ICampaignService>().GetLinkReportAsync(campaignId);
                var row = report.FirstOrDefault();
                run.Check("the Links report lists the clicked address with one clicker",
                    row is not null && row.Url == "https://example.test/offer" && row.UniqueClickers == 1, row?.Url);
            });

            // A crash between recording the event and updating the recipient: the event row exists,
            // the recipient and counter never moved, and Gmail will not fetch the pixel again.
            await harness.ExecAsync("""
                INSERT INTO "EmailEvents" ("IdempotencyKey","EventKind","Source","OccurredAt","CampaignId","CampaignContactId","CreatedAt")
                VALUES (@k, 'Opened', 'test', now(), @c, @r, now())
                """, ("k", $"crash-open-{harness.Tag}"), ("c", staleId), ("r", staleRecipientId));

            IReadOnlyList<int> swept = [];
            await harness.InScopeAsync(async services =>
                swept = await JobQueueMaintenanceWorker.CampaignsToReconcileAsync(services.GetRequiredService<AppDbContext>(), DateTime.UtcNow.AddHours(-6), CancellationToken.None));
            run.Check("a finished campaign with a fresh event is picked up by the sweep", swept.Contains(staleId), string.Join(",", swept));

            await harness.InScopeAsync(async services =>
                await CampaignFinalizer.ReconcileEmailCountersAsync(services.GetRequiredService<AppDbContext>(), staleId, CancellationToken.None));
            var openedAt = await harness.ScalarAsync("""SELECT "OpenedAt" FROM "CampaignContacts" WHERE "Id" = @id""", ("id", staleRecipientId));
            run.Check("the sweep repairs the recipient from the recorded event", openedAt is not null);
            run.Check("and the counter follows", await CounterAsync(staleId, "OpenedCount") == "1", await CounterAsync(staleId, "OpenedCount"));
        }
        finally
        {
            await harness.ExecAsync("""DELETE FROM "EmailEvents" WHERE "CampaignId" IN (@a, @b)""", ("a", campaignId), ("b", staleId));
        }
    }

    private static MimeKit.MimeMessage Mime(string raw) =>
        MimeKit.MimeMessage.Load(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(raw.Replace("\r\n", "\n").Replace("\n", "\r\n"))));

    /// <summary>The plain-text report Exim (cPanel and most shared hosts) sends: no multipart/report, no DSN fields.</summary>
    private static string EximReport(string to, string recipient, string originalId, string diagnostic = "550 5.1.1 <{0}>: Recipient address rejected: User unknown") => $"""
        From: Mail Delivery System <Mailer-Daemon@mail.example.test>
        To: {to}
        Subject: Mail delivery failed: returning message to sender
        Message-Id: <E1report-{Guid.NewGuid():N}@mail.example.test>
        Date: Wed, 01 Oct 2026 06:00:00 +0000
        Auto-Submitted: auto-replied
        Content-Type: text/plain; charset=utf-8

        This message was created automatically by mail delivery software.

        A message that you sent could not be delivered to one or more of its
        recipients. This is a permanent error. The following address(es) failed:

          {recipient}
            host mx.example.test [192.0.2.1]
            SMTP error from remote mail server after RCPT TO:<{recipient}>:
            {string.Format(diagnostic, recipient)}

        ------ This is a copy of the message, including all the headers. ------

        Return-path: <{to}>
        Message-ID: {originalId}
        From: Sender <{to}>
        To: {recipient}
        Subject: Hello

        Hi there
        """;

    private static async Task BouncesAsync(TestHarness harness, TestRun run)
    {
        run.Section("Bounces from plain-text reports");
        var tag = harness.Tag;
        var exim = ImapBounceDetector.TryParse(Mime(EximReport("crm1@example.test", $"gone-{tag}@example.test", $"<orig-{tag}@example.test>")));
        run.Check("an Exim/cPanel plain-text report is recognised as a bounce", exim is not null);
        run.Check("the failed recipient is read from the report, not the sender", exim?.FinalRecipient == $"gone-{tag}@example.test", exim?.FinalRecipient);
        run.Check("a 5.x.x status is a permanent bounce", exim is { BounceType: EmailBounceType.Permanent, StatusCode: "5.1.1" }, $"{exim?.BounceType} {exim?.StatusCode}");
        run.Check("the original Message-ID is found in the returned copy", exim?.OriginalMessageId == $"<orig-{tag}@example.test>", exim?.OriginalMessageId);

        var noUser = ImapBounceDetector.TryParse(Mime(EximReport("crm1@example.test", $"gone-{tag}@example.test", $"<orig-{tag}@example.test>", "No Such User Here")));
        run.Check("a report with only words (\"No Such User Here\", \"permanent error\") is still permanent", noUser is { BounceType: EmailBounceType.Permanent }, noUser?.BounceType.ToString());

        var delayed = ImapBounceDetector.TryParse(Mime(EximReport("crm1@example.test", $"slow-{tag}@example.test", $"<orig2-{tag}@example.test>", "451 4.4.1 Temporary failure, will retry")
            .Replace("Mail delivery failed: returning message to sender", "Warning: message delayed")
            .Replace("This is a permanent error.", "Delivery is being retried.")));
        run.Check("a delay warning (4.x.x) is transient, never a bounce", delayed is { BounceType: EmailBounceType.Transient }, delayed?.BounceType.ToString());

        var human = ImapBounceDetector.TryParse(Mime($"""
            From: Ashok <ashok-{tag}@example.test>
            To: crm1@example.test
            Subject: Re: your offer
            Message-Id: <human-{tag}@example.test>
            Content-Type: text/plain; charset=utf-8

            Thanks, the 550 rupees price is fine and the delivery failed to arrive last time.
            """));
        run.Check("an ordinary reply that mentions numbers is not a bounce", human is null);

        run.Section("A bounce without a Message-ID finds the right recipient");
        var connectionId = 0;
        await harness.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var connection = new Connection { Name = $"{TestHarness.Prefix} r5 bounce conn {tag}", IsActive = true };
            var other = new Connection { Name = $"{TestHarness.Prefix} r5 bounce other {tag}", IsActive = true };
            db.Connections.AddRange(connection, other);
            await db.SaveChangesAsync();
            connectionId = connection.Id;
            Campaign Sent(string label, int conn, DateTime sentAt)
            {
                var contact = db.Contacts.Local.FirstOrDefault(c => c.Email == $"gone-{tag}@example.test")
                    ?? new Contact { Name = $"{TestHarness.Prefix} r5 gone {tag}", Phone = $"+9194{Random.Shared.Next(10000000, 99999999)}", Email = $"gone-{tag}@example.test", Type = "Lead", Status = "New", Source = "Import", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
                var campaign = new Campaign { Name = $"{TestHarness.Prefix} r5 bounce {label} {tag}", Channel = MessageChannel.Email, ConnectionId = conn, RelationType = "Lead", ScheduleType = ScheduleType.Immediate, Status = CampaignStatus.Sent, TotalRecipients = 1 };
                campaign.CampaignContacts.Add(new CampaignContact { Contact = contact, Status = MessageStatus.Sent, SentAt = sentAt });
                db.Campaigns.Add(campaign);
                return campaign;
            }
            var old = Sent("old", connection.Id, DateTime.UtcNow.AddDays(-(BounceCatalog.MatchWindowDays + 2)));
            var recent = Sent("recent", connection.Id, DateTime.UtcNow.AddHours(-2));
            var elsewhere = Sent("elsewhere", other.Id, DateTime.UtcNow.AddMinutes(-5));
            await db.SaveChangesAsync();

            var bounce = exim! with { OriginalMessageId = null };
            var (campaignId, recipientId) = await ImapPollingWorker.ResolveBounceRecipientAsync(db, bounce, connection.Id, DateTime.UtcNow, CancellationToken.None);
            run.Check("it matches the latest send to that address from the same connection", campaignId == recent.Id && recipientId == recent.CampaignContacts.First().Id, $"{campaignId}/{recipientId} (recent {recent.Id}, old {old.Id}, elsewhere {elsewhere.Id})");

            var (none, _) = await ImapPollingWorker.ResolveBounceRecipientAsync(db, bounce with { FinalRecipient = $"stranger-{tag}@example.test" }, connection.Id, DateTime.UtcNow, CancellationToken.None);
            run.Check("an address this connection never mailed matches nothing", none is null, none?.ToString());
        });

        run.Section("A recipient the server refuses at send time is a bounce");
        await using var smtp = new FakeSmtpServer { RejectRecipientsContaining = "nobody" };
        await harness.InScopeAsync(async services =>
        {
            var provider = ActivatorUtilities.CreateInstance<SmtpEmailProvider>(services);
            var message = new EmailMessage
            {
                From = new EmailAddress("crm1@example.test"),
                To = [new EmailAddress($"nobody-{tag}@example.test")],
                Subject = "Bounce check",
                HtmlBody = "<p>Hi</p>",
                MessageId = $"<bounce-{tag}@example.test>"
            };
            var context = new EmailProviderContext { EmailConfigurationId = -1, Provider = EmailProviderType.Smtp, SmtpHost = "127.0.0.1", SmtpPort = smtp.Port, SmtpSecurity = SmtpSecurityMode.None };
            var result = await provider.SendAsync(message, context);
            run.Check("a 550 to RCPT TO is reported as a permanent bounce", !result.Success && result.IsPermanentBounce, result.ErrorMessage);

            var db = services.GetRequiredService<AppDbContext>();
            var contact = new Contact { Name = $"{TestHarness.Prefix} r5 nobody {tag}", Phone = $"+9195{Random.Shared.Next(10000000, 99999999)}", Email = $"nobody-{tag}@example.test", Type = "Lead", Status = "New", Source = "Import", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            var campaign = new Campaign { Name = $"{TestHarness.Prefix} r5 refused {tag}", Channel = MessageChannel.Email, ConnectionId = connectionId, RelationType = "Lead", ScheduleType = ScheduleType.Immediate, Status = CampaignStatus.Sending, TotalRecipients = 1 };
            var recipient = new CampaignContact { Contact = contact, Status = MessageStatus.Pending };
            campaign.CampaignContacts.Add(recipient);
            db.Campaigns.Add(campaign);
            await db.SaveChangesAsync();

            await services.GetRequiredService<IEmailSendRecorder>().RecordFailedAsync(campaign, recipient, message, result, "Smtp");
            var row = await db.Campaigns.AsNoTracking().Where(c => c.Id == campaign.Id).Select(c => new { c.BouncedCount, c.FailedCount }).FirstAsync();
            var status = await db.CampaignContacts.AsNoTracking().Where(cc => cc.Id == recipient.Id).Select(cc => cc.Status).FirstAsync();
            run.Check("the campaign counts it as Bounced, not Failed", row.BouncedCount == 1 && row.FailedCount == 0, $"bounced {row.BouncedCount}, failed {row.FailedCount}");
            run.Check("the recipient is marked Bounced", status == MessageStatus.Bounced, status.ToString());
            run.Check("the address is suppressed so it is never mailed again",
                await services.GetRequiredService<IEmailSuppressionService>().IsSuppressedAsync($"nobody-{tag}@example.test", null));
            await db.EmailEvents.Where(e => e.CampaignId == campaign.Id).ExecuteDeleteAsync();
        });
    }

    /// <summary>Answers every call with a completed task (or the default value): for collaborators a check does not exercise.</summary>
    private class NoOpService : System.Reflection.DispatchProxy
    {
        public static T Create<T>() where T : class => Create<T, NoOpService>();

        protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args)
        {
            var type = method?.ReturnType;
            if (type is null || type == typeof(void)) return null;
            if (type == typeof(Task)) return Task.CompletedTask;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var inner = type.GetGenericArguments()[0];
                var value = inner.IsValueType ? Activator.CreateInstance(inner) : null;
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(inner).Invoke(null, [value]);
            }
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }
    }

    private static async Task AuditAsync(TestHarness harness, TestRun run)
    {
        run.Section("Every change is audited");
        var tag = harness.Tag;
        var phone = $"+9196{Random.Shared.Next(10000000, 99999999)}";
        int contactId = 0;
        await harness.InScopeAsync(async services =>
        {
            var contacts = ActivatorUtilities.CreateInstance<ContactService>(services, NoOpService.Create<IChatService>());
            CreateContactRequest Request() => new()
            {
                Name = $"{TestHarness.Prefix} r5 audit {tag}", Phone = phone, Email = $"audit-{tag}@example.test",
                Type = "Lead", Status = "New", Source = "Import"
            };
            var first = await contacts.CreateAsync(Request());
            contactId = first.Id;
            await contacts.DeleteAsync(first.Id);
            var before = harness.Audit.Entries.Count(e => e.Event == "Contact.Created" && e.EntityId == first.Id.ToString());
            var again = await contacts.CreateAsync(Request());
            var after = harness.Audit.Entries.Count(e => e.Event == "Contact.Created" && e.EntityId == again.Id.ToString());
            run.Check("re-adding a deleted contact (same phone) is audited as a create", again.Id == first.Id && after == before + 1, $"before {before}, after {after}");

            var note = await contacts.AddNoteAsync(again.Id, "Called, will reply tomorrow");
            // Filed under the contact, so the note shows in that customer's history.
            run.Check("adding a note is audited on the contact's history", harness.Audit.Has("ContactNote.Created", again.Id.ToString()));
            await contacts.DeleteNoteAsync(again.Id, note.Id);
            run.Check("deleting a note is audited on the contact's history", harness.Audit.Has("ContactNote.Deleted", again.Id.ToString()));
            var missing = await Record(() => contacts.AddNoteAsync(int.MaxValue, "x"));
            run.Check("a note for a contact that does not exist is refused, not a database error", missing is KeyNotFoundException, missing?.GetType().Name);
        });

        var (finishing, _) = await SentCampaignAsync(harness, "finish", DateTime.UtcNow);
        await harness.ExecAsync("""UPDATE "Campaigns" SET "Status" = 'Sending' WHERE "Id" = @id""", ("id", finishing));
        string? first = null, second = null;
        await harness.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            first = await CampaignFinalizer.TryFinalizeAsync(db, finishing, CancellationToken.None);
            second = await CampaignFinalizer.TryFinalizeAsync(db, finishing, CancellationToken.None);
        });
        var completed = await harness.CountAsync("""
            SELECT count(*) FROM "AuditLogs" WHERE "EntityType" = 'Campaign' AND "EntityId" = @id AND "Event" = 'Campaign.Completed' AND "UserName" = 'System'
            """, ("id", finishing.ToString()));
        run.Check("a campaign that finishes is audited once, as the system", first == "Sent" && second is null && completed == 1, $"{first}/{second}, rows {completed}");

        run.Section("Recent Activity is the audit log");
        await harness.ExecAsync("""
            INSERT INTO "AuditLogs" ("Event","Category","Module","Action","Status","Description","EntityType","EntityId","UserId","UserName","CreatedAt")
            VALUES ('Campaign.Created','Data','Campaign','Created','Success', @d1, 'Campaign','1', -901, 'Agent One', now() + interval '1 minute'),
                   ('Contact.Updated','Data','Contact','Updated','Success', @d2, 'Contact','2', -902, 'Agent Two', now() + interval '2 minutes'),
                   ('Auth.LoginSucceeded','Security','Auth','LoginSucceeded','Success', @d3, NULL, NULL, -901, 'Agent One', now() + interval '3 minutes')
            """, ("d1", $"{TestHarness.Prefix} campaign {tag}"), ("d2", $"{TestHarness.Prefix} contact {tag}"), ("d3", $"{TestHarness.Prefix} login {tag}"));
        try
        {
            await harness.InScopeAsync(async services =>
            {
                var db = services.GetRequiredService<AppDbContext>();
                var all = await DashboardCacheService.RecentActivityAsync(db, onlyUserId: null, take: 8, CancellationToken.None);
                var mine = await DashboardCacheService.RecentActivityAsync(db, onlyUserId: -901, take: 8, CancellationToken.None);
                run.Check("the newest business events come first", all.Count >= 2 && all[0].Title.Contains($"contact {tag}") && all[1].Title.Contains($"campaign {tag}"),
                    string.Join(" | ", all.Take(3).Select(a => a.Title)));
                run.Check("sign-ins are not business activity", all.All(a => !a.Title.Contains($"login {tag}")));
                run.Check("each item names who did it", all[0].Subtitle == "Agent Two", all[0].Subtitle);
                run.Check("a scoped user sees only their own actions", mine.Count >= 1 && mine.All(a => a.Subtitle == "Agent One"), string.Join(" | ", mine.Select(a => a.Subtitle)));
            });
        }
        finally
        {
            await harness.ExecAsync("""DELETE FROM "AuditLogs" WHERE "Description" LIKE @p""", ("p", $"{TestHarness.Prefix}%{tag}"));
        }
    }

    private static async Task<Exception?> Record(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception ex) { return ex; }
    }

    private static async Task TemplateDeleteAsync(TestHarness harness, TestRun run)
    {
        run.Section("Deleting a template that is still in use");
        var tag = harness.Tag;
        await harness.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var used = new Template { Name = $"zz_emailtest_r5_inuse_{tag}", Status = TemplateStatus.Approved, BodyText = "Hi", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            var main = new Template { Name = $"zz_emailtest_r5_main_{tag}", Status = TemplateStatus.Approved, BodyText = "Hi", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            db.Templates.AddRange(used, main);
            await db.SaveChangesAsync();
            harness.TrackTemplate(used.Id, main.Id);

            // A deleted campaign still keeps its history; its A/B variant used this template.
            var campaign = new Campaign
            {
                Name = $"{TestHarness.Prefix} r5 variant {tag}", Channel = MessageChannel.WhatsApp, TemplateId = main.Id,
                RelationType = "Lead", ScheduleType = ScheduleType.Immediate, Status = CampaignStatus.Sent, IsDeleted = true
            };
            campaign.Variants.Add(new CampaignVariant { Label = "B", TemplateId = used.Id });
            db.Campaigns.Add(campaign);
            await db.SaveChangesAsync();

            var templates = ActivatorUtilities.CreateInstance<TemplateService>(services);
            var refused = await Record(() => templates.DeleteAsync(used.Id));
            run.Check("it is refused with a clear reason, not a database error", refused is InvalidOperationException, refused?.GetType().Name + ": " + refused?.Message);
            var mainRefused = await Record(() => templates.DeleteAsync(main.Id));
            run.Check("a template a deleted campaign was sent with is kept too (its history needs it)", mainRefused is InvalidOperationException, mainRefused?.GetType().Name);
            var stillThere = await db.Campaigns.IgnoreQueryFilters().AnyAsync(c => c.Id == campaign.Id);
            run.Check("and the deleted campaign's history is untouched", stillThere);
        });
    }

    private static async Task ThreadingAsync(TestHarness harness, TestRun run)
    {
        run.Section("New Email starts a new thread; replies stay in theirs");
        var tag = harness.Tag;
        await harness.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var contact = new Contact { Name = $"{TestHarness.Prefix} r5 thread {tag}", Phone = $"+9197{Random.Shared.Next(10000000, 99999999)}", Email = $"thread-{tag}@example.test", Type = "Lead", Status = "New", Source = "Import", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            var conversation = new ChatConversation { Contact = contact, Channel = MessageChannel.Email };
            var earlier = new ChatMessage { Conversation = conversation, Contact = contact, Channel = MessageChannel.Email, CreatedAt = DateTime.UtcNow.AddMinutes(-5),
                EmailDetail = new EmailMessageDetail { MessageIdHeader = $"<earlier-{tag}@example.test>", Subject = "Hello" } };
            db.ChatMessages.Add(earlier);
            await db.SaveChangesAsync();

            var reply = await EmailReplyService.ResolveThreadingHeadersAsync(db, conversation.Id, earlier.Id, CancellationToken.None);
            run.Check("a reply carries the message it answers as In-Reply-To", reply.InReplyTo == $"<earlier-{tag}@example.test>", reply.InReplyTo);
            var fresh = await EmailReplyService.ResolveThreadingHeadersAsync(db, conversation.Id, null, CancellationToken.None);
            run.Check("a New Email has no In-Reply-To or References, so mail apps start a new thread", fresh.InReplyTo is null && fresh.References is null, fresh.InReplyTo);
        });
    }

    /// <summary>A request with no signed-in user (a failed sign-in) — not the system.</summary>
    private sealed class AnonymousCaller : WhatsAppCampaignApi.Services.Interfaces.ICurrentUserService
    {
        public int? UserId => null;
        public string? Email => null;
        public string? UserName => null;
        public bool IsAdministrator => false;
        public bool IsAuthenticated => false;
        public string? IpAddress => "203.0.113.7";
        public string? UserAgent => "test-browser";
        public Task<bool> HasPermissionAsync(string permissionKey) => Task.FromResult(false);
        public Task<bool> HasAnyPermissionAsync(IEnumerable<string> permissionKeys) => Task.FromResult(false);
        public Task<List<string>> GetPermissionsAsync() => Task.FromResult(new List<string>());
    }

    /// <summary>A background worker: no request, no user.</summary>
    private sealed class BackgroundCaller : WhatsAppCampaignApi.Services.Interfaces.ICurrentUserService
    {
        public int? UserId => null;
        public string? Email => null;
        public string? UserName => null;
        public bool IsAdministrator => false;
        public bool IsAuthenticated => false;
        public string? IpAddress => null;
        public string? UserAgent => null;
        public Task<bool> HasPermissionAsync(string permissionKey) => Task.FromResult(false);
        public Task<bool> HasAnyPermissionAsync(IEnumerable<string> permissionKeys) => Task.FromResult(false);
        public Task<List<string>> GetPermissionsAsync() => Task.FromResult(new List<string>());
    }

    private static async Task ActorAsync(TestHarness harness, TestRun run)
    {
        run.Section("Who did it: a person, a stranger, or the system");
        var tag = harness.Tag;
        try
        {
            await harness.InScopeAsync(async services =>
            {
                var db = services.GetRequiredService<AppDbContext>();
                async Task<string?> LogAs(WhatsAppCampaignApi.Services.Interfaces.ICurrentUserService caller, string label)
                {
                    var audit = ActivatorUtilities.CreateInstance<AuditService>(services, caller, new AuditChangeBuffer());
                    var description = $"{TestHarness.Prefix} actor {label} {tag}";
                    await audit.LogAsync("Auth.LoginFailed", "Auth", description);
                    return await db.AuditLogs.AsNoTracking().Where(a => a.Description == description).Select(a => a.UserName).FirstOrDefaultAsync();
                }
                var stranger = await LogAs(new AnonymousCaller(), "stranger");
                var worker = await LogAs(new BackgroundCaller(), "worker");
                run.Check("a failed sign-in from a browser is not attributed to the system", stranger is null, stranger);
                run.Check("work done by a background worker is attributed to the system", worker == RecentActivityCatalog.SystemActor, worker);
            });
        }
        finally
        {
            await harness.ExecAsync("""DELETE FROM "AuditLogs" WHERE "Description" LIKE @p""", ("p", $"{TestHarness.Prefix} actor %{tag}"));
        }
    }

    private static async Task InboxCountsAsync(TestHarness harness, TestRun run)
    {
        run.Section("Inbox quick views, sort, and a record's history");
        var tag = harness.Tag;
        await harness.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            // Any real user: the owner column is a foreign key to AppUsers.
            var me = await db.AppUsers.OrderBy(u => u.Id).Select(u => u.Id).FirstAsync();
            var t0 = DateTime.UtcNow.AddDays(-3);
            ChatConversation Conversation(int unread, int hour) => new()
            {
                Channel = MessageChannel.Email, UnreadCount = unread, AssignedUserId = null, LastMessageAt = t0.AddHours(hour),
                Contact = new Contact { Name = $"{TestHarness.Prefix} r5 inbox {tag}", Phone = $"+9198{Random.Shared.Next(10000000, 99999999)}", Email = $"inbox-{Guid.NewGuid():N}@example.test", Type = "Lead", Status = "New", Source = "Import", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
            };
            var items = new[] { Conversation(2, 0), Conversation(0, 1), Conversation(1, 2) };
            db.ChatConversations.AddRange(items);
            await db.SaveChangesAsync();
            // Assigned after insert, without loading the user entity.
            await db.ChatConversations.Where(c => c.Id == items[1].Id || c.Id == items[2].Id)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.AssignedUserId, (int?)me));
            var ids = items.Select(i => i.Id).ToList();

            var counts = await ChatService.CountConversationsAsync(db.ChatConversations.Where(c => ids.Contains(c.Id)), me);
            run.Check("All counts every conversation in view", counts.All == 3, counts.All.ToString());
            run.Check("Unread counts conversations with unread messages", counts.Unread == 2, counts.Unread.ToString());
            run.Check("Mine counts conversations assigned to me", counts.Mine == 2, counts.Mine.ToString());

            // Sort: newest or oldest activity first, with keyset paging in either direction.
            var scope = db.ChatConversations.Where(c => ids.Contains(c.Id));
            var oldest = await ChatService.OrderConversations(scope, ChatCatalog.SortOldest, null).Select(c => c.Id).ToListAsync();
            run.Check("Oldest first lists the earliest activity first", oldest.SequenceEqual(ids), string.Join(",", oldest));
            var newest = await ChatService.OrderConversations(scope, ChatCatalog.SortNewest, null).Select(c => c.Id).ToListAsync();
            run.Check("Newest first lists the latest activity first", newest.SequenceEqual(ids.AsEnumerable().Reverse()), string.Join(",", newest));
            var afterSecond = ChatPaging.EncodeConversationCursor(items[1].LastMessageAt!.Value, items[1].Id);
            var nextOldest = await ChatService.OrderConversations(scope, ChatCatalog.SortOldest, afterSecond).Select(c => c.Id).ToListAsync();
            run.Check("the next page of Oldest first continues after the cursor", nextOldest.SequenceEqual([ids[2]]), string.Join(",", nextOldest));
            var nextNewest = await ChatService.OrderConversations(scope, ChatCatalog.SortNewest, afterSecond).Select(c => c.Id).ToListAsync();
            run.Check("the next page of Newest first continues after the cursor", nextNewest.SequenceEqual([ids[0]]), string.Join(",", nextNewest));
            var unknown = await ChatService.OrderConversations(scope, "sideways", null).Select(c => c.Id).ToListAsync();
            run.Check("an unknown sort falls back to Newest first", unknown.SequenceEqual(newest), string.Join(",", unknown));

            // Each quick view is a combination of the inbox's own filters, so the tabs and the filter panel agree.
            var readValues = ChatCatalog.ReadFilters.Select(o => o.Value).ToHashSet();
            var ownerValues = ChatCatalog.AssigneeFilters.Select(o => o.Value).ToHashSet();
            run.Check("every quick view maps onto existing read and owner filters",
                ChatCatalog.QuickViews.All(v => readValues.Contains(v.ReadFilter) && ownerValues.Contains(v.AssigneeFilter)));
            var byView = ChatService.CountsByView(counts);
            run.Check("counts are reported for every quick view", ChatCatalog.QuickViews.All(v => byView.ContainsKey(v.Value)),
                string.Join(",", byView.Keys));

            db.AuditLogs.AddRange(
                new AuditLog { Event = "Chat.Assigned", Category = "Data", EntityType = "ChatConversation", EntityId = ids[0].ToString(), Description = $"{TestHarness.Prefix} hist conv {tag}" },
                new AuditLog { Event = "Contact.Updated", Category = "Data", EntityType = "Contact", EntityId = "-77", Description = $"{TestHarness.Prefix} hist contact {tag}" },
                new AuditLog { Event = "Contact.Updated", Category = "Data", EntityType = "Contact", EntityId = "-78", Description = $"{TestHarness.Prefix} hist other {tag}" });
            await db.SaveChangesAsync();
            try
            {
                var history = await AuditQueries.ForEntities(db.AuditLogs, [$"ChatConversation:{ids[0]}", "Contact:-77"])
                    .Where(a => a.Description!.EndsWith(tag)).Select(a => a.Description).ToListAsync();
                run.Check("a record's history returns the events of exactly those records", history.Count == 2 && history.All(d => !d!.Contains("other")), string.Join(" | ", history));
                var none = await AuditQueries.ForEntities(db.AuditLogs, ["not-a-pair", "Contact:"]).Where(a => a.Description!.EndsWith(tag)).CountAsync();
                run.Check("malformed entity references match nothing rather than everything", none == 0, none.ToString());
            }
            finally
            {
                await db.AuditLogs.Where(a => a.Description!.EndsWith(tag) && a.Description.StartsWith(TestHarness.Prefix + " hist")).ExecuteDeleteAsync();
                await db.ChatConversations.Where(c => ids.Contains(c.Id)).ExecuteDeleteAsync();
            }
        });
    }
}
