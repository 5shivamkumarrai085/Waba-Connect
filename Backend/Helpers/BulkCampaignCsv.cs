using System.Runtime.CompilerServices;
using System.Text;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Helpers;

/// <summary>
/// One contact row, already parsed and normalised.
/// </summary>
public sealed class CsvContactRow
{
    public int RowNumber { get; init; }
    public string Phone { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Country { get; init; } = string.Empty;
}

/// <summary>
/// Where each field the importer cares about sits in the header row.
/// </summary>
public sealed class CsvColumnMap
{
    public int Phone { get; init; } = -1;
    public int FirstName { get; init; } = -1;
    public int LastName { get; init; } = -1;
    public int Email { get; init; } = -1;
    public int Country { get; init; } = -1;
}

/// <summary>
/// The bulk-campaign CSV importer: one parser, one column map, one row validator.
///
/// <para>
/// The validate endpoint and the create service each used to carry their own copy of the header
/// lookup, the phone normalisation and the "is this row usable" rule, with a comment in each
/// warning that the two must stay byte-for-byte equivalent. That is not a property source code can
/// hold on its own — the file was read, split and judged twice, and the day the two disagreed the
/// symptom would be a campaign quietly created with fewer recipients than the preview promised.
/// Both now call this.
/// </para>
/// <para>
/// Reading is streamed a record at a time rather than loaded with <c>File.ReadAllLines</c>, which
/// held the entire file in memory as an array of strings, and split on raw newlines — so a
/// perfectly legal quoted address containing a line break silently became two broken rows. Nothing
/// here holds more than the current record, so file size is bounded by disk, not by RAM.
/// </para>
/// </summary>
public static class BulkCampaignCsv
{
    /// <summary>Header spellings accepted for each column, lowercased.</summary>
    private static readonly string[] PhoneNames = ["phone", "phoneno", "phone number", "phonenumber", "telephone", "mobile", "contact"];
    private static readonly string[] FirstNameNames = ["firstname", "first name", "name", "full name", "fullname"];
    private static readonly string[] LastNameNames = ["lastname", "last name", "surname"];
    private static readonly string[] EmailNames = ["email", "email address", "emailaddress", "e-mail"];
    private static readonly string[] CountryNames = ["country"];

    /// <summary>
    /// Reads a CSV as a sequence of records.
    ///
    /// <para>
    /// Handles the three things a naive line-splitter gets wrong: quoted fields containing commas,
    /// quoted fields containing newlines, and the doubled <c>""</c> that escapes a quote inside a
    /// quoted field. A UTF-8 BOM is stripped, and both CRLF and LF endings are accepted.
    /// </para>
    /// <para>
    /// <c>RowNumber</c> counts records, not physical lines, and is 1-based with the header as row
    /// 1 — so it matches what the user sees when they open the file in a spreadsheet, which is the
    /// only number that helps them find the row being complained about.
    /// </para>
    /// </summary>
    public static async IAsyncEnumerable<(int RowNumber, List<string> Fields)> ReadRecordsAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var recordHasContent = false;
        var rowNumber = 0;

        var buffer = new char[8192];
        int read;

        while ((read = await reader.ReadAsync(buffer, cancellationToken)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                var c = buffer[i];

                if (inQuotes)
                {
                    if (c != '"')
                    {
                        field.Append(c);
                        continue;
                    }

                    // A quote inside a quoted field: doubled means a literal quote, otherwise the
                    // field ends here. Peeking may need the next buffer, so refill if exhausted.
                    if (i + 1 >= read)
                    {
                        read = await reader.ReadAsync(buffer, cancellationToken);
                        i = -1;
                        if (read == 0)
                        {
                            inQuotes = false;
                            break;
                        }
                    }

                    if (buffer[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }

                    continue;
                }

                switch (c)
                {
                    case '"':
                        inQuotes = true;
                        recordHasContent = true;
                        break;

                    case ',':
                        fields.Add(field.ToString().Trim());
                        field.Clear();
                        recordHasContent = true;
                        break;

                    case '\r':
                        // Swallowed; the \n that follows ends the record.
                        break;

                    case '\n':
                        fields.Add(field.ToString().Trim());
                        field.Clear();

                        rowNumber++;
                        // A blank line is not a record. It still advances the row number, so the
                        // numbers reported back keep matching the spreadsheet.
                        if (recordHasContent || fields.Count > 1 || fields[0].Length > 0)
                        {
                            yield return (rowNumber, new List<string>(fields));
                        }

                        fields.Clear();
                        recordHasContent = false;
                        break;

                    default:
                        field.Append(c);
                        if (!char.IsWhiteSpace(c)) recordHasContent = true;
                        break;
                }
            }
        }

        // A final record with no trailing newline.
        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString().Trim());
            rowNumber++;
            if (recordHasContent || fields.Count > 1)
            {
                yield return (rowNumber, fields);
            }
        }
    }

    /// <summary>
    /// Locates the columns in a header row. Returns null when the file lacks a phone or a name
    /// column, which are the two the importer cannot proceed without.
    /// </summary>
    /// <param name="channel">
    /// Which column set the file has to satisfy. Phone and name are required on every channel —
    /// a Contact is identified by its phone number, so a row without one cannot become a contact
    /// whatever the campaign sends. An email campaign additionally requires the email column,
    /// because that is what the message is actually addressed to.
    /// </param>
    public static CsvColumnMap? MapColumns(
        IReadOnlyList<string> headerFields,
        MessageChannel channel = MessageChannel.WhatsApp)
    {
        var headers = headerFields
            .Select(h => h.Trim().Trim('"').ToLowerInvariant())
            .ToList();

        int Find(string[] names) => headers.FindIndex(h => names.Contains(h));

        var map = new CsvColumnMap
        {
            Phone = Find(PhoneNames),
            FirstName = Find(FirstNameNames),
            LastName = Find(LastNameNames),
            Email = Find(EmailNames),
            Country = Find(CountryNames)
        };

        if (map.Phone == -1 || map.FirstName == -1) return null;
        if (channel == MessageChannel.Email && map.Email == -1) return null;

        return map;
    }

    /// <summary>
    /// Judges one data row and, when it is usable, produces the contact it describes.
    ///
    /// This is the single definition of a valid row. The preview counts what this accepts and the
    /// campaign is built from what this accepts, so the two cannot report different totals.
    /// </summary>
    public static bool TryReadRow(
        IReadOnlyList<string> fields,
        int rowNumber,
        CsvColumnMap map,
        out CsvContactRow row,
        out List<CsvRowError> errors,
        MessageChannel channel = MessageChannel.WhatsApp)
    {
        row = null!;
        errors = [];

        if (fields.Count <= Math.Max(map.Phone, map.FirstName))
        {
            errors.Add(new CsvRowError
            {
                RowNumber = rowNumber,
                Column = null,
                Value = string.Join(",", fields),
                Reason = "Row has fewer columns than the header row."
            });
            return false;
        }

        string At(int index) => index >= 0 && index < fields.Count ? fields[index].Trim() : string.Empty;

        var phoneVal = At(map.Phone);
        // PhoneNumberHelper is the single normaliser for the whole app. The inline version that
        // used to live in the controller stripped only spaces, dashes and brackets, so a phone
        // column Excel had typed as a number ("919143000000.0") failed every row of an otherwise
        // good file — and said only that the format was invalid.
        var phoneValid = PhoneNumberHelper.TryNormalize(phoneVal, out var normalizedPhone, out var phoneFailure);
        if (!phoneValid)
        {
            errors.Add(new CsvRowError
            {
                RowNumber = rowNumber,
                Column = "phone",
                Value = phoneVal,
                Reason = phoneFailure!
            });
        }

        var firstName = At(map.FirstName);
        if (firstName.Length < 2)
        {
            errors.Add(new CsvRowError
            {
                RowNumber = rowNumber,
                Column = "firstname",
                Value = firstName,
                Reason = "Name must be at least 2 characters long."
            });
        }

        // An email campaign's recipient address is not optional the way it is for WhatsApp.
        // Judged here rather than at send time so the operator sees the bad rows in the preview,
        // next to the file they can still fix, instead of as silent non-delivery afterwards.
        var emailVal = At(map.Email);
        if (channel == MessageChannel.Email && !IsPlausibleEmail(emailVal))
        {
            errors.Add(new CsvRowError
            {
                RowNumber = rowNumber,
                Column = "email",
                Value = emailVal,
                Reason = emailVal.Length == 0
                    ? "An email campaign needs an email address in every row."
                    : "Email address is not valid."
            });
        }

        if (errors.Count > 0) return false;

        var lastName = At(map.LastName);
        var fullName = string.IsNullOrEmpty(lastName) ? firstName : $"{firstName} {lastName}".Trim();

        row = new CsvContactRow
        {
            RowNumber = rowNumber,
            Phone = normalizedPhone,
            FullName = fullName.Length < 2 ? "CSV User" : fullName,
            Email = emailVal,
            Country = At(map.Country)
        };

        return true;
    }

    /// <summary>
    /// How many row errors are returned to the caller.
    ///
    /// A 200,000-row file where every phone number is malformed would otherwise produce a response
    /// larger than the upload that caused it. The count is always exact; the list is a sample.
    /// </summary>
    public const int MaxReportedErrors = 500;

    /// <summary>
    /// A deliberately permissive address check: one @, something either side, a dot in the
    /// domain, no whitespace.
    ///
    /// <para>
    /// It is not RFC 5322 and does not try to be. The only authority on whether an address exists
    /// is the receiving mail server, so a stricter local rule cannot add correctness — it can only
    /// reject deliverable addresses. This catches the mistakes a spreadsheet actually produces
    /// (an empty cell, a name in the wrong column, a missing domain) and leaves the rest to the
    /// bounce handling that already exists.
    /// </para>
    /// </summary>
    private static bool IsPlausibleEmail(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.Any(char.IsWhiteSpace)) return false;

        var at = value.IndexOf('@');
        if (at <= 0 || at != value.LastIndexOf('@')) return false;

        var domain = value[(at + 1)..];
        var dot = domain.IndexOf('.');

        // The dot must have something on both sides: "a@b." and "a@.b" are both malformed.
        return dot > 0 && dot < domain.Length - 1;
    }
}
