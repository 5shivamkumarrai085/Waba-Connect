using Microsoft.Extensions.DependencyInjection;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.Setup;
using WhatsAppCampaignApi.Services.Email;

namespace EmailChannel.Tests;

/// <summary>
/// Phase 4 — email templates as the single source of truth, and the merge renderer.
///
/// <para>
/// The HTML-preservation tests here exist because of a real defect this suite caught: the
/// project's <c>SanitizeHtml</c> shared a sanitizer whose allowlist had been emptied, and
/// Ganss.Xss deletes a disallowed element together with its children — so every template body
/// saved through it came back empty. Campaigns were sending blank emails, and editing any seeded
/// template through the existing UI would have wiped it. These assertions are what stop that
/// coming back.
/// </para>
/// </summary>
public static class Phase4_TemplateTests
{
    public static async Task RunAsync(TestHarness harness, TestRun run)
    {
        run.BeginPhase("Phase 4 — email templates and merge fields");

        using var scope = harness.CreateScope();
        var templates = scope.ServiceProvider.GetRequiredService<IEmailTemplateService>();

        try
        {
            run.Section("Merge renderer — both placeholder syntaxes");

            var values = new Dictionary<string, string?>
            {
                ["name"] = "Shivam",
                ["company_name"] = "OmniConnect"
            };

            // Double braces are what the campaign UI and the rest of the product use.
            run.Check("double-brace placeholders are substituted",
                MergeFieldRenderer.Render("Hello {{name}}", values) == "Hello Shivam",
                MergeFieldRenderer.Render("Hello {{name}}", values));

            // Single braces are what the four seeded system templates were written with. Both
            // have to work, or one set of templates breaks.
            run.Check("single-brace placeholders are substituted",
                MergeFieldRenderer.Render("Hello {name}", values) == "Hello Shivam",
                MergeFieldRenderer.Render("Hello {name}", values));

            run.Check("both syntaxes in one template are substituted",
                MergeFieldRenderer.Render("{{name}} at {company_name}", values) == "Shivam at OmniConnect",
                MergeFieldRenderer.Render("{{name}} at {company_name}", values));

            run.Check("whitespace inside the braces is tolerated",
                MergeFieldRenderer.Render("Hello {{ name }}", values) == "Hello Shivam",
                MergeFieldRenderer.Render("Hello {{ name }}", values));

            run.Check("placeholder names are matched case-insensitively",
                MergeFieldRenderer.Render("Hello {{Name}}", values) == "Hello Shivam",
                MergeFieldRenderer.Render("Hello {{Name}}", values));

            // Left as written, not blanked. An operator previewing a template has to be able to
            // see which fields are unfilled — silently emptying them produces mail reading
            // "Dear ," with nothing to explain why.
            run.Check("an unknown placeholder is left visible rather than blanked",
                MergeFieldRenderer.Render("Hi {{unknown_field}}", values) == "Hi {{unknown_field}}",
                MergeFieldRenderer.Render("Hi {{unknown_field}}", values));

            run.Check("unresolved placeholders are detectable",
                MergeFieldRenderer.HasUnresolvedFields("Hi {{unknown_field}}"));
            run.Check("a fully substituted string reports nothing unresolved",
                !MergeFieldRenderer.HasUnresolvedFields("Hi Shivam"));

            // CSS in an HTML template contains braces. Treating those as merge fields would
            // corrupt the styling of every template with a <style> block.
            run.Check("CSS braces are not mistaken for placeholders",
                MergeFieldRenderer.Render("p { color: red; }", values) == "p { color: red; }",
                MergeFieldRenderer.Render("p { color: red; }", values));

            run.Section("Merge renderer — field extraction");

            var extracted = MergeFieldRenderer.Extract(
                "Hello {{name}}, your email is {{email}}. Regards {company_name}");

            run.Check("every referenced field is found",
                extracted.Contains("name") && extracted.Contains("email") && extracted.Contains("company_name"),
                string.Join(",", extracted));

            run.Check("fields are reported in the order they appear",
                extracted.Take(2).SequenceEqual(["name", "email"]), string.Join(",", extracted));

            run.Check("a repeated field is reported once",
                MergeFieldRenderer.Extract("{{name}} and {{name}} again").Count == 1,
                string.Join(",", MergeFieldRenderer.Extract("{{name}} and {{name}} again")));

            run.Section("HTML is preserved, scripts are not");

            const string body =
                "<h1>Welcome {{name}}</h1>" +
                "<p>Your email is <strong>{{email}}</strong>.</p>" +
                "<table><tr><td style=\"color:#ff0000\">Cell</td></tr></table>" +
                "<a href=\"https://example.test\">Link</a>" +
                "<script>alert('xss')</script>" +
                "<a href=\"javascript:alert(1)\">Bad link</a>" +
                "<img src=\"https://example.test/a.png\" onerror=\"alert(1)\">";

            var created = await templates.CreateAsync(new SaveEmailTemplateRequest
            {
                Name = $"{TestHarness.Prefix} template {harness.Tag}",
                Subject = "Hello {{name}} from {{company_name}}",
                BodyHtml = body,
                Language = "en",
                Description = "Integration test template",
                IsEnabled = true
            });

            // The regression guard. This is exactly what was silently failing before.
            run.Check("the body is not emptied by sanitization",
                !string.IsNullOrWhiteSpace(created.BodyHtml), $"length {created.BodyHtml.Length}");

            run.Check("headings survive", created.BodyHtml.Contains("<h1"), created.BodyHtml);
            run.Check("paragraphs survive", created.BodyHtml.Contains("<p"));
            run.Check("inline emphasis survives", created.BodyHtml.Contains("<strong"));

            // Tables are not optional for email — they are still the only layout mechanism that
            // works across Outlook and the major webmail clients.
            run.Check("tables survive", created.BodyHtml.Contains("<table"));
            run.Check("inline styles survive", created.BodyHtml.Contains("color"));
            run.Check("http links survive", created.BodyHtml.Contains("https://example.test"));
            run.Check("the merge placeholders survive",
                created.BodyHtml.Contains("{{name}}") && created.BodyHtml.Contains("{{email}}"));

            run.Check("script tags are removed",
                !created.BodyHtml.Contains("<script", StringComparison.OrdinalIgnoreCase), created.BodyHtml);
            run.Check("javascript: URLs are removed",
                !created.BodyHtml.Contains("javascript:", StringComparison.OrdinalIgnoreCase), created.BodyHtml);
            run.Check("inline event handlers are removed",
                !created.BodyHtml.Contains("onerror", StringComparison.OrdinalIgnoreCase), created.BodyHtml);

            run.Section("Variables are detected from the content, not a declared list");

            run.Check("the subject's and body's fields are both detected",
                created.DetectedVariables.Contains("name")
                && created.DetectedVariables.Contains("email")
                && created.DetectedVariables.Contains("company_name"),
                string.Join(",", created.DetectedVariables));

            run.Section("Keys");

            run.Check("a key is derived from the name when none is given",
                !string.IsNullOrWhiteSpace(created.Key), created.Key);
            run.Check("the derived key uses only lowercase, digits and underscores",
                created.Key.All(c => char.IsAsciiLetterOrDigit(c) && !char.IsUpper(c) || c == '_'),
                created.Key);

            var duplicateKeyRejected = false;
            try
            {
                await templates.CreateAsync(new SaveEmailTemplateRequest
                {
                    Name = "different name", Key = created.Key,
                    Subject = "s", BodyHtml = "<p>b</p>"
                });
            }
            catch (InvalidOperationException) { duplicateKeyRejected = true; }
            run.Check("a duplicate key is rejected", duplicateKeyRejected);

            // The key is what system notifications and existing campaigns resolve a template by,
            // so an edit must not be able to change it.
            var updated = await templates.UpdateAsync(created.Id, new SaveEmailTemplateRequest
            {
                Name = created.Name,
                Key = "a_completely_different_key",
                Subject = created.Subject,
                BodyHtml = created.BodyHtml,
                IsEnabled = true
            });
            run.Check("an update cannot change the key", updated.Key == created.Key,
                $"{created.Key} -> {updated.Key}");

            run.Section("Preview");

            var preview = await templates.PreviewAsync(created.Id);

            run.Check("the subject is rendered", !preview.Subject.Contains("{{"), preview.Subject);
            run.Check("the body is rendered", !preview.BodyHtml.Contains("{{"),
                preview.BodyHtml.Length > 120 ? preview.BodyHtml[..120] : preview.BodyHtml);
            run.Check("nothing is left unresolved once samples are applied",
                preview.UnresolvedVariables.Count == 0, string.Join(",", preview.UnresolvedVariables));

            var withValues = await templates.PreviewAsync(created.Id, new Dictionary<string, string?>
            {
                ["name"] = "Supplied Name"
            });
            run.Check("supplied values take precedence over samples",
                withValues.Subject.Contains("Supplied Name"), withValues.Subject);

            run.Section("Seeded system templates");

            var all = await templates.GetAllAsync();
            var system = all.Where(t => t.IsSystem).ToList();
            run.Check("the seeded templates are listed", system.Count >= 4, $"{system.Count}");

            // Their single-brace placeholders have to keep resolving, which is the whole reason
            // the renderer supports both syntaxes.
            var welcome = system.FirstOrDefault(t => t.Key.Equals("WelcomeEmail", StringComparison.OrdinalIgnoreCase));
            if (welcome is not null)
            {
                run.Check("a seeded template's single-brace fields are detected",
                    welcome.DetectedVariables.Count > 0, string.Join(",", welcome.DetectedVariables));

                var seededPreview = await templates.PreviewAsync(welcome.Id);
                run.Check("a seeded template still renders",
                    !string.IsNullOrWhiteSpace(seededPreview.BodyHtml)
                    && seededPreview.UnresolvedVariables.Count == 0,
                    string.Join(",", seededPreview.UnresolvedVariables));
            }
            else
            {
                run.Skip("seeded WelcomeEmail render", "the seeded template is not present in this database");
            }

            var systemDeleteRejected = false;
            if (system.Count > 0)
            {
                try
                {
                    await templates.DeleteAsync(system[0].Id);
                }
                catch (InvalidOperationException) { systemDeleteRejected = true; }
            }
            run.Check("a system template cannot be deleted", systemDeleteRejected);

            run.Section("Filtering for the campaign picker");

            await templates.ToggleAsync(created.Id);
            var enabledOnly = await templates.GetAllAsync(enabledOnly: true);
            run.Check("a disabled template is hidden from the campaign picker",
                enabledOnly.All(t => t.Id != created.Id));

            await templates.ToggleAsync(created.Id);
            var enabledAgain = await templates.GetAllAsync(enabledOnly: true);
            run.Check("re-enabling makes it selectable again",
                enabledAgain.Any(t => t.Id == created.Id));

            run.Section("Deleting a template an operator authored");

            await templates.DeleteAsync(created.Id);
            var deleted = false;
            try
            {
                await templates.GetByIdAsync(created.Id);
            }
            catch (KeyNotFoundException) { deleted = true; }
            run.Check("an unused, non-system template can be deleted", deleted);
        }
        catch (Exception ex)
        {
            run.Error("template phase threw", ex);
        }
    }
}
