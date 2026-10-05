using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.Campaigns;
using WhatsAppCampaignApi.Models.DTOs.Contacts;
using WhatsAppCampaignApi.Models.DTOs.Email;
using WhatsAppCampaignApi.Models.DTOs.Setup;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Models.Options;
using WhatsAppCampaignApi.Services;
using WhatsAppCampaignApi.Services.Campaigns;
using WhatsAppCampaignApi.Services.Catalogs;
using WhatsAppCampaignApi.Services.Email;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Services.Queue;
using WhatsAppCampaignApi.Services.Segments;
using WhatsAppCampaignApi.Services.WhatsApp;
using WhatsAppCampaignApi.Validators;

namespace EmailChannel.Tests;

/// <summary>
/// Phase 22 — the round-4 fixes: editing a campaign, the A/B split and decision, validation before
/// save, contact types and consent topics from their managed lists, required contact fields and
/// age, the versioned job queue, holding a campaign on an unusable connection, and the platform
/// SMTP sender that replaced Amazon SES.
/// </summary>
public static class Phase22_Round4Tests
{
    public static async Task RunAsync(TestHarness harness, TestRun run)
    {
        run.BeginPhase("Phase 22 — round 4: campaigns, A/B, contacts, queue contract, SMTP");

        int templateA = 0, templateB = 0, templateC = 0, connectionId = 0;
        var contactIds = new List<int>();
        var dispatched = new List<int>();

        await harness.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            Template T(string suffix) => new() { Name = $"zz_emailtest_r4_{suffix}_{harness.Tag}", Status = TemplateStatus.Approved, BodyText = "Hi", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            var (a, b, c) = (T("a"), T("b"), T("c"));
            var connection = new Connection { Name = $"{TestHarness.Prefix} r4 conn {harness.Tag}", IsActive = true };
            db.Templates.AddRange(a, b, c);
            db.Connections.Add(connection);
            var types = new[] { "Lead", "Customer", "Vendor" };
            db.Contacts.AddRange(Enumerable.Range(1, 90).Select(i => new Contact
            {
                Name = $"{TestHarness.Prefix} R4 {i} {harness.Tag}",
                Phone = $"+9192{Random.Shared.Next(10000000, 99999999)}",
                Type = types[i % 3],
                Status = "New",
                Source = "Import",
                IsActive = true,
                DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-(15 + i % 50))
            }));
            await db.SaveChangesAsync();
            harness.TrackTemplate(a.Id, b.Id, c.Id);
            (templateA, templateB, templateC, connectionId) = (a.Id, b.Id, c.Id, connection.Id);
            contactIds.AddRange(await db.Contacts.Where(x => x.Name.StartsWith($"{TestHarness.Prefix} R4 ") && x.Name.EndsWith(harness.Tag)).Select(x => x.Id).ToListAsync());
        });

        ICampaignService Build(IServiceProvider services)
        {
            var db = services.GetRequiredService<AppDbContext>();
            var dispatcher = new RecordingDispatcher(dispatched);
            var ab = new AbTestService(db, services.GetRequiredService<IEmailCampaignDispatcher>(), dispatcher,
                services.GetRequiredService<IAuditService>(), NullLogger<AbTestService>.Instance);
            return ActivatorUtilities.CreateInstance<CampaignService>(services, dispatcher, ab);
        }

        CreateCampaignRequest Request(string name, List<int> contacts, AbTestRequest? ab = null, string relation = "Lead", string schedule = "Immediate", string? topic = null) => new()
        {
            Name = $"{TestHarness.Prefix} {name} {harness.Tag}",
            Channel = "WhatsApp",
            TemplateId = templateA,
            ConnectionId = connectionId,
            RelationType = relation,
            ScheduleType = schedule,
            ScheduledAt = schedule == "Scheduled" ? DateTime.UtcNow.AddDays(2) : null,
            ContactIds = contacts,
            Topic = topic,
            AbTest = ab,
            Variables = [new CampaignVariableRequest { VariableName = "1", VariableValue = "there" }]
        };

        try
        {
            run.Section("Three variants split exactly evenly");
            var threeWay = await Create(harness, Build, Request("ab3", contactIds,
                new AbTestRequest { Percent = 30, Metric = "read", DecideAfterHours = 2, Variants = [new AbVariantRequest { TemplateId = templateB }, new AbVariantRequest { TemplateId = templateC }] }));
            var perLabel = new Dictionary<string, int>();
            foreach (var label in new[] { "A", "B", "C" })
                perLabel[label] = await harness.CountAsync("""SELECT count(*) FROM "CampaignContacts" cc JOIN "CampaignVariants" v ON v."Id" = cc."VariantId" WHERE cc."CampaignId" = @id AND v."Label" = @l""", ("id", threeWay), ("l", label));
            var held = await harness.CountAsync("""SELECT count(*) FROM "CampaignContacts" WHERE "CampaignId" = @id AND "HeldForWinner" """, ("id", threeWay));
            run.Check("the test group is 30% of 90, rounded up (27)", perLabel.Values.Sum() == 27, string.Join(", ", perLabel.Select(p => $"{p.Key} {p.Value}")));
            run.Check("each of A, B and C gets exactly a third (9)", perLabel.Values.All(v => v == 9), string.Join(", ", perLabel.Select(p => $"{p.Key} {p.Value}")));
            run.Check("the other 63 are held for the winner", held == 63, $"{held}");
            var window = await harness.ScalarAsync("""SELECT "AbDecideAfterHours" FROM "Campaigns" WHERE "Id" = @id""", ("id", threeWay));
            run.Check("the test window is stored, so it can be measured from the first send", window == "2", window);

            run.Section("No data, no winner");
            int? starvedWinner = 0;
            await harness.InScopeAsync(async s => starvedWinner = await Build(s).DecideAbTestAsync(threeWay, null));
            var decidedAt = await harness.ScalarAsync("""SELECT "AbDecidedAt" FROM "Campaigns" WHERE "Id" = @id""", ("id", threeWay));
            var postponed = await harness.CountAsync("""SELECT count(*) FROM "Campaigns" WHERE "Id" = @id AND "AbDecideAt" > now() + interval '20 minutes'""", ("id", threeWay));
            run.Check("with nothing sent yet, no winner is picked", starvedWinner is null && decidedAt is null, $"{starvedWinner} / {decidedAt}");
            run.Check("the decision is postponed by the recheck interval", postponed == 1);

            run.Section("No signal: nobody read any variant");
            async Task SentHoursAgoAsync(int campaign, int hours) => await harness.ExecAsync("""
                UPDATE "CampaignContacts" SET "Status" = 'Sent', "SentAt" = now() - make_interval(hours => @h)
                 WHERE "CampaignId" = @id AND "VariantId" IS NOT NULL
                """, ("id", campaign), ("h", hours));
            async Task<int?> DecideAsync(int campaign)
            {
                int? decided = null;
                await harness.InScopeAsync(async s => decided = await Build(s).DecideAbTestAsync(campaign, null));
                return decided;
            }
            async Task<int> VariantIdAsync(int campaign, string label) =>
                int.Parse((await harness.ScalarAsync("""SELECT "Id" FROM "CampaignVariants" WHERE "CampaignId" = @id AND "Label" = @l""", ("id", campaign), ("l", label)))!);

            // Past the 2-hour window but inside the grace period, with zero reads everywhere.
            await SentHoursAgoAsync(threeWay, 3);
            run.Check("inside the grace period a zero-signal test keeps waiting instead of picking A", await DecideAsync(threeWay) is null);

            var graceOver = 2 + CampaignFeatureCatalog.AbNoSignalGraceHours.Default + 1;
            await SentHoursAgoAsync(threeWay, graceOver);
            var noSignalWinner = await DecideAsync(threeWay);
            var reason = await harness.ScalarAsync("""SELECT "AbDecisionReason" FROM "Campaigns" WHERE "Id" = @id""", ("id", threeWay));
            run.Check("after the grace period variant A is kept", noSignalWinner == await VariantIdAsync(threeWay, "A"), $"{noSignalWinner}");
            run.Check("and the decision says there was no signal", reason == "no-signal", reason);

            var twoWay = await Create(harness, Build, Request("ab2", contactIds.Take(20).ToList(),
                new AbTestRequest { Percent = 100, Metric = "read", DecideAfterHours = 2, Variants = [new AbVariantRequest { TemplateId = templateB }] }));
            await SentHoursAgoAsync(twoWay, 3);
            await harness.ExecAsync("""
                UPDATE "CampaignContacts" SET "ReadAt" = now() WHERE "Id" = (
                    SELECT cc."Id" FROM "CampaignContacts" cc JOIN "CampaignVariants" v ON v."Id" = cc."VariantId"
                     WHERE cc."CampaignId" = @id AND v."Label" = 'B' LIMIT 1)
                """, ("id", twoWay));
            var bestWinner = await DecideAsync(twoWay);
            var bestReason = await harness.ScalarAsync("""SELECT "AbDecisionReason" FROM "Campaigns" WHERE "Id" = @id""", ("id", twoWay));
            run.Check("with a signal, the better variant wins (B with one read)", bestWinner == await VariantIdAsync(twoWay, "B"), $"{bestWinner}");
            run.Check("and the decision says it was the best rate", bestReason == "best-rate", bestReason);

            run.Section("Invalid settings are refused before anything is saved");
            var badName = $"{TestHarness.Prefix} ab-invalid {harness.Tag}";
            Exception? bad = null;
            try
            {
                await harness.InScopeAsync(async s => await Build(s).CreateAsync(Request("ab-invalid", contactIds.Take(5).ToList(),
                    new AbTestRequest { Percent = 50, Metric = "open", Variants = [new AbVariantRequest { TemplateId = templateB }] })));
            }
            catch (Exception ex) { bad = ex; }
            var orphan = await harness.CountAsync("""SELECT count(*) FROM "Campaigns" WHERE "Name" = @n""", ("n", badName));
            run.Check("a WhatsApp test decided by email opens is refused", bad is ArgumentException, bad?.GetType().Name);
            run.Check("and no half-created campaign is left behind", orphan == 0, $"{orphan}");

            run.Section("Contact types come from the managed list");
            var multi = await Create(harness, Build, Request("types", contactIds.Take(6).ToList(), relation: "lead, customer,VENDOR"));
            var storedTypes = await harness.ScalarAsync("""SELECT "RelationType" FROM "Campaigns" WHERE "Id" = @id""", ("id", multi));
            run.Check("all three selected types are stored, in their canonical spelling", storedTypes == "Lead,Customer,Vendor", storedTypes);
            Exception? unknownType = null;
            try { await Create(harness, Build, Request("types-bad", contactIds.Take(3).ToList(), relation: "Martian")); } catch (Exception ex) { unknownType = ex; }
            run.Check("a type that is not in the list is refused", unknownType is ArgumentException, unknownType?.Message);

            run.Section("Consent topics come from the settings");
            Exception? badTopic = null;
            try { await Create(harness, Build, Request("topic-bad", contactIds.Take(3).ToList(), topic: $"not-a-topic-{harness.Tag}")); } catch (Exception ex) { badTopic = ex; }
            run.Check("a topic that is not configured is refused", badTopic is ArgumentException, badTopic?.Message);

            run.Section("Editing a campaign");
            var scheduled = await Create(harness, Build, Request("edit", contactIds.Take(20).ToList(), schedule: "Scheduled"));
            Exception? editError = null;
            try
            {
                await harness.InScopeAsync(async s => await Build(s).UpdateAsync(scheduled, Request("edit", contactIds.Skip(20).Take(7).ToList(), schedule: "Scheduled")));
            }
            catch (Exception ex) { editError = ex; }
            var recipients = await harness.CountAsync("""SELECT count(*) FROM "CampaignContacts" WHERE "CampaignId" = @id""", ("id", scheduled));
            var total = await harness.ScalarAsync("""SELECT "TotalRecipients" FROM "Campaigns" WHERE "Id" = @id""", ("id", scheduled));
            run.Check("saving an edited campaign with variables and recipients succeeds (was a 500)", editError is null, editError?.Message);
            run.Check("its recipients are replaced, not added to", recipients == 7 && total == "7", $"{recipients} rows, total {total}");

            run.Section("Required contact fields and age");
            await harness.InScopeAsync(async s =>
            {
                var db = s.GetRequiredService<AppDbContext>();
                var cache = s.GetRequiredService<IMemoryCache>();
                var original = await db.AppSettings.AsNoTracking().Where(x => x.Key == ContactFieldCatalog.RequiredFieldsSettingKey).Select(x => x.Value).FirstOrDefaultAsync();
                try
                {
                    var validator = new CreateContactValidator(new ContactLookupValidatorCache(db, cache));
                    CreateContactRequest Valid() => new() { Name = "Test Person", Phone = "+919876543210", Type = "Lead", Status = "New", Source = "Import", Email = "person@example.test", DateOfBirth = new DateOnly(1990, 1, 1) };

                    await SetRequiredAsync(db, cache, ["email", "dateOfBirth"]);
                    run.Check("a complete contact is valid", (await validator.ValidateAsync(Valid())).IsValid);
                    var noEmail = Valid(); noEmail.Email = null; noEmail.DateOfBirth = null;
                    var errors = (await validator.ValidateAsync(noEmail)).Errors.Select(e => e.ErrorMessage).ToList();
                    run.Check("email is required when the settings say so", errors.Contains("Email is required."), string.Join(" | ", errors));
                    run.Check("date of birth is required when the settings say so", errors.Contains("Date of birth is required."), string.Join(" | ", errors));

                    await SetRequiredAsync(db, cache, []);
                    run.Check("with nothing extra required, the same contact is valid", (await validator.ValidateAsync(noEmail)).IsValid);

                    var future = Valid(); future.DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
                    run.Check("a date of birth in the future is refused", !(await validator.ValidateAsync(future)).IsValid);
                    var ancient = Valid(); ancient.DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-(ContactFieldCatalog.AgeYears.Max + 1));
                    run.Check($"an age over {ContactFieldCatalog.AgeYears.Max} is refused", !(await validator.ValidateAsync(ancient)).IsValid);
                    var badEmail = Valid(); badEmail.Email = "not-an-address";
                    run.Check("a malformed email is refused", !(await validator.ValidateAsync(badEmail)).IsValid);
                    var badType = Valid(); badType.Type = "Martian";
                    run.Check("a contact type outside the managed list is refused", !(await validator.ValidateAsync(badType)).IsValid);
                }
                finally
                {
                    var row = await db.AppSettings.FirstOrDefaultAsync(x => x.Key == ContactFieldCatalog.RequiredFieldsSettingKey);
                    if (row is not null && original is not null) { row.Value = original; await db.SaveChangesAsync(); }
                    cache.Remove(ContactLookupValidatorCache.RequiredFieldsCacheKey);
                }
            });
            run.Check("age is whole years, counting the birthday itself",
                ContactFieldCatalog.AgeOn(new DateOnly(2000, 6, 15), new DateOnly(2026, 6, 15)) == 26
                && ContactFieldCatalog.AgeOn(new DateOnly(2000, 6, 15), new DateOnly(2026, 6, 14)) == 25);

            await harness.InScopeAsync(async s =>
            {
                var db = s.GetRequiredService<AppDbContext>();
                var rules = new SegmentRuleGroup { Match = "all", Rules = [new SegmentRule { Field = "age", Op = "gte", Value = "40" }] };
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var expected = await db.Contacts.CountAsync(c => contactIds.Contains(c.Id) && c.DateOfBirth <= today.AddYears(-40));
                var viaSegment = await SegmentQueryBuilder.Apply(db, db.Contacts.Where(c => contactIds.Contains(c.Id)), rules, DateTime.UtcNow).CountAsync();
                run.Check("an \"age is at least 40\" segment matches exactly the contacts aged 40+", viaSegment == expected && expected > 0, $"{viaSegment} vs {expected}");
            });

            run.Section("Contacts CSV import follows the same field rules");
            {
                var required = new HashSet<string>(ContactFieldCatalog.DefaultRequired, StringComparer.OrdinalIgnoreCase);
                var layout = CsvContactColumns.Layout(required, "Lead", out var optional);
                var headers = layout.Select(c => c.Header).ToList();
                run.Check("the layout asks for every field required by default (email, date_of_birth)",
                    headers.Contains("email") && headers.Contains("date_of_birth"), string.Join(",", headers));
                run.Check("and lists the rest as optional columns", optional.Contains("company") && !optional.Contains("email"), string.Join(",", optional));

                var columns = CsvContactColumns.Map(headers);
                var row = layout.Select(c => c.Example).ToList();
                var imported = new Contact();
                run.Check("the sample row it produces imports cleanly",
                    CsvContactColumns.Apply(imported, row, columns, required) is null && imported.Email is not null && imported.DateOfBirth is not null);

                CsvRowError? RowWith(string header, string value)
                {
                    var copy = row.ToList();
                    copy[headers.IndexOf(header)] = value;
                    return CsvContactColumns.Apply(new Contact(), copy, columns, required);
                }
                run.Check("a blank required email is reported on its column", RowWith("email", "") is { Column: "email" } e1 && e1.Reason.Contains("required"));
                run.Check("a malformed email is reported", RowWith("email", "not-an-email")?.Column == "email");
                run.Check("a non-ISO date of birth is reported", RowWith("date_of_birth", "15/06/1990")?.Column == "date_of_birth");
                run.Check("a date of birth in the future is reported", RowWith("date_of_birth", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2).ToString("yyyy-MM-dd"))?.Column == "date_of_birth");
                run.Check("any spelling of a field's name is recognised as its column",
                    CsvContactColumns.Map(["Date of birth", "zipCode"]) is var alt && alt.ContainsKey("dateOfBirth") && alt.ContainsKey("zipCode"));
                run.Check("every importable field can be written to a contact",
                    CsvContactColumns.Importable.All(f =>
                    {
                        var value = f.Kind switch { "email" => "a@example.com", "date" => "1990-01-01", _ => "x" };
                        var only = new Dictionary<string, int> { [f.Key] = 0 };
                        return CsvContactColumns.Apply(new Contact(), [value], only, new HashSet<string>()) is null;
                    }));
            }

            run.Section("The job queue contract");
            run.Check("every queue name carries the contract version, so an older build cannot take its jobs",
                QueueNames.All.All(q => q.EndsWith("." + QueueNames.ContractVersion)), string.Join(", ", QueueNames.All));

            run.Section("An unusable connection holds the campaign instead of failing recipients");
            await HoldAsync(harness, run);

            run.Section("Platform SMTP (system mail)");
            var off = new SmtpEmailSender(Options.Create(new SmtpOptions()), NullLogger<SmtpEmailSender>.Instance);
            run.Check("unconfigured SMTP reports itself as off", !off.IsEnabled);
            run.Check("and sending returns false instead of throwing", !await off.SendAsync("a@example.test", "A", "s", "<p>h</p>", "h"));
            run.Check("a host plus the username counts as configured (the address defaults to it)",
                new SmtpOptions { Host = "mail.example.test", Username = "me@example.test" }.IsConfigured);

            await using (var smtp = new FakeSmtpServer())
            {
                var plain = new SmtpEmailSender(Options.Create(new SmtpOptions { Host = "127.0.0.1", Port = smtp.Port, FromAddress = "system@example.test" }), NullLogger<SmtpEmailSender>.Instance);
                var sent = await plain.SendAsync("to@example.test", "To", "subject", "<p>h</p>", "h");
                run.Check("a server that cannot upgrade to TLS is refused, never used in plaintext", !sent && smtp.MessageCount == 0, $"sent {sent}, messages {smtp.MessageCount}");
            }

            await harness.InScopeAsync(async s =>
            {
                var mail = new SystemMailService(s.GetRequiredService<AppDbContext>(), off, Options.Create(new SmtpOptions()), NullLogger<SystemMailService>.Instance);
                run.Check("system mail is skipped cleanly when SMTP is off",
                    !await mail.SendTemplateAsync(SystemMailService.WelcomeTemplateKey, "a@example.test", "A", new Dictionary<string, string?> { ["user_name"] = "A" }));
            });
        }
        catch (Exception ex)
        {
            run.Error("round-4 phase threw", ex);
        }
    }

    private static async Task<int> Create(TestHarness harness, Func<IServiceProvider, ICampaignService> build, CreateCampaignRequest request)
    {
        var id = 0;
        await harness.InScopeAsync(async s => id = (await build(s).CreateAsync(request)).Id);
        return id;
    }

    private static async Task SetRequiredAsync(AppDbContext db, IMemoryCache cache, string[] keys)
    {
        var row = await db.AppSettings.FirstOrDefaultAsync(x => x.Key == ContactFieldCatalog.RequiredFieldsSettingKey);
        var json = System.Text.Json.JsonSerializer.Serialize(keys);
        if (row is null) db.AppSettings.Add(new AppSetting { Key = ContactFieldCatalog.RequiredFieldsSettingKey, Value = json });
        else row.Value = json;
        await db.SaveChangesAsync();
        cache.Remove(ContactLookupValidatorCache.RequiredFieldsCacheKey);
    }

    /// <summary>
    /// A connection whose stored password cannot be decrypted: the factory says so with the
    /// dedicated exception, the connection is flagged, and a campaign on it is put on hold with every
    /// recipient still Pending (nothing counted as failed or bounced).
    /// </summary>
    private static async Task HoldAsync(TestHarness harness, TestRun run)
    {
        int configId = 0, senderId = 0, templateId = 0, campaignId = 0;
        var contactIds = new List<int>();

        await harness.InScopeAsync(async s =>
        {
            var db = s.GetRequiredService<AppDbContext>();
            var connections = s.GetRequiredService<IEmailConnectionService>();
            var created = await connections.CreateAsync(new CreateEmailConnectionRequest
            {
                Name = $"{TestHarness.Prefix}-hold-{harness.Tag}", DisplayName = "Hold Test", EmailAddress = $"hold-{harness.Tag}@example.test"
            });
            configId = created.Id;
            await connections.SaveProviderAsync(configId, new SaveEmailProviderRequest
            {
                Provider = "Smtp", SmtpHost = "127.0.0.1", SmtpPort = 2525, SmtpSecurity = "None", SmtpUsername = "u", SmtpPassword = "p", MaxSendRatePerSecond = 50, IsActive = true
            });
            senderId = (await connections.GetSendersAsync(configId)).Single().Id;
            templateId = (await s.GetRequiredService<IEmailTemplateService>().CreateAsync(new SaveEmailTemplateRequest
            {
                Name = $"{TestHarness.Prefix} hold template {harness.Tag}", Subject = "Hold", BodyHtml = "<p>Hold</p>", IsEnabled = true
            })).Id;
            for (var i = 0; i < 2; i++)
            {
                var c = new Contact { Name = $"{TestHarness.Prefix} Hold {i} {harness.Tag}", Phone = $"+9191{Random.Shared.Next(10000000, 99999999)}", Email = $"hold{i}-{harness.Tag}@example.test", Type = "Lead", Status = "New", Source = "Import", IsActive = true };
                db.Contacts.Add(c);
                await db.SaveChangesAsync();
                contactIds.Add(c.Id);
            }
        });

        // A value in the stored format that no key can open, as if it had been written with another key.
        await harness.ExecAsync("""UPDATE "EmailConfigurations" SET "SmtpPasswordEncrypted" = @v WHERE "Id" = @id""",
            ("v", SecretCipher.Prefix + Convert.ToBase64String(new byte[40])), ("id", configId));

        Exception? resolveError = null;
        await harness.InScopeAsync(async s =>
        {
            try { await s.GetRequiredService<IEmailProviderFactory>().ResolveByConfigurationAsync(configId); }
            catch (Exception ex) { resolveError = ex; }
            var conn = await s.GetRequiredService<IEmailConnectionService>().GetByIdAsync(configId);
            run.Check("the connection page flags the unreadable password", !conn.CredentialsReadable && conn.Status == "Needs attention", conn.Status);
        });
        run.Check("resolving the connection reports it as unavailable", resolveError is EmailConnectionUnavailableException, resolveError?.GetType().Name);

        await harness.InScopeAsync(async s =>
        {
            campaignId = (await s.GetRequiredService<ICampaignService>().CreateAsync(new CreateCampaignRequest
            {
                Name = $"{TestHarness.Prefix} hold campaign {harness.Tag}", Channel = "Email", EmailTemplateId = templateId,
                SenderIdentityId = senderId, RelationType = "Lead", ScheduleType = "Immediate", ContactIds = contactIds
            })).Id;
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var expansion = ActivatorUtilities.CreateInstance<CampaignExpansionWorker>(harness.RootServices);
        var dispatch = ActivatorUtilities.CreateInstance<EmailDispatchWorker>(harness.RootServices);
        await expansion.StartAsync(cts.Token);
        await dispatch.StartAsync(cts.Token);
        string? status = null;
        try
        {
            while (!cts.IsCancellationRequested)
            {
                status = await harness.ScalarAsync("""SELECT "Status" FROM "Campaigns" WHERE "Id" = @id""", ("id", campaignId));
                if (status == nameof(CampaignStatus.Paused)) break;
                await Task.Delay(500);
            }
        }
        finally
        {
            await dispatch.StopAsync(CancellationToken.None);
            await expansion.StopAsync(CancellationToken.None);
        }

        var reason = await harness.ScalarAsync("""SELECT "PausedReason" FROM "Campaigns" WHERE "Id" = @id""", ("id", campaignId));
        var pending = await harness.CountAsync("""SELECT count(*) FROM "CampaignContacts" WHERE "CampaignId" = @id AND "Status" = 'Pending'""", ("id", campaignId));
        var failed = await harness.ScalarAsync("""SELECT "FailedCount" + "BouncedCount" FROM "Campaigns" WHERE "Id" = @id""", ("id", campaignId));
        run.Check("the campaign is put on hold", status == nameof(CampaignStatus.Paused), status);
        run.Check("with the reason shown", reason?.StartsWith("On hold:") == true && reason.Contains("decrypted"), reason);
        run.Check("the hold is audited", harness.Audit.Has("Campaign.Held", campaignId.ToString()));
        run.Check("every recipient is still Pending, ready for Resume", pending == contactIds.Count, $"{pending} of {contactIds.Count}");
        run.Check("nothing is counted as failed or bounced", failed == "0", failed);

        await harness.ExecAsync("""DELETE FROM "JobQueue" WHERE "PartitionKey" = @p""", ("p", campaignId.ToString()));
        await harness.InScopeAsync(async s => { try { await s.GetRequiredService<IEmailConnectionService>().DeleteAsync(configId); } catch { } });
    }

    private sealed class RecordingDispatcher(List<int> calls) : IWhatsAppCampaignDispatcher
    {
        public Task StartAsync(int campaignId, DateTime? notBefore, CancellationToken ct = default)
        {
            lock (calls) calls.Add(campaignId);
            return Task.CompletedTask;
        }
    }
}
