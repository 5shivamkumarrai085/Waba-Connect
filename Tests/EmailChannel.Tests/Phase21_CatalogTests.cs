using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Campaigns;
using WhatsAppCampaignApi.Services.Catalogs;
using WhatsAppCampaignApi.Services.Segments;
using WhatsAppCampaignApi.Services.WhatsApp;

namespace EmailChannel.Tests;

/// <summary>
/// Phase 21 — the option catalogues the UI reads (GET api/reference/*) agree with the rules the
/// server enforces. Anything the screen offers must be accepted; anything past a limit refused.
/// </summary>
public static class Phase21_CatalogTests
{
    public static Task RunAsync(TestHarness harness, TestRun run)
    {
        run.BeginPhase("Phase 21 — option catalogues match the server's rules");

        run.Section("Segments");
        var rejected = new List<string>();
        foreach (var field in SegmentFieldCatalog.Fields)
        {
            foreach (var op in SegmentFieldCatalog.Operators[field.Kind])
            {
                var rule = new SegmentRule
                {
                    Field = field.Value,
                    Op = op.Value,
                    Value = field.Kind switch
                    {
                        "group" => "1",
                        "number" => ContactFieldCatalog.AgeYears.Default.ToString(),
                        "consent" => $"{ConsentCatalog.Channels[0].Value}:marketing:{ConsentCatalog.Statuses[0].Value}",
                        _ => "x"
                    },
                    Days = SegmentFieldCatalog.Days.Default
                };
                try { SegmentQueryBuilder.Validate(new SegmentRuleGroup { Rules = [rule] }); }
                catch (ArgumentException ex) { rejected.Add($"{field.Value}/{op.Value}: {ex.Message}"); }
            }
        }
        run.Check("every field and operator the editor offers is accepted", rejected.Count == 0, string.Join("; ", rejected));
        run.Check("every kind of field has operators", SegmentFieldCatalog.Fields.All(f => SegmentFieldCatalog.Operators.ContainsKey(f.Kind)));

        run.Section("Follow-ups and A/B tests");
        run.Check("every offered condition is one the server knows",
            CampaignFeatureCatalog.EmailFollowUpConditions.Concat(CampaignFeatureCatalog.WhatsAppFollowUpConditions)
                .All(c => FollowUpService.Conditions.Contains(c.Value)));
        run.Check("opens and clicks are offered for email only",
            !CampaignFeatureCatalog.WhatsAppFollowUpConditions.Any(c => c.Value is "NotOpened" or "NotClicked" or "Clicked"));
        run.Check("each channel's default A/B metric is in its list",
            CampaignFeatureCatalog.AbMetrics(MessageChannel.Email).Any(m => m.Value == AbTestService.DefaultMetric(MessageChannel.Email))
            && CampaignFeatureCatalog.AbMetrics(MessageChannel.WhatsApp).Any(m => m.Value == AbTestService.DefaultMetric(MessageChannel.WhatsApp)));
        run.Check("A/B defaults sit inside their ranges",
            InRange(CampaignFeatureCatalog.AbTestPercent) && InRange(CampaignFeatureCatalog.AbDecideAfterHours) && InRange(CampaignFeatureCatalog.FollowUpDelayHours));

        run.Section("Template buttons");
        foreach (var type in TemplateAuthoringCatalog.ButtonTypes)
        {
            TemplateButton Make() => new()
            {
                Type = type.Value,
                Text = "Label",
                Url = type.Value == "URL" ? "https://example.com/a" : null,
                PhoneNumber = type.Value == "PHONE_NUMBER" ? "+911234567890" : null,
                Example = type.Value == "COPY_CODE" ? "SAVE20" : null
            };
            Exception? atLimit = null, overLimit = null;
            try { TemplateComponents.ValidateButtons(Enumerable.Range(0, Math.Min(type.MaxCount, TemplateAuthoringCatalog.MaxButtons)).Select(_ => Make())); }
            catch (Exception ex) { atLimit = ex; }
            if (type.MaxCount < TemplateAuthoringCatalog.MaxButtons)
            {
                try { TemplateComponents.ValidateButtons(Enumerable.Range(0, type.MaxCount + 1).Select(_ => Make())); }
                catch (Exception ex) { overLimit = ex; }
            }
            run.Check($"{type.Label}: up to {type.MaxCount} accepted{(type.MaxCount < TemplateAuthoringCatalog.MaxButtons ? ", one more refused" : "")}",
                atLimit is null && (type.MaxCount >= TemplateAuthoringCatalog.MaxButtons || overLimit is ArgumentException), atLimit?.Message ?? overLimit?.Message);
        }
        var longLabel = new TemplateButton { Type = "QUICK_REPLY", Text = new string('x', TemplateAuthoringCatalog.MaxButtonLabelLength + 1) };
        Exception? labelError = null;
        try { TemplateComponents.ValidateButtons([longLabel]); } catch (Exception ex) { labelError = ex; }
        run.Check("a label one past the catalogue's limit is refused", labelError is ArgumentException);

        run.Section("Scheduled reports");
        run.Check("the lookback default sits inside its range", InRange(ReportScheduleCatalog.LookbackDays));
        run.Check("the default day of week is a real day", Enum.IsDefined(typeof(DayOfWeek), ReportScheduleCatalog.DefaultDayOfWeek));
        run.Check("the default time parses", TimeOnly.TryParseExact(ReportScheduleCatalog.DefaultTimeOfDay, "HH:mm", out _));

        return Task.CompletedTask;
    }

    private static bool InRange(NumberRange r) => r.Min <= r.Default && r.Default <= r.Max;
}
