using System.Text.Json;
using System.Text.Json.Serialization;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Services.WhatsApp;

/// <summary>One button of a WhatsApp template, as Meta defines it.</summary>
public sealed class TemplateButton
{
    /// <summary>QUICK_REPLY, URL, PHONE_NUMBER or COPY_CODE.</summary>
    public string Type { get; set; } = "QUICK_REPLY";
    public string Text { get; set; } = string.Empty;

    /// <summary>URL buttons; may end in {{1}} for a per-recipient suffix.</summary>
    public string? Url { get; set; }

    [JsonPropertyName("phone_number")]
    public string? PhoneNumber { get; set; }

    /// <summary>COPY_CODE: an example code, required by Meta for review.</summary>
    public string? Example { get; set; }
}

/// <summary>
/// Template components beyond header and body: buttons and carousel cards — reading them from
/// Meta, submitting them for approval, and filling their parameters at send time.
/// </summary>
/// <remarks>
/// Send-time values come from the same variables dictionary as body parameters, by name:
/// <c>button_{n}</c> (URL suffix), <c>button_{n}_payload</c> (quick-reply payload),
/// <c>button_{n}_code</c> (copy-code coupon), <c>card_{c}_image</c> (carousel card header) and
/// <c>card_{c}_{k}</c> (carousel card body variable k). None of these are body parameters.
/// </remarks>
public static class TemplateComponents
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly Catalogs.TemplateAuthoringCatalog.ButtonType[] ButtonTypes = Catalogs.TemplateAuthoringCatalog.ButtonTypes;

    /// <summary>Variables that are not body parameters.</summary>
    public static bool IsNonBodyVariable(string key) =>
        key.Equals("file", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("button_", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("card_", StringComparison.OrdinalIgnoreCase);

    public static List<TemplateButton> ParseButtons(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<TemplateButton>>(json, Json) ?? []; }
        catch (JsonException) { return []; }
    }

    /// <summary>Checks buttons against Meta's rules before they are stored or submitted.</summary>
    public static List<TemplateButton> ValidateButtons(IEnumerable<TemplateButton>? buttons)
    {
        var list = (buttons ?? []).ToList();
        var maxLabel = Catalogs.TemplateAuthoringCatalog.MaxButtonLabelLength;
        if (list.Count > Catalogs.TemplateAuthoringCatalog.MaxButtons)
            throw new ArgumentException($"A template can have at most {Catalogs.TemplateAuthoringCatalog.MaxButtons} buttons.");

        foreach (var (b, i) in list.Select((b, i) => (b, i + 1)))
        {
            b.Type = b.Type?.Trim().ToUpperInvariant() ?? "";
            b.Text = b.Text?.Trim() ?? "";
            if (!ButtonTypes.Any(t => t.Value == b.Type))
                throw new ArgumentException($"Button {i}: type must be one of: {string.Join(", ", ButtonTypes.Select(t => t.Label.ToLowerInvariant()))}.");
            if (b.Type != "COPY_CODE" && (b.Text.Length == 0 || b.Text.Length > maxLabel))
                throw new ArgumentException($"Button {i}: the label must be 1–{maxLabel} characters.");
            if (b.Type == "URL" && (string.IsNullOrWhiteSpace(b.Url) || !Uri.TryCreate(b.Url.Replace("{{1}}", "x"), UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException($"Button {i}: a URL button needs an https:// address.");
            if (b.Type == "PHONE_NUMBER" && (string.IsNullOrWhiteSpace(b.PhoneNumber) || !b.PhoneNumber.TrimStart('+').All(char.IsDigit)))
                throw new ArgumentException($"Button {i}: a phone button needs a number in international format.");
            if (b.Type == "COPY_CODE" && string.IsNullOrWhiteSpace(b.Example))
                throw new ArgumentException($"Button {i}: a copy-code button needs an example code.");
        }

        foreach (var type in ButtonTypes)
        {
            if (list.Count(b => b.Type == type.Value) > type.MaxCount)
                throw new ArgumentException($"A template can have at most {type.MaxCount} \"{type.Label}\" button(s).");
        }
        return list;
    }

    /// <summary>From Meta's components array: the buttons, and whether it is a carousel.</summary>
    public static (List<TemplateButton> Buttons, bool IsCarousel) ReadFromMeta(JsonElement components)
    {
        var buttons = new List<TemplateButton>();
        var isCarousel = false;
        foreach (var component in components.EnumerateArray())
        {
            var type = component.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (type == "CAROUSEL") isCarousel = true;
            if (type != "BUTTONS" || !component.TryGetProperty("buttons", out var array)) continue;

            foreach (var b in array.EnumerateArray())
            {
                buttons.Add(new TemplateButton
                {
                    Type = b.TryGetProperty("type", out var bt) ? bt.GetString() ?? "" : "",
                    Text = b.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : "",
                    Url = b.TryGetProperty("url", out var url) ? url.GetString() : null,
                    PhoneNumber = b.TryGetProperty("phone_number", out var ph) ? ph.GetString() : null
                });
            }
        }
        return (buttons, isCarousel);
    }

    /// <summary>Button and carousel parameters for a send, from the named variables.</summary>
    public static List<object> BuildSendComponents(Dictionary<string, string> variables, string? buttonsJson)
    {
        var components = new List<object>();
        var buttons = ParseButtons(buttonsJson);

        for (var i = 0; i < buttons.Count; i++)
        {
            var b = buttons[i];
            var index = i.ToString();
            if (b.Type == "URL" && b.Url?.Contains("{{1}}") == true && variables.TryGetValue($"button_{i}", out var suffix) && !string.IsNullOrWhiteSpace(suffix))
            {
                components.Add(new { type = "button", sub_type = "url", index, parameters = new object[] { new { type = "text", text = suffix } } });
            }
            else if (b.Type == "QUICK_REPLY" && variables.TryGetValue($"button_{i}_payload", out var payload) && !string.IsNullOrWhiteSpace(payload))
            {
                components.Add(new { type = "button", sub_type = "quick_reply", index, parameters = new object[] { new { type = "payload", payload } } });
            }
            else if (b.Type == "COPY_CODE" && variables.TryGetValue($"button_{i}_code", out var code) && !string.IsNullOrWhiteSpace(code))
            {
                components.Add(new { type = "button", sub_type = "copy_code", index, parameters = new object[] { new { type = "coupon_code", coupon_code = code } } });
            }
        }

        // Carousel: one card per card_{c}_image, in card order.
        var cardIndexes = variables.Keys
            .Select(k => System.Text.RegularExpressions.Regex.Match(k, @"^card_(\d+)_image$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            .Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value)).Distinct().OrderBy(c => c).ToList();
        if (cardIndexes.Count > 0)
        {
            var cards = cardIndexes.Select(c =>
            {
                var cardComponents = new List<object>
                {
                    new { type = "header", parameters = new object[] { new { type = "image", image = new { link = variables[$"card_{c}_image"] } } } }
                };
                var bodyParams = variables
                    .Where(v => System.Text.RegularExpressions.Regex.IsMatch(v.Key, $@"^card_{c}_\d+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    .OrderBy(v => int.Parse(v.Key.Split('_')[2]))
                    .Select(v => (object)new { type = "text", text = v.Value })
                    .ToArray();
                if (bodyParams.Length > 0) cardComponents.Add(new { type = "body", parameters = bodyParams });
                return (object)new { card_index = c, components = cardComponents };
            }).ToArray();
            components.Add(new { type = "carousel", cards });
        }

        return components;
    }

    /// <summary>
    /// The body of POST /{waba}/message_templates for a template created here. Media headers need a
    /// sample uploaded to Meta, so those templates are created in WhatsApp Manager instead.
    /// </summary>
    public static object BuildSubmission(Template template)
    {
        if (template.HeaderType is not (HeaderType.None or HeaderType.Text))
            throw new InvalidOperationException("Templates with an image, video or document header need a sample uploaded to Meta; create those in WhatsApp Manager, then sync.");

        var components = new List<object>();
        if (template.HeaderType == HeaderType.Text && !string.IsNullOrWhiteSpace(template.HeaderContent))
            components.Add(new { type = "HEADER", format = "TEXT", text = template.HeaderContent });

        var samples = template.Variables.OrderBy(v => v.Position).Select(v => string.IsNullOrWhiteSpace(v.SampleValue) ? $"sample{v.Position}" : v.SampleValue!).ToArray();
        components.Add(samples.Length > 0
            ? new { type = "BODY", text = template.BodyText, example = new { body_text = new[] { samples } } }
            : (object)new { type = "BODY", text = template.BodyText });

        if (!string.IsNullOrWhiteSpace(template.FooterText))
            components.Add(new { type = "FOOTER", text = template.FooterText });

        var buttons = ParseButtons(template.ButtonsJson);
        if (buttons.Count > 0)
        {
            components.Add(new
            {
                type = "BUTTONS",
                buttons = buttons.Select(b => b.Type switch
                {
                    "URL" when b.Url!.Contains("{{1}}") => (object)new { type = "URL", text = b.Text, url = b.Url, example = new[] { b.Url.Replace("{{1}}", "example") } },
                    "URL" => new { type = "URL", text = b.Text, url = b.Url },
                    "PHONE_NUMBER" => new { type = "PHONE_NUMBER", text = b.Text, phone_number = b.PhoneNumber },
                    "COPY_CODE" => new { type = "COPY_CODE", example = b.Example },
                    _ => new { type = "QUICK_REPLY", text = b.Text }
                }).ToArray()
            });
        }

        return new
        {
            name = template.Name,
            language = template.Language,
            category = template.Category.ToString().ToUpperInvariant(),
            components
        };
    }
}
