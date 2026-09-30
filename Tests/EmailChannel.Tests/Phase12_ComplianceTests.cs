using Microsoft.Extensions.DependencyInjection;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Setup;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Compliance;
using WhatsAppCampaignApi.Services.Interfaces;

namespace EmailChannel.Tests;

/// <summary>
/// Phase 12 — compliance: quiet hours and local-time sends (pure time arithmetic), consent
/// recording with history, WhatsApp STOP/START keywords, and the guard's batch decisions —
/// opt-out, WhatsApp opt-in, the frequency cap, and the transactional exemption.
/// </summary>
public static class Phase12_ComplianceTests
{
    public static async Task RunAsync(TestHarness harness, TestRun run)
    {
        run.BeginPhase("Phase 12 — consent, quiet hours, local time, frequency cap");

        TimeArithmetic(run);
        await ConsentAndDecisionsAsync(harness, run);
    }

    private static readonly TimeZoneInfo Kolkata = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

    private static CompliancePolicy Policy(bool quiet = true, int cap = 0, bool optIn = false) =>
        new(quiet, 21, 9, Kolkata, cap, 7, optIn);

    private static void TimeArithmetic(TestRun run)
    {
        run.Section("Quiet hours (21:00–09:00, Asia/Kolkata)");

        // 22:30 IST on 1 Oct = 17:00 UTC → released at 09:00 IST on 2 Oct = 03:30 UTC.
        var late = new DateTime(2026, 10, 1, 17, 0, 0, DateTimeKind.Utc);
        var release = ComplianceGuard.QuietHoursRelease(Policy(), Kolkata, late);
        run.Check("a message due at 22:30 is held until 09:00 next morning",
            release == new DateTime(2026, 10, 2, 3, 30, 0, DateTimeKind.Utc), release?.ToString("O"));

        // 02:00 IST on 2 Oct = 20:30 UTC on 1 Oct → released 09:00 IST the same local day.
        var night = new DateTime(2026, 10, 1, 20, 30, 0, DateTimeKind.Utc);
        var releaseNight = ComplianceGuard.QuietHoursRelease(Policy(), Kolkata, night);
        run.Check("a message due at 02:00 is held until 09:00 that morning",
            releaseNight == new DateTime(2026, 10, 2, 3, 30, 0, DateTimeKind.Utc), releaseNight?.ToString("O"));

        // 14:00 IST = 08:30 UTC → not quiet.
        var day = new DateTime(2026, 10, 1, 8, 30, 0, DateTimeKind.Utc);
        run.Check("a message due at 14:00 is not held", ComplianceGuard.QuietHoursRelease(Policy(), Kolkata, day) is null);
        run.Check("with quiet hours off nothing is held", ComplianceGuard.QuietHoursRelease(Policy(quiet: false), Kolkata, late) is null);

        run.Section("Recipient-local send time");
        var campaign = new Campaign
        {
            ScheduleType = ScheduleType.RecipientLocalTime,
            LocalSendAt = new DateTime(2026, 10, 3, 10, 0, 0, DateTimeKind.Unspecified)
        };
        var now = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var india = ComplianceGuard.EarliestSendUtc(campaign, Policy(quiet: false), "Asia/Kolkata", now);
        var london = ComplianceGuard.EarliestSendUtc(campaign, Policy(quiet: false), "Europe/London", now);
        run.Check("10:00 in Kolkata is 04:30 UTC", india == new DateTime(2026, 10, 3, 4, 30, 0, DateTimeKind.Utc), india?.ToString("O"));
        run.Check("10:00 in London (BST) is 09:00 UTC", london == new DateTime(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc), london?.ToString("O"));
        var fallback = ComplianceGuard.EarliestSendUtc(campaign, Policy(quiet: false), "Not/AZone", now);
        run.Check("an unknown zone falls back to the account default", fallback == india, fallback?.ToString("O"));

        var early = new Campaign
        {
            ScheduleType = ScheduleType.RecipientLocalTime,
            LocalSendAt = new DateTime(2026, 10, 3, 7, 0, 0, DateTimeKind.Unspecified)
        };
        var heldToNine = ComplianceGuard.EarliestSendUtc(early, Policy(), "Asia/Kolkata", now);
        run.Check("a local time inside quiet hours moves to when they end",
            heldToNine == new DateTime(2026, 10, 3, 3, 30, 0, DateTimeKind.Utc), heldToNine?.ToString("O"));

        var transactional = new Campaign { ScheduleType = ScheduleType.Immediate, IsTransactional = true };
        run.Check("a transactional message is never held for quiet hours",
            ComplianceGuard.EarliestSendUtc(transactional, Policy(), "Asia/Kolkata", late) is null);
    }

    private static async Task ConsentAndDecisionsAsync(TestHarness harness, TestRun run)
    {
        var contactIds = new List<int>();
        int priorCampaignId = 0, templateId = 0;

        await harness.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var contacts = Enumerable.Range(1, 4).Select(i => new Contact
            {
                Name = $"{TestHarness.Prefix} Compliance {i} {harness.Tag}",
                Phone = $"+9195{Random.Shared.Next(10000000, 99999999)}",
                Email = $"compliance-{i}-{harness.Tag}@example.test",
                Type = "Lead",
                Status = "New",
                Source = "Import",
                IsActive = true
            }).ToList();
            db.Contacts.AddRange(contacts);

            var template = new Template { Name = $"zz_emailtest_compliance_{harness.Tag}", Status = TemplateStatus.Approved, BodyText = "Hi", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            db.Templates.Add(template);
            await db.SaveChangesAsync();
            contactIds.AddRange(contacts.Select(c => c.Id));
            templateId = template.Id;

            // Contact 3 already received two marketing messages this week.
            var prior = new Campaign
            {
                Name = $"{TestHarness.Prefix} compliance prior {harness.Tag}",
                Channel = MessageChannel.WhatsApp,
                TemplateId = template.Id,
                RelationType = "Lead",
                Status = CampaignStatus.Sent
            };
            prior.CampaignContacts.Add(new CampaignContact { ContactId = contacts[2].Id, Status = MessageStatus.Delivered, SentAt = DateTime.UtcNow.AddDays(-1) });
            db.Campaigns.Add(prior);
            await db.SaveChangesAsync();
            priorCampaignId = prior.Id;

            var second = new Campaign
            {
                Name = $"{TestHarness.Prefix} compliance prior 2 {harness.Tag}",
                Channel = MessageChannel.Email,
                RelationType = "Lead",
                Status = CampaignStatus.Sent
            };
            second.CampaignContacts.Add(new CampaignContact { ContactId = contacts[2].Id, Status = MessageStatus.Sent, SentAt = DateTime.UtcNow.AddDays(-2) });
            db.Campaigns.Add(second);
            await db.SaveChangesAsync();
        });

        try
        {
            run.Section("Consent is recorded once, with history");
            await harness.InScopeAsync(async services =>
            {
                var consent = services.GetRequiredService<IConsentService>();
                var first = await consent.SetAsync(contactIds[0], MessageChannel.WhatsApp, "all", ConsentStatus.OptedOut, "agent", new { note = "asked by phone" });
                var again = await consent.SetAsync(contactIds[0], MessageChannel.WhatsApp, "all", ConsentStatus.OptedOut, "agent");
                run.Check("the first opt-out is recorded", first);
                run.Check("repeating the same answer is not a change", !again);

                var history = await consent.GetHistoryAsync(contactIds[0]);
                run.Check("exactly one history entry was written", history.Count == 1, $"{history.Count}");
                run.Check("the evidence is kept", history.FirstOrDefault()?.ProofJson?.Contains("asked by phone") == true, history.FirstOrDefault()?.ProofJson);

                run.Section("WhatsApp keywords");
                var stop = await consent.TryApplyWhatsAppKeywordAsync(contactIds[1], "  stop ", "wamid.test-stop");
                run.Check("'stop' opts the contact out", stop == ConsentStatus.OptedOut, $"{stop}");
                var start = await consent.TryApplyWhatsAppKeywordAsync(contactIds[1], "START", "wamid.test-start");
                run.Check("'START' opts them back in", start == ConsentStatus.OptedIn, $"{start}");
                var chat = await consent.TryApplyWhatsAppKeywordAsync(contactIds[1], "stop sending me the old price list please", "wamid.test-chat");
                run.Check("a sentence containing 'stop' is not a keyword", chat is null, $"{chat}");

                // Opted in, so that only the frequency cap can exclude this contact below.
                await consent.SetAsync(contactIds[2], MessageChannel.WhatsApp, "all", ConsentStatus.OptedIn, "agent");
            });

            run.Section("The guard's decisions");
            var campaign = new Campaign { Id = -1, Channel = MessageChannel.WhatsApp, ScheduleType = ScheduleType.Immediate };
            var candidates = contactIds.Select((id, i) => new ComplianceCandidate(1000 + i, id, "Asia/Kolkata")).ToList();
            var noon = new DateTime(2026, 10, 1, 6, 30, 0, DateTimeKind.Utc); // 12:00 IST

            IReadOnlyDictionary<int, ComplianceDecision> decisions = new Dictionary<int, ComplianceDecision>();
            await harness.InScopeAsync(async services =>
            {
                var guard = new ComplianceGuard(services.GetRequiredService<AppDbContext>(), new FixedSettings(cap: 2, optIn: true));
                decisions = await guard.EvaluateAsync(campaign, candidates, noon);
            });

            run.Check("an opted-out contact is excluded", decisions[1000].IsExcluded && decisions[1000].ExclusionReason!.Contains("opted out"), decisions[1000].ExclusionReason);
            run.Check("an opted-in contact is allowed", !decisions[1001].IsExcluded, decisions[1001].ExclusionReason);
            run.Check("a contact at the frequency cap is excluded",
                decisions[1002].IsExcluded && decisions[1002].ExclusionReason!.Contains("Frequency cap"), decisions[1002].ExclusionReason);
            run.Check("without an opt-in, WhatsApp marketing is excluded when opt-in is required",
                decisions[1003].IsExcluded && decisions[1003].ExclusionReason!.Contains("opt-in"), decisions[1003].ExclusionReason);

            var transactional = new Campaign { Id = -2, Channel = MessageChannel.WhatsApp, ScheduleType = ScheduleType.Immediate, IsTransactional = true };
            await harness.InScopeAsync(async services =>
            {
                var guard = new ComplianceGuard(services.GetRequiredService<AppDbContext>(), new FixedSettings(cap: 2, optIn: true));
                decisions = await guard.EvaluateAsync(transactional, candidates, noon);
            });
            run.Check("a transactional message ignores the cap", !decisions[1002].IsExcluded, decisions[1002].ExclusionReason);
            run.Check("and the opt-in requirement", !decisions[1003].IsExcluded, decisions[1003].ExclusionReason);
            run.Check("but never an opt-out", decisions[1000].IsExcluded, decisions[1000].ExclusionReason);
        }
        finally
        {
            foreach (var id in contactIds)
            {
                await harness.ExecAsync("""DELETE FROM "ConsentEvents" WHERE "ContactId" = @id""", ("id", id));
                await harness.ExecAsync("""DELETE FROM "ContactConsents" WHERE "ContactId" = @id""", ("id", id));
                await harness.ExecAsync("""DELETE FROM "CampaignContacts" WHERE "ContactId" = @id""", ("id", id));
            }
            await harness.ExecAsync("""DELETE FROM "Campaigns" WHERE "Name" LIKE @p""", ("p", $"{TestHarness.Prefix} compliance prior%"));
            await harness.ExecAsync("""DELETE FROM "Templates" WHERE "Id" = @id""", ("id", templateId));
        }
    }

    /// <summary>A settings source with fixed compliance values, so the decisions are deterministic.</summary>
    private sealed class FixedSettings(int cap, bool optIn) : IOmniSettingsService
    {
        public Task<string?> GetValueAsync(string key) => Task.FromResult<string?>(key == ComplianceGuard.TimeZoneKey ? "Asia/Kolkata" : null);
        public Task<bool> GetFlagAsync(string key, bool fallback = false) => Task.FromResult(key switch
        {
            ComplianceGuard.QuietHoursEnabledKey => true,
            ComplianceGuard.WhatsAppOptInKey => optIn,
            _ => fallback
        });
        public Task<int> GetNumberAsync(string key, int fallback) => Task.FromResult(key switch
        {
            ComplianceGuard.CapMaxKey => cap,
            ComplianceGuard.CapDaysKey => 7,
            _ => fallback
        });
        public Task<IReadOnlyList<string>> GetListAsync(string key) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<OmniSettingsSchemaDto> GetSchemaAsync() => throw new NotSupportedException();
        public Task<OmniSettingsSchemaDto> SaveSectionAsync(string sectionKey, SaveOmniSettingsRequest request) => throw new NotSupportedException();
        public void InvalidateCache() { }
    }
}
