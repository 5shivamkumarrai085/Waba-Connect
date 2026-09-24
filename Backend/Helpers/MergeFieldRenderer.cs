using System.Text.RegularExpressions;

namespace WhatsAppCampaignApi.Helpers;

/// <summary>Which placeholder syntax to operate on.</summary>
public enum MergeFieldSyntax
{
    /// <summary><c>{name}</c> — what the seeded system email templates use.</summary>
    SingleBrace,

    /// <summary><c>{{name}}</c> — what WhatsApp templates, bot flows and campaign variables use.</summary>
    DoubleBrace,

    /// <summary>
    /// Both, double-brace first. The default, because the two conventions genuinely coexist:
    /// the four seeded email templates were written with single braces, while every campaign
    /// template authored through the new UI uses double braces. Supporting one would break the
    /// other.
    /// </summary>
    Both
}

/// <summary>
/// Substitutes merge fields into a template, and reports which fields a template uses.
///
/// <para>
/// <see cref="Extract"/> is what makes the campaign wizard's variable chips dynamic. Without it
/// the UI would need a hardcoded list of supported variables, which is wrong the moment somebody
/// edits a template.
/// </para>
/// <para>
/// Scope note: this is used by the email path only. The same substitution is currently duplicated
/// in five places on the WhatsApp side, and consolidating those is a worthwhile follow-up — but
/// doing it here would mean editing the WhatsApp send path, the template preview and the bot
/// executors for no email benefit, which is exactly the kind of change this work is meant to
/// avoid.
/// </para>
/// </summary>
public static class MergeFieldRenderer
{
    /// <summary>
    /// Matches <c>{{ name }}</c>. Field names are restricted to word characters, so a brace pair
    /// wrapping anything else — a CSS rule in an HTML template, say — is left untouched rather
    /// than being mangled into a substitution attempt.
    /// </summary>
    private static readonly Regex DoubleBracePattern = new(
        @"\{\{\s*(?<name>[\w.-]+)\s*\}\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Matches <c>{ name }</c> but not <c>{{ name }}</c>. The lookarounds are load-bearing:
    /// without them, running the single-brace pass over already-substituted double-brace output
    /// would match the inner braces of any pair the first pass left behind.
    /// </summary>
    private static readonly Regex SingleBracePattern = new(
        @"(?<!\{)\{\s*(?<name>[\w.-]+)\s*\}(?!\})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Replaces every recognised placeholder with its value.
    ///
    /// <para>
    /// A placeholder with no supplied value is left exactly as written, rather than being
    /// replaced with an empty string. That is deliberate: an operator previewing a template needs
    /// to see which fields are unfilled, and silently blanking them produces mail reading
    /// "Dear ," with nothing to indicate what went wrong. The send path checks for leftovers
    /// before sending.
    /// </para>
    /// </summary>
    public static string Render(
        string? template,
        IReadOnlyDictionary<string, string?> values,
        MergeFieldSyntax syntax = MergeFieldSyntax.Both)
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;

        var result = template;

        if (syntax is MergeFieldSyntax.DoubleBrace or MergeFieldSyntax.Both)
        {
            result = DoubleBracePattern.Replace(result, match => Resolve(match, values));
        }

        if (syntax is MergeFieldSyntax.SingleBrace or MergeFieldSyntax.Both)
        {
            result = SingleBracePattern.Replace(result, match => Resolve(match, values));
        }

        return result;
    }

    /// <summary>
    /// The distinct field names a template references, in first-appearance order so the UI's
    /// variable chips read in the same order as the template body.
    /// </summary>
    public static IReadOnlyList<string> Extract(
        string? template,
        MergeFieldSyntax syntax = MergeFieldSyntax.Both)
    {
        if (string.IsNullOrEmpty(template)) return [];

        // Ordinal-ignore-case, matching Resolve: a template using both {{Name}} and {{name}}
        // has one field, not two.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();

        void Collect(Regex pattern)
        {
            foreach (var match in pattern.Matches(template).Cast<Match>())
            {
                var name = match.Groups["name"].Value;
                if (seen.Add(name)) ordered.Add(name);
            }
        }

        if (syntax is MergeFieldSyntax.DoubleBrace or MergeFieldSyntax.Both) Collect(DoubleBracePattern);
        if (syntax is MergeFieldSyntax.SingleBrace or MergeFieldSyntax.Both) Collect(SingleBracePattern);

        return ordered;
    }

    /// <summary>
    /// Whether any placeholder remains unsubstituted. The send path uses this to refuse mail that
    /// would arrive with a literal <c>{{first_name}}</c> in it.
    /// </summary>
    public static bool HasUnresolvedFields(string? rendered, MergeFieldSyntax syntax = MergeFieldSyntax.Both) =>
        Extract(rendered, syntax).Count > 0;

    private static string Resolve(Match match, IReadOnlyDictionary<string, string?> values)
    {
        var name = match.Groups["name"].Value;

        // Case-insensitive lookup regardless of the dictionary's own comparer, so a template
        // written with {{Name}} still resolves against a "name" key. Templates are authored by
        // hand and casing drift is routine.
        if (values.TryGetValue(name, out var value)) return value ?? string.Empty;

        foreach (var (key, candidate) in values)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) return candidate ?? string.Empty;
        }

        // Left as written — see the note on Render.
        return match.Value;
    }
}
