using System.Text.RegularExpressions;

namespace WhatsAppCampaignApi.Helpers;

/// <summary>
/// Prepares request/response bodies for storage in the message activity log.
///
/// <para>
/// Two jobs, in this order: strip anything that could be replayed as a credential, then cap the
/// size. The order matters — capping first could split a token in half and leave the first
/// half stored in the clear.
/// </para>
/// <para>
/// This is deliberately conservative. It matches on key names rather than on value shapes,
/// because a Meta access token is just a long opaque string with no distinguishing format, and
/// a value-shape matcher would either miss it or redact half the message body.
/// </para>
/// </summary>
public static class PayloadRedactor
{
    /// <summary>
    /// 8 KB. Large enough for any real template payload including a components array, small
    /// enough that a runaway response can't bloat the log table.
    /// </summary>
    public const int MaxLength = 8 * 1024;

    private const string Placeholder = "\"***REDACTED***\"";

    /// <summary>
    /// Key names whose values are replaced wholesale. Covers what Meta actually sends and
    /// receives, plus the generic names a future caller is likely to reach for.
    /// </summary>
    private static readonly string[] SensitiveKeys =
    {
        "access_token", "accessToken",
        "auth_token", "authToken",
        "input_token", "inputToken",
        "client_secret", "clientSecret",
        "app_secret", "appSecret",
        "verify_token", "verifyToken",
        "password", "secret", "authorization", "api_key", "apiKey"
    };

    private static readonly Regex JsonKeyValue = new(
        // "key" : "value"  — value captured non-greedily, escaped quotes tolerated.
        "(\"(?:" + string.Join("|", SensitiveKeys.Select(Regex.Escape)) + ")\"\\s*:\\s*)\"(?:\\\\.|[^\"\\\\])*\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BearerHeader = new(
        @"Bearer\s+[A-Za-z0-9\-._~+/]+=*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Returns a stored-safe version of <paramref name="payload"/>, or null if there was
    /// nothing to store.
    /// </summary>
    public static string? Redact(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;

        var redacted = JsonKeyValue.Replace(payload, m => m.Groups[1].Value + Placeholder);
        redacted = BearerHeader.Replace(redacted, "Bearer ***REDACTED***");

        if (redacted.Length <= MaxLength) return redacted;

        // Truncation is announced rather than silent: a viewer looking at a payload that ends
        // mid-object should be able to tell it was cut rather than malformed.
        return redacted[..MaxLength] + $"\n\n… truncated at {MaxLength} characters ({redacted.Length} total).";
    }
}
