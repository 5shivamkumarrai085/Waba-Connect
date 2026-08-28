using System.Globalization;
using System.Text;

namespace WhatsAppCampaignApi.Helpers;

/// <summary>
/// Writes RFC 4180 CSV that Excel opens correctly and safely.
///
/// <para>
/// Replaces the per-service <c>StringBuilder</c> + private <c>CsvEscape</c> pattern, which had
/// three problems worth naming:
/// </para>
/// <list type="bullet">
/// <item><description>
/// It escaped quotes and commas but not a bare carriage return, so a value containing one broke
/// the row structure of the file.
/// </description></item>
/// <item><description>
/// It emitted no byte-order mark. Excel reads a BOM-less file as the machine's ANSI codepage, so
/// every non-ASCII character — the em-dash in the existing metrics export, any accented contact
/// name — arrived mangled.
/// </description></item>
/// <item><description>
/// It had no formula-injection guard. A contact named <c>=cmd|'/c calc'!A1</c> exported as a live
/// formula, and Excel offers to run it when the file is opened. That is a real attack against
/// whoever opens the export, not against this application, which is precisely what makes it easy
/// to miss.
/// </description></item>
/// </list>
/// </summary>
public sealed class CsvWriter
{
    private readonly StringBuilder _buffer = new();
    private int _columnCount;

    /// <summary>
    /// Characters Excel and Sheets treat as the start of a formula.
    ///
    /// <c>+</c> and <c>-</c> are included even though they are also how numbers are written; the
    /// numeric case is handled before this is consulted, so a legitimate "-5" is unaffected.
    /// </summary>
    private static readonly char[] FormulaTriggers = { '=', '+', '-', '@', '\t', '\r' };

    /// <summary>Writes the header row. Also fixes the column count every later row is checked against.</summary>
    public void WriteHeader(params string[] columns)
    {
        _columnCount = columns.Length;
        WriteRawRow(columns);
    }

    /// <summary>
    /// Writes one data row.
    ///
    /// Throws when the row's length disagrees with the header's. A ragged CSV is the kind of bug
    /// that survives review and surfaces as silently shifted columns in someone's spreadsheet.
    /// </summary>
    public void WriteRow(params object?[] values)
    {
        if (_columnCount > 0 && values.Length != _columnCount)
        {
            throw new InvalidOperationException(
                $"CSV row has {values.Length} value(s) but the header declared {_columnCount}.");
        }

        WriteRawRow(values.Select(Stringify).ToArray());
    }

    /// <summary>A blank separator line, for files that carry more than one table.</summary>
    public void WriteBlankLine() => _buffer.Append("\r\n");

    /// <summary>
    /// Starts a new section with its own header, resetting the column count. Used by exports that
    /// stack several tables into one file.
    /// </summary>
    public void WriteSection(string title, params string[] columns)
    {
        if (_buffer.Length > 0) WriteBlankLine();
        _columnCount = 0;
        WriteRow(title);
        WriteHeader(columns);
    }

    /// <summary>
    /// The finished file, prefixed with a UTF-8 BOM.
    ///
    /// The BOM is what makes Excel read the file as UTF-8. It is not optional for an export that
    /// can contain a customer's name.
    /// </summary>
    public byte[] ToBytes()
    {
        var bytes = Encoding.UTF8.GetBytes(_buffer.ToString());
        var bom = Encoding.UTF8.GetPreamble();

        var result = new byte[bom.Length + bytes.Length];
        Buffer.BlockCopy(bom, 0, result, 0, bom.Length);
        Buffer.BlockCopy(bytes, 0, result, bom.Length, bytes.Length);
        return result;
    }

    private void WriteRawRow(IReadOnlyList<string> cells)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            if (i > 0) _buffer.Append(',');
            _buffer.Append(Escape(cells[i]));
        }

        // CRLF, per RFC 4180. A bare LF is read correctly by most tools and by some Windows
        // spreadsheet importers as a single unbroken row.
        _buffer.Append("\r\n");
    }

    /// <summary>
    /// Renders a value for the file. Dates go out in ISO 8601 and numbers in the invariant culture,
    /// so an export opened in one locale means the same thing as one opened in another.
    /// </summary>
    private static string Stringify(object? value) => value switch
    {
        null => string.Empty,
        string s => s,
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        bool b => b ? "Yes" : "No",
        decimal d => d.ToString(CultureInfo.InvariantCulture),
        double d => d.ToString(CultureInfo.InvariantCulture),
        float f => f.ToString(CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var neutralised = Neutralise(value);

        var needsQuoting =
            neutralised.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0
            // Leading or trailing spaces are stripped by some readers unless the field is quoted,
            // which silently alters the data.
            || neutralised != neutralised.Trim();

        return needsQuoting
            ? $"\"{neutralised.Replace("\"", "\"\"")}\""
            : neutralised;
    }

    /// <summary>
    /// Defuses a value a spreadsheet would otherwise evaluate as a formula, by prefixing a single
    /// quote — the standard marker for "treat this as literal text".
    ///
    /// A value that genuinely parses as a number is left alone: "-5" and "+1.5" are data, and
    /// mangling every negative number to protect against a leading minus would be worse than the
    /// problem. The check is on the whole string, so "-5+cmd" is still neutralised.
    /// </summary>
    private static string Neutralise(string value)
    {
        if (value.Length == 0) return value;
        if (Array.IndexOf(FormulaTriggers, value[0]) < 0) return value;
        if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out _)) return value;

        return "'" + value;
    }
}
