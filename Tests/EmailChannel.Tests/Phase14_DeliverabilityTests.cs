using Microsoft.Extensions.DependencyInjection;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Email;

namespace EmailChannel.Tests;

/// <summary>
/// Phase 14 — deliverability pre-check: the content rules, and live DNS checks against a public
/// domain with known records (read-only lookups).
/// </summary>
public static class Phase14_DeliverabilityTests
{
    public static async Task RunAsync(TestHarness harness, TestRun run)
    {
        run.BeginPhase("Phase 14 — deliverability pre-check");

        await harness.InScopeAsync(async services =>
        {
            var checks = services.GetRequiredService<IDeliverabilityService>();

            IReadOnlyList<PrecheckItem> Lint(string subject, string html, bool transactional = false, params string[] vars) =>
                checks.LintContent(subject, html, transactional, vars);
            string? Level(IReadOnlyList<PrecheckItem> items, string key) => items.FirstOrDefault(i => i.Key == key)?.Level;

            run.Section("Content");
            var good = Lint("Your October statement", "<p>Hello {{first_name}}, your statement is ready.</p><p><a href=\"{{unsubscribe_url}}\">Unsubscribe</a></p>");
            run.Check("a clean message has no failures or warnings",
                good.All(i => i.Level == "pass"), string.Join("; ", good.Where(i => i.Level != "pass").Select(i => i.Title)));

            run.Check("an empty subject fails", Level(Lint("", "<p>Body</p>"), "subject") == "fail");
            run.Check("an empty body fails", Level(Lint("Subject", "<p>   </p>"), "body") == "fail");
            run.Check("a capitalised subject is flagged", Level(Lint("FREE MONEY FOR EVERYONE", "<p>x</p>"), "subject") == "warn");
            run.Check("spam phrases are flagged", Level(Lint("Hello", "<p>Act now, this is risk-free!</p>"), "phrases") == "warn");
            run.Check("link shorteners are flagged", Level(Lint("Hello", "<a href=\"https://bit.ly/abc\">x</a>"), "links") == "warn");
            run.Check("a marketing email without a visible opt-out is flagged", Level(Lint("Hello", "<p>Offer inside</p>"), "unsubscribe") == "warn");
            run.Check("a transactional email is not asked for one", Level(Lint("Your OTP", "<p>123456</p>", transactional: true), "unsubscribe") is null);
            run.Check("an unknown merge field is flagged", Level(Lint("Hello", "<p>{{loyalty_tier}}</p>"), "fields") == "warn");
            run.Check("unless the campaign supplies it", Level(Lint("Hello", "<p>{{loyalty_tier}}</p>", false, "loyalty_tier"), "fields") is null);
            var big = Lint("Hello", "<p>" + new string('x', DeliverabilityService.GmailClipBytes + 10) + "</p>");
            run.Check("a message Gmail would clip is flagged", Level(big, "size") == "warn");

            run.Section("DNS (live, read-only)");
            var gmail = await checks.CheckDomainAsync("gmail.com");
            if (gmail.Any(i => i.Key == "dns"))
            {
                run.Skip("DNS checks", "DNS is not reachable from this machine");
            }
            else
            {
                run.Check("gmail.com publishes SPF", Level(gmail, "spf") == "pass", gmail.First(i => i.Key == "spf").Detail);
                run.Check("gmail.com publishes a DMARC policy", Level(gmail, "dmarc") is "pass" or "warn", gmail.First(i => i.Key == "dmarc").Detail);
                run.Check("gmail.com has MX records", Level(gmail, "mx") == "pass");

                var missing = await checks.CheckDomainAsync($"no-such-domain-{harness.Tag}.example.test");
                run.Check("a domain without records gets warnings, never a hard failure",
                    missing.All(i => i.Level != "fail"), string.Join("; ", missing.Select(i => $"{i.Key}={i.Level}")));
            }
        });
    }
}
