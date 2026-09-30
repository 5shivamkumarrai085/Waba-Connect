using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Webhook;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Segments;
using WhatsAppCampaignApi.Services.WhatsApp;

namespace EmailChannel.Tests;

/// <summary>
/// Phase 18 — WhatsApp interactive templates and ad attribution: button rules, reading buttons
/// from Meta, button and carousel parameters at send time, the submission body, and the
/// Click-to-WhatsApp referral reaching a segment.
/// </summary>
public static class Phase18_InteractiveTemplateTests
{
    public static async Task RunAsync(TestHarness harness, TestRun run)
    {
        run.BeginPhase("Phase 18 — interactive templates and ad attribution");

        run.Section("Button rules");
        Exception? Refused(params TemplateButton[] buttons)
        {
            try { TemplateComponents.ValidateButtons(buttons); return null; } catch (Exception ex) { return ex; }
        }
        run.Check("a quick reply and a URL button are accepted",
            Refused(new TemplateButton { Type = "quick_reply", Text = "Yes" }, new TemplateButton { Type = "URL", Text = "Track", Url = "https://example.com/o/{{1}}" }) is null);
        run.Check("an http:// URL button is refused", Refused(new TemplateButton { Type = "URL", Text = "Go", Url = "http://example.com" }) is ArgumentException);
        run.Check("a label over 25 characters is refused", Refused(new TemplateButton { Type = "QUICK_REPLY", Text = new string('x', 26) }) is ArgumentException);
        run.Check("a copy-code button needs an example", Refused(new TemplateButton { Type = "COPY_CODE" }) is ArgumentException);
        run.Check("two phone buttons are refused",
            Refused(new TemplateButton { Type = "PHONE_NUMBER", Text = "Call", PhoneNumber = "+911234567890" }, new TemplateButton { Type = "PHONE_NUMBER", Text = "Call 2", PhoneNumber = "+911234567891" }) is ArgumentException);
        run.Check("an unknown type is refused", Refused(new TemplateButton { Type = "FLOW", Text = "x" }) is ArgumentException);

        run.Section("Reading a synced template");
        using var meta = JsonDocument.Parse("""
            [{"type":"BODY","text":"Hi {{1}}"},
             {"type":"BUTTONS","buttons":[{"type":"URL","text":"Track","url":"https://example.com/o/{{1}}"},{"type":"QUICK_REPLY","text":"Stop"}]}]
            """);
        var (read, isCarousel) = TemplateComponents.ReadFromMeta(meta.RootElement);
        run.Check("buttons are read from Meta's components", read.Count == 2 && read[0].Type == "URL" && read[1].Text == "Stop");
        run.Check("a plain template is not a carousel", !isCarousel);
        using var carousel = JsonDocument.Parse("""[{"type":"BODY","text":"x"},{"type":"CAROUSEL","cards":[]}]""");
        run.Check("a carousel is recognised", TemplateComponents.ReadFromMeta(carousel.RootElement).IsCarousel);

        run.Section("Send-time parameters");
        var buttonsJson = JsonSerializer.Serialize(read, TemplateComponents.Json);
        var variables = new Dictionary<string, string>
        {
            ["1"] = "Asha", ["button_0"] = "ORD-42", ["button_1_payload"] = "stop-marketing",
            ["card_0_image"] = "https://example.com/a.jpg", ["card_0_1"] = "Blue", ["card_1_image"] = "https://example.com/b.jpg"
        };
        run.Check("button and card variables are not body parameters",
            variables.Keys.Where(k => !TemplateComponents.IsNonBodyVariable(k)).SequenceEqual(["1"]));
        var sent = JsonSerializer.Serialize(TemplateComponents.BuildSendComponents(variables, buttonsJson));
        run.Check("the URL suffix is sent on button 0", sent.Contains("\"sub_type\":\"url\",\"index\":\"0\"") && sent.Contains("ORD-42"), sent);
        run.Check("the quick-reply payload is sent on button 1", sent.Contains("\"sub_type\":\"quick_reply\",\"index\":\"1\"") && sent.Contains("stop-marketing"));
        run.Check("two carousel cards are sent, in order", sent.Contains("\"card_index\":0") && sent.Contains("\"card_index\":1") && sent.IndexOf("a.jpg") < sent.IndexOf("b.jpg"));
        run.Check("nothing is sent for a template with no buttons or cards",
            TemplateComponents.BuildSendComponents(new() { ["1"] = "x" }, null).Count == 0);

        run.Section("Submitting to Meta");
        var template = new Template
        {
            Name = "order_update", Language = "en", Category = TemplateCategory.Utility,
            HeaderType = HeaderType.Text, HeaderContent = "Your order", BodyText = "Hi {{1}}, order {{2}} shipped.",
            FooterText = "Reply STOP to opt out", ButtonsJson = buttonsJson,
            Variables = [new() { Position = 2, SampleValue = "A-1" }, new() { Position = 1, SampleValue = "Asha" }]
        };
        var body = JsonSerializer.Serialize(TemplateComponents.BuildSubmission(template));
        run.Check("the body carries examples in position order", body.Contains("\"body_text\":[[\"Asha\",\"A-1\"]]"), body);
        run.Check("the category is upper case", body.Contains("\"category\":\"UTILITY\""));
        run.Check("a dynamic URL button carries an example URL", body.Contains("https://example.com/o/example"));
        template.HeaderType = HeaderType.Image;
        Exception? media = null;
        try { TemplateComponents.BuildSubmission(template); } catch (Exception ex) { media = ex; }
        run.Check("a media-header template is refused with a reason", media is InvalidOperationException);

        run.Section("Click-to-WhatsApp referral");
        var payload = JsonSerializer.Deserialize<WhatsAppWebhookPayload>("""
            {"object":"whatsapp_business_account","entry":[{"id":"1","changes":[{"field":"messages","value":{
              "messaging_product":"whatsapp","messages":[{"from":"919000000000","id":"wamid.x","type":"text","text":{"body":"hi"},
              "referral":{"source_url":"https://fb.me/ad","source_id":"120000","source_type":"ad","headline":"Diwali sale","ctwa_clid":"clid-1"}}]}}]}]}
            """, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var referral = payload?.Entry?[0].Changes?[0].Value?.Messages?[0].Referral;
        run.Check("the referral is read from the webhook", referral is { SourceId: "120000", Headline: "Diwali sale", CtwaClid: "clid-1" });

        int contactId = 0;
        try
        {
            await harness.InScopeAsync(async services =>
            {
                var db = services.GetRequiredService<AppDbContext>();
                var contact = new Contact
                {
                    Name = $"{TestHarness.Prefix} Ad {harness.Tag}", Phone = $"+9192{Random.Shared.Next(10000000, 99999999)}",
                    Type = "Lead", Status = "New", Source = "Import", IsActive = true,
                    AdSourceId = $"ad-{harness.Tag}", AdHeadline = "Diwali sale", AdAttributedAt = DateTime.UtcNow
                };
                db.Contacts.Add(contact);
                await db.SaveChangesAsync();
                contactId = contact.Id;
            });

            var count = 0;
            await harness.InScopeAsync(async services =>
                count = (await services.GetRequiredService<ISegmentService>().PreviewAsync(new SegmentRuleGroup
                {
                    Rules = [new SegmentRule { Field = "adSourceId", Op = "eq", Value = $"ad-{harness.Tag}" }]
                })).Count);
            run.Check("a segment can select contacts who came from an ad", count == 1, count.ToString());
        }
        finally
        {
            if (contactId > 0) await harness.ExecAsync("""DELETE FROM "Contacts" WHERE "Id" = @id""", ("id", contactId));
        }
    }
}
