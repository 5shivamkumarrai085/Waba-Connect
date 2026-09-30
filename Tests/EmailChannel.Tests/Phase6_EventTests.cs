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
/// Phase 6 — suppression automation and one-click unsubscribe. (Delivery and bounce events arrive
/// by IMAP now that mail is SMTP only.)
/// </summary>
public static class Phase6_EventTests
{
    public static async Task RunAsync(TestHarness harness, TestRun run)
    {
        run.BeginPhase("Phase 6 — suppression and unsubscribe");

        var contactId = 0;
        var campaignId = 0;
        var recipientId = 0;
        var configId = 0;
        var templateId = 0;
        var providerMessageId = $"<events-{harness.Tag}-0001@example.test>";
        var recipientEmail = $"events-{harness.Tag}@example.test";

        try
        {
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
}
