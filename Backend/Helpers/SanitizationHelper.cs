using Ganss.Xss;
using System.Text.RegularExpressions;

namespace WhatsAppCampaignApi.Helpers;

public static class SanitizationHelper
{
    /// <summary>
    /// Strips every tag. Used by <see cref="SanitizeString"/> for ordinary text input, where any
    /// markup at all is unwanted.
    /// </summary>
    private static readonly HtmlSanitizer _textSanitizer;

    /// <summary>
    /// Keeps a safe subset of markup, for content that is meant to be HTML — email template
    /// bodies and received email.
    ///
    /// <para>
    /// A separate instance, and that separation is the point. Both sanitizers previously shared
    /// one configuration with <c>AllowedTags</c> cleared, which made <see cref="SanitizeHtml"/>
    /// destructive rather than protective: Ganss.Xss removes a disallowed element together with
    /// its children by default, so sanitizing <c>&lt;p&gt;Hello&lt;/p&gt;</c> against an empty
    /// allowlist returned an empty string, not "Hello". Anything saved through it lost its entire
    /// body.
    /// </para>
    /// </summary>
    private static readonly HtmlSanitizer _htmlSanitizer;

    static SanitizationHelper()
    {
        _textSanitizer = new HtmlSanitizer();
        _textSanitizer.AllowedTags.Clear(); // Allow NO HTML tags at all

        _htmlSanitizer = new HtmlSanitizer();

        // Start from nothing and add back deliberately, rather than subtracting from the
        // library's defaults — a future library version adding a tag to its defaults must not
        // silently widen what this accepts.
        _htmlSanitizer.AllowedTags.Clear();
        foreach (var tag in new[]
        {
            // Structure. Tables are not optional for email: they remain the only layout
            // mechanism that works across Outlook and the major webmail clients.
            "p", "div", "span", "br", "hr",
            "h1", "h2", "h3", "h4", "h5", "h6",
            "ul", "ol", "li", "blockquote", "pre",
            "table", "thead", "tbody", "tfoot", "tr", "td", "th", "caption", "colgroup", "col",
            // Inline formatting.
            "strong", "b", "em", "i", "u", "s", "small", "sub", "sup", "code",
            // Links and images.
            "a", "img",
            // Wrappers a WYSIWYG editor emits.
            "center", "font"
        })
        {
            _htmlSanitizer.AllowedTags.Add(tag);
        }

        _htmlSanitizer.AllowedAttributes.Clear();
        foreach (var attribute in new[]
        {
            "href", "src", "alt", "title", "width", "height",
            // Presentational attributes, because email clients ignore much of CSS and these are
            // what template editors actually produce.
            "align", "valign", "bgcolor", "border", "cellpadding", "cellspacing",
            "colspan", "rowspan", "color", "face", "size", "dir",
            "style", "class", "target", "role"
        })
        {
            _htmlSanitizer.AllowedAttributes.Add(attribute);
        }

        // Inline styles are allowed, but only these properties. An unrestricted style attribute
        // is an injection surface in its own right — `behavior`, `-moz-binding` and
        // `expression()` have all been script vectors.
        _htmlSanitizer.AllowedCssProperties.Clear();
        foreach (var property in new[]
        {
            "color", "background-color", "background", "font-family", "font-size", "font-weight",
            "font-style", "text-align", "text-decoration", "line-height", "letter-spacing",
            "margin", "margin-top", "margin-bottom", "margin-left", "margin-right",
            "padding", "padding-top", "padding-bottom", "padding-left", "padding-right",
            "border", "border-top", "border-bottom", "border-left", "border-right",
            "border-radius", "border-collapse", "border-spacing", "border-color", "border-width",
            "border-style", "width", "max-width", "min-width", "height", "max-height",
            "display", "vertical-align", "text-transform", "white-space"
        })
        {
            _htmlSanitizer.AllowedCssProperties.Add(property);
        }

        // Only these URL schemes survive. Notably excludes javascript: and data: — the first is
        // direct script execution, and the second can smuggle HTML into an <img> or a link.
        _htmlSanitizer.AllowedSchemes.Clear();
        _htmlSanitizer.AllowedSchemes.Add("http");
        _htmlSanitizer.AllowedSchemes.Add("https");
        _htmlSanitizer.AllowedSchemes.Add("mailto");
        _htmlSanitizer.AllowedSchemes.Add("tel");
        // cid: references an image embedded in the same message, which is how inline images work.
        _htmlSanitizer.AllowedSchemes.Add("cid");

        // Keeps the text of a tag that is not allowed, instead of deleting the element whole.
        // This is what stops one stray unsupported tag from silently removing a paragraph of an
        // operator's copy.
        _htmlSanitizer.KeepChildNodes = true;
    }

    public static string? SanitizeString(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        // 1. Strip all HTML
        var noHtml = _textSanitizer.Sanitize(input);

        // 2. Fallback regex in case something slips
        noHtml = StripHtmlTags(noHtml);

        // 3. Normalize whitespace & trim
        return NormalizeWhitespace(noHtml);
    }

    /// <summary>
    /// Sanitizes content that is meant to be HTML, keeping a safe subset of markup.
    ///
    /// <para>
    /// Use for email template bodies and received email — anything where removing the markup
    /// would destroy the content. For ordinary text fields use <see cref="SanitizeString"/>,
    /// which strips markup entirely.
    /// </para>
    /// </summary>
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
