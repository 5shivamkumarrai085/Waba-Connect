using Ganss.Xss;
using System.Text.RegularExpressions;

namespace WhatsAppCampaignApi.Helpers;

public static class SanitizationHelper
{
    private static readonly HtmlSanitizer _htmlSanitizer;

    static SanitizationHelper()
    {
        _htmlSanitizer = new HtmlSanitizer();
        _htmlSanitizer.AllowedTags.Clear(); // Allow NO HTML tags at all
    }

    public static string? SanitizeString(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        // 1. Strip all HTML
        var noHtml = _htmlSanitizer.Sanitize(input);
        
        // 2. Fallback regex in case something slips
        noHtml = StripHtmlTags(noHtml);
        
        // 3. Normalize whitespace & trim
        return NormalizeWhitespace(noHtml);
    }

    public static string? SanitizeHtml(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        return _htmlSanitizer.Sanitize(input);
    }

    public static string? StripHtmlTags(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        return Regex.Replace(input, "<.*?>", string.Empty);
    }

    public static string? NormalizeWhitespace(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        return Regex.Replace(input.Trim(), @"\s+", " ");
    }
}
