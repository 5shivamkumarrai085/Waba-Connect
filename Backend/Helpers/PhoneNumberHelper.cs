using System.Text;
using System.Text.RegularExpressions;

namespace WhatsAppCampaignApi.Helpers;

public static class PhoneNumberHelper
{
    private static readonly Regex E164Regex = new(@"^\+[1-9]\d{6,14}$", RegexOptions.Compiled);

    /// <summary>
    /// A phone column typed as a number in Excel or Google Sheets round-trips as "919143000000.0".
    /// The digits are fully intact, so the suffix is pure formatting noise and safe to drop —
    /// but only when the fraction is all zeros. "919143000000.5" is genuinely malformed and is
    /// left alone so it still fails validation rather than being silently altered into a
    /// different, valid-looking number.
    /// </summary>
    private static readonly Regex TrailingZeroDecimalRegex =
        new(@"^(\+?\d+)\.0+$", RegexOptions.Compiled);

    /// <summary>
    /// "9.19143E+11". Excel truncates the significand, so the trailing digits are permanently
    /// gone — the true number is anywhere in a range thousands wide. This is detected only to
    /// produce an accurate error; it is never "repaired", because any repair would be a guess,
    /// and a wrong guess here sends a WhatsApp message to a stranger.
    /// </summary>
    private static readonly Regex ScientificNotationRegex =
        new(@"^[+-]?\d+(\.\d+)?[eE][+-]?\d+$", RegexOptions.Compiled);

    // Code points rather than literal characters: a raw no-break space or en dash is invisible
    // in a diff and silently destroyed by a re-encode of this file.
    private const char DashFirst = (char)0x2010;   // HYPHEN
    private const char DashLast = (char)0x2015;    // HORIZONTAL BAR (covers non-breaking hyphen,
                                                   // figure dash, en dash, em dash in between)
    private const char MinusSign = (char)0x2212;
    private const char RightSingleQuote = (char)0x2019;

    /// <summary>
    /// True for characters that carry no meaning in a phone number and can be dropped.
    ///
    /// <c>char.IsWhiteSpace</c> already covers the whole Unicode space category, so no-break
    /// space (U+00A0), narrow no-break space (U+202F) and figure space (U+2007) — the ones
    /// spreadsheets inject — need no special case. Deliberately excludes '.', because a decimal
    /// point is only safe to drop when its fraction is zero; see <see cref="TrailingZeroDecimalRegex"/>.
    /// </summary>
    private static bool IsSeparator(char c) =>
        char.IsWhiteSpace(c)
        || c == '-'
        || (c >= DashFirst && c <= DashLast)
        || c == MinusSign
        || c == '('
        || c == ')';

    public static bool IsValidE164(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return false;
        return E164Regex.IsMatch(phone);
    }

    public static string NormalizePhoneNumber(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return string.Empty;

        // Excel writes a leading apostrophe to force a cell to text; it is never part of the number.
        var trimmed = phone.Trim().TrimStart('\'', RightSingleQuote);

        var builder = new StringBuilder(trimmed.Length);
        foreach (var c in trimmed)
        {
            if (!IsSeparator(c)) builder.Append(c);
        }

        var normalized = builder.ToString();

        var decimalMatch = TrailingZeroDecimalRegex.Match(normalized);
        if (decimalMatch.Success)
        {
            normalized = decimalMatch.Groups[1].Value;
        }

        if (!normalized.StartsWith("+") && normalized.Length > 0 && normalized.All(char.IsDigit))
        {
            normalized = "+" + normalized;
        }

        return normalized;
    }

    /// <summary>
    /// Normalizes and validates in one step, reporting *why* a value was rejected.
    ///
    /// The CSV importers need this: a single generic "not a valid international format" for
    /// every failure is what made a bad phone column impossible to diagnose from the UI. The
    /// reason returned here is shown to the user against the offending row, so it names the
    /// actual problem and, where there is one, the fix.
    /// </summary>
    public static bool TryNormalize(string? raw, out string normalized, out string? failureReason)
    {
        normalized = string.Empty;
        failureReason = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            failureReason = "Phone number is missing.";
            return false;
        }

        var trimmed = raw.Trim();

        // Checked before normalization: stripping separators from "9.19143E+11" would produce a
        // plausible-looking but wrong number.
        if (ScientificNotationRegex.IsMatch(trimmed))
        {
            failureReason = "Excel has converted this to scientific notation and the original digits are lost. " +
                            "Format the phone column as Text and export the file again.";
            return false;
        }

        normalized = NormalizePhoneNumber(trimmed);

        if (IsValidE164(normalized))
        {
            return true;
        }

        failureReason = DescribeFailure(normalized);
        normalized = string.Empty;
        return false;
    }

    private static string DescribeFailure(string normalized)
    {
        if (normalized.Any(char.IsLetter))
            return "Phone number contains letters.";

        var digits = normalized.Count(char.IsDigit);

        if (digits == 0)
            return "Phone number is missing.";

        // NormalizePhoneNumber only prepends '+' when every remaining character is a digit, so a
        // value still lacking one is carrying something we deliberately refused to guess about
        // (a stray '.', '/', '#', and so on).
        if (!normalized.StartsWith("+"))
            return "Phone number contains unsupported characters.";

        if (digits < 7)
            return "Phone number is too short - include the country code (e.g. +15551234567).";

        if (digits > 15)
            return "Phone number is too long - an international number has at most 15 digits.";

        if (normalized.StartsWith("+0"))
            return "Phone number's country code cannot start with 0.";

        // Unchanged wording: this is the message the campaign importer has always shown, and it
        // remains correct for anything that reaches here.
        return "Phone number is not a valid international format (e.g. +15551234567).";
    }

    public static string FormatForWhatsApp(string e164Phone)
    {
        if (string.IsNullOrWhiteSpace(e164Phone)) return string.Empty;
        return e164Phone.StartsWith("+") ? e164Phone.Substring(1) : e164Phone;
    }
}
