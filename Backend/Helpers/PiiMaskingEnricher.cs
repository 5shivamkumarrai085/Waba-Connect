using Serilog.Core;
using Serilog.Events;

namespace WhatsAppCampaignApi.Helpers;

/// <summary>
/// Masks personal data in structured log properties before any sink sees it.
/// </summary>
/// <remarks>
/// Logs are copied, shipped and retained far more loosely than the database, so customer phone
/// numbers and email addresses must not land in them in the clear. Masking by property name keeps
/// every existing log statement unchanged: <c>{Phone}</c>, <c>{Email}</c>, <c>{Recipient}</c> and
/// similar are rewritten to a form that still lets an operator correlate entries (last digits,
/// first letter and domain) without exposing the value.
/// </remarks>
public sealed class PiiMaskingEnricher : ILogEventEnricher
{
    private static readonly string[] SensitiveFragments = ["phone", "email", "recipient", "sender", "mobile", "msisdn"];
    private static readonly HashSet<string> SensitiveNames = new(StringComparer.OrdinalIgnoreCase) { "To", "From", "Address", "Cc", "Bcc" };

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        List<LogEventProperty>? replacements = null;

        foreach (var (name, value) in logEvent.Properties)
        {
            if (value is not ScalarValue { Value: string text } || string.IsNullOrEmpty(text)) continue;
            if (!IsSensitive(name, text)) continue;

            (replacements ??= []).Add(new LogEventProperty(name, new ScalarValue(Mask(text))));
        }

        if (replacements is null) return;
        foreach (var property in replacements) logEvent.AddOrUpdateProperty(property);
    }

    private static bool IsSensitive(string name, string value)
    {
        if (SensitiveFragments.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase))) return true;

        // Generic names (Address, To, From…) are also used by the framework for non-personal
        // values — Kestrel logs "Now listening on: {address}" with a URL. Mask them only when the
        // value actually looks like an email address or a phone number.
        return SensitiveNames.Contains(name) && LooksPersonal(value);
    }

    private static bool LooksPersonal(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") return false;
        return value.Contains('@') || value.Count(char.IsDigit) >= 6;
    }

    public static string Mask(string value)
    {
        var at = value.IndexOf('@');
        if (at > 0)
        {
            // a***@example.com — the domain helps diagnose delivery problems, the local part is the person.
            return $"{value[0]}***{value[at..]}";
        }

        var digits = value.Count(char.IsDigit);
        if (digits >= 6)
        {
            // Last four digits only, as on a card slip.
            return $"***{new string(value.Where(char.IsDigit).TakeLast(4).ToArray())}";
        }

        return value.Length <= 2 ? "**" : $"{value[0]}***";
    }
}
