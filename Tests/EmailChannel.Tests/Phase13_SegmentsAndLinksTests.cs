using Microsoft.Extensions.DependencyInjection;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services;
using WhatsAppCampaignApi.Services.Compliance;
using WhatsAppCampaignApi.Services.Segments;

namespace EmailChannel.Tests;

/// <summary>
/// Phase 13 — dynamic segments (rules over fields, tags, consent and engagement; validation),
/// the send-time audience refresh, and the per-link click report.
/// </summary>
public static class Phase13_SegmentsAndLinksTests
{
    public static async Task RunAsync(TestHarness harness, TestRun run)
    {
        run.BeginPhase("Phase 13 — segments, audience refresh, link report");

        var city = $"zzcity{harness.Tag}";
        var tag = $"zztag{harness.Tag}";
        var contactIds = new List<int>();
        int campaignId = 0;
        var recipientIds = new List<int>();

        await harness.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            Contact Make(int i, string? contactCity, string? tags) => new()
            {
                Name = $"{TestHarness.Prefix} Segment {i} {harness.Tag}",
                Phone = $"+9194{Random.Shared.Next(10000000, 99999999)}",
                Email = $"segment-{i}-{harness.Tag}@example.test",
                Type = "Lead",
                Status = "New",
                Source = "Import",
                City = contactCity,
                Tags = tags,
                IsActive = true
            };

            var contacts = new[]
            {
                Make(1, city, $"vip, {tag}"),
                Make(2, city, "vip"),
                Make(3, "elsewhere", tag),
                Make(4, city, $"{tag}x")          // a different tag that merely starts with ours
            };
            db.Contacts.AddRange(contacts);
            await db.SaveChangesAsync();
            contactIds.AddRange(contacts.Select(c => c.Id));

            var campaign = new Campaign
            {
                Name = $"{TestHarness.Prefix} segment campaign {harness.Tag}",
                Channel = MessageChannel.Email,
                RelationType = "Lead",
                Status = CampaignStatus.Sent
            };
            foreach (var c in contacts.Take(2))
            {
                campaign.CampaignContacts.Add(new CampaignContact
                {
                    ContactId = c.Id,
                    Status = MessageStatus.Sent,
                    SentAt = DateTime.UtcNow.AddDays(-2),
                    OpenedAt = c == contacts[0] ? DateTime.UtcNow.AddDays(-1) : null
                });
            }
            db.Campaigns.Add(campaign);
            await db.SaveChangesAsync();
            campaignId = campaign.Id;
            recipientIds.AddRange(campaign.CampaignContacts.Select(cc => cc.Id));
        });

        async Task<int> CountAsync(SegmentRuleGroup rules)
        {
            var count = -1;
            await harness.InScopeAsync(async services =>
                count = (await services.GetRequiredService<ISegmentService>().PreviewAsync(rules)).Count);
            return count;
        }

        SegmentRule R(string field, string op, string? value = null, int? days = null) => new() { Field = field, Op = op, Value = value, Days = days };
        SegmentRuleGroup All(params SegmentRule[] rules) => new() { Match = "all", Rules = [.. rules] };
        SegmentRuleGroup Any(params SegmentRule[] rules) => new() { Match = "any", Rules = [.. rules] };

        try
        {
            run.Section("Field and tag rules");
            run.Check("city is (case-insensitive)", await CountAsync(All(R("city", "eq", city.ToUpperInvariant()))) == 3);
            run.Check("a whole tag matches, not a prefix of another", await CountAsync(All(R("tag", "has", tag))) == 2);
            run.Check("all: city AND tag", await CountAsync(All(R("city", "eq", city), R("tag", "has", tag))) == 1);
            run.Check("any: city OR tag", await CountAsync(Any(R("city", "eq", city), R("tag", "has", tag))) == 4);
            run.Check("does not have a tag", await CountAsync(All(R("city", "eq", city), R("tag", "notHas", tag))) == 2);

            run.Section("Engagement and consent rules");
            run.Check("opened in the last 7 days", await CountAsync(All(R("city", "eq", city), R("opened", "withinDays", days: 7))) == 1);
            run.Check("received but did not open", await CountAsync(All(R("received", "withinDays", days: 7), R("opened", "notWithinDays", days: 7), R("city", "eq", city))) == 1);

            await harness.InScopeAsync(async services =>
                await services.GetRequiredService<IConsentService>().SetAsync(contactIds[1], MessageChannel.Email, "all", ConsentStatus.OptedOut, "agent"));
            run.Check("consent: opted out of email", await CountAsync(All(R("city", "eq", city), R("consent", "eq", "Email:all:OptedOut"))) == 1);

            run.Section("Rules are validated, not ignored");
            Exception? bad = null;
            try { await CountAsync(All(R("passwordHash", "eq", "x"))); } catch (Exception ex) { bad = ex; }
            run.Check("an unknown field is refused", bad is ArgumentException, bad?.GetType().Name);
            bad = null;
            try { await CountAsync(All(R("opened", "eq", "x"))); } catch (Exception ex) { bad = ex; }
            run.Check("an operator a field does not support is refused", bad is ArgumentException, bad?.GetType().Name);
            bad = null;
            try { await CountAsync(new SegmentRuleGroup()); } catch (Exception ex) { bad = ex; }
            run.Check("a segment with no rules is refused", bad is ArgumentException, bad?.GetType().Name);

            run.Section("The audience is re-resolved when the campaign sends");
            int segmentId = 0, liveCampaignId = 0;
            await harness.InScopeAsync(async services =>
            {
                var segments = services.GetRequiredService<ISegmentService>();
                var saved = await segments.CreateAsync(new SaveSegmentRequest($"{TestHarness.Prefix} seg {harness.Tag}", null, All(R("tag", "has", tag))));
                segmentId = saved.Id;
                run.Check("a saved segment records its count", saved.CachedCount == 2, $"{saved.CachedCount}");

                var db = services.GetRequiredService<AppDbContext>();
                var live = new Campaign
                {
                    Name = $"{TestHarness.Prefix} segment live {harness.Tag}",
                    Channel = MessageChannel.Email,
                    RelationType = "Lead",
                    Status = CampaignStatus.Scheduled,
                    AudienceIsSegmentsOnly = true
                };
                foreach (var id in new[] { contactIds[0], contactIds[2] })
                    live.CampaignContacts.Add(new CampaignContact { ContactId = id, Status = MessageStatus.Pending });
                live.Segments.Add(new CampaignSegment { SegmentId = segmentId });
                db.Campaigns.Add(live);
                await db.SaveChangesAsync();
                liveCampaignId = live.Id;
            });

            // Contact 3 leaves the segment, contact 2 joins it, before the send.
            await harness.ExecAsync("""UPDATE "Contacts" SET "Tags" = 'none' WHERE "Id" = @id""", ("id", contactIds[2]));
            await harness.ExecAsync("""UPDATE "Contacts" SET "Tags" = @t WHERE "Id" = @id""", ("t", tag), ("id", contactIds[1]));

            await harness.InScopeAsync(async services =>
            {
                var db = services.GetRequiredService<AppDbContext>();
                var live = await db.Campaigns.FindAsync(liveCampaignId);
                await CampaignAudienceRefresher.RefreshAsync(db, services.GetRequiredService<ISegmentService>(), live!, CancellationToken.None);
            });

            var members = await harness.ScalarAsync("""
                SELECT string_agg("ContactId"::text, ',' ORDER BY "ContactId") FROM "CampaignContacts" WHERE "CampaignId" = @id
                """, ("id", liveCampaignId));
            var expected = string.Join(',', new[] { contactIds[0], contactIds[1] }.Order());
            run.Check("joiners are added and leavers dropped", members == expected, $"{members} (expected {expected})");
            run.Check("the recipient total follows",
                await harness.ScalarAsync("""SELECT "TotalRecipients" FROM "Campaigns" WHERE "Id" = @id""", ("id", liveCampaignId)) == "2");

            await harness.ExecAsync("""DELETE FROM "Campaigns" WHERE "Id" = @id""", ("id", liveCampaignId));
            await harness.ExecAsync("""DELETE FROM "Segments" WHERE "Id" = @id""", ("id", segmentId));

            run.Section("Link report");
            var now = DateTime.UtcNow;
            await harness.InScopeAsync(async services =>
            {
                var db = services.GetRequiredService<AppDbContext>();
                void Click(int recipient, string url, int minutesAgo) => db.EmailEvents.Add(new EmailEvent
                {
                    CampaignId = campaignId,
                    CampaignContactId = recipient,
                    EventKind = EmailEventKind.Clicked,
                    OriginalUrl = url,
                    OccurredAt = now.AddMinutes(-minutesAgo),
                    IdempotencyKey = $"test-click-{harness.Tag}-{recipient}-{minutesAgo}",
                    Source = "test",
                    CreatedAt = now
                });
                Click(recipientIds[0], "https://example.test/offer", 30);
                Click(recipientIds[0], "https://example.test/offer", 20);
                Click(recipientIds[1], "https://example.test/offer", 10);
                Click(recipientIds[1], "https://example.test/terms", 5);
                await db.SaveChangesAsync();
            });

            IReadOnlyList<WhatsAppCampaignApi.Models.DTOs.Campaigns.CampaignLinkClicks> links = [];
            await harness.InScopeAsync(async services =>
            {
                var campaigns = Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<CampaignService>(services);
                links = await campaigns.GetLinkReportAsync(campaignId);
            });
            var offer = links.FirstOrDefault(l => l.Url.EndsWith("/offer"));
            run.Check("the most clicked link is first", links.FirstOrDefault()?.Url.EndsWith("/offer") == true);
            run.Check("total clicks count every click", offer?.TotalClicks == 3, $"{offer?.TotalClicks}");
            run.Check("unique clickers count people", offer?.UniqueClickers == 2, $"{offer?.UniqueClickers}");
            run.Check("each link is listed once", links.Count == 2, $"{links.Count}");
        }
        finally
        {
            await harness.ExecAsync("""DELETE FROM "EmailEvents" WHERE "CampaignId" = @id""", ("id", campaignId));
            foreach (var id in contactIds)
            {
                await harness.ExecAsync("""DELETE FROM "ConsentEvents" WHERE "ContactId" = @id""", ("id", id));
                await harness.ExecAsync("""DELETE FROM "ContactConsents" WHERE "ContactId" = @id""", ("id", id));
            }
            await harness.ExecAsync("""DELETE FROM "Campaigns" WHERE "Id" = @id""", ("id", campaignId));
            await harness.ExecAsync("""DELETE FROM "Segments" WHERE "Name" LIKE @p""", ("p", $"{TestHarness.Prefix} seg%"));
        }
    }
}
