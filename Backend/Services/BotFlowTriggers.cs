using System.Text;
using System.Text.Json;
using WhatsAppCampaignApi.Models.Entities;

namespace WhatsAppCampaignApi.Services;

/// <summary>How a message matched a flow's Start Trigger.</summary>
public enum TriggerMatch { None, StartsWith, Contains, Exact }

/// <summary>A Start Trigger's keywords and match rule, parsed once from its node data.</summary>
public sealed record BotFlowTrigger(string TriggerType, IReadOnlyList<string> Keywords)
{
    public const string ExactType = "on exact match";
    public const string ContainsType = "contains";
    public const string StartsWithType = "starts with";
}

/// <summary>
/// Trigger keyword rules for the Bot Flow engine, in one place so routing is predictable and
/// testable: text is compared after <see cref="Normalize"/> (case, spacing and trailing
/// punctuation ignored), and keywords may be stored as a JSON array or a comma-separated string.
/// </summary>
public static class BotFlowTriggers
{
    /// <summary>
    /// Lower-cased, trimmed, inner whitespace collapsed, trailing punctuation removed:
    /// "  Send   Money! " and "send money" are the same request.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var ch in text.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }
            if (pendingSpace) builder.Append(' ');
            pendingSpace = false;
            builder.Append(char.ToLowerInvariant(ch));
        }

        var end = builder.Length;
        while (end > 0 && IsTrailingPunctuation(builder[end - 1])) end--;
        return builder.ToString(0, end).TrimEnd();
    }

    private static bool IsTrailingPunctuation(char ch) => ch is '.' or '!' or '?' or ',' or ';' or ':' or '…';

    /// <summary>Reads the Start Trigger's settings; null when the node has no usable keywords.</summary>
    public static BotFlowTrigger? Parse(FlowNode triggerNode)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(triggerNode.DataJson) ? "{}" : triggerNode.DataJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("keywords", out var raw)) return null;

            IEnumerable<string?> values = raw.ValueKind switch
            {
                JsonValueKind.Array => raw.EnumerateArray().Select(k => k.ValueKind == JsonValueKind.String ? k.GetString() : k.ToString()),
                JsonValueKind.String => (raw.GetString() ?? string.Empty).Split(','),
                _ => []
            };
            var keywords = values.Select(Normalize).Where(k => k.Length > 0).Distinct().ToList();
            if (keywords.Count == 0) return null;

            var type = root.TryGetProperty("triggerType", out var t) && t.ValueKind == JsonValueKind.String
                ? (t.GetString() ?? BotFlowTrigger.ExactType).Trim().ToLowerInvariant()
                : BotFlowTrigger.ExactType;
            return new BotFlowTrigger(type, keywords);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>How the message matches this trigger. An exact match is reported as Exact whatever the type.</summary>
    public static TriggerMatch Match(BotFlowTrigger trigger, string message)
    {
        var text = Normalize(message);
        if (text.Length == 0) return TriggerMatch.None;

        var best = TriggerMatch.None;
        foreach (var keyword in trigger.Keywords)
        {
            if (text == keyword) return TriggerMatch.Exact;
            if (trigger.TriggerType == BotFlowTrigger.ContainsType && text.Contains(keyword, StringComparison.Ordinal))
                best = Max(best, TriggerMatch.Contains);
            else if (trigger.TriggerType == BotFlowTrigger.StartsWithType && text.StartsWith(keyword, StringComparison.Ordinal))
                best = Max(best, TriggerMatch.StartsWith);
        }
        return best;
    }

    private static TriggerMatch Max(TriggerMatch a, TriggerMatch b) => (int)a >= (int)b ? a : b;

    /// <summary>
    /// Whether the message is one of the options a menu node is waiting for (a button's text or
    /// value, a list row's title, id or value). Used so a trigger keyword never steals a genuine
    /// answer to the menu on screen.
    /// </summary>
    public static bool IsMenuOption(FlowNode node, string message)
    {
        var text = Normalize(message);
        if (text.Length == 0) return false;

        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(node.DataJson) ? "{}" : node.DataJson);
            var options = new List<string>();
            Collect(doc.RootElement, options);
            return options.Any(o => Normalize(o) == text);
        }
        catch (JsonException)
        {
            return false;
        }

        // Executors read this data case-insensitively, so property names are matched the same way.
        static void Collect(JsonElement root, List<string> into)
        {
            foreach (var button in Items(root, "buttons"))
                into.AddRange(Strings(button, "text", "value"));

            foreach (var section in Items(root, "sections"))
                foreach (var row in Items(section, "items").Concat(Items(section, "rows")))
                    into.AddRange(Strings(row, "title", "id", "value"));
        }

        static IEnumerable<JsonElement> Items(JsonElement parent, string name) =>
            Property(parent, name) is { ValueKind: JsonValueKind.Array } array
                ? array.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object)
                : [];

        static IEnumerable<string> Strings(JsonElement item, params string[] names) =>
            names.Select(n => Property(item, n)).Where(v => v is { ValueKind: JsonValueKind.String }).Select(v => v!.Value.GetString()!);

        static JsonElement? Property(JsonElement element, string name)
        {
            if (element.ValueKind != JsonValueKind.Object) return null;
            foreach (var p in element.EnumerateObject())
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value;
            return null;
        }
    }
}
