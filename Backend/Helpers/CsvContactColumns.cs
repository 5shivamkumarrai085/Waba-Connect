using System.Globalization;
using System.Net.Mail;
using System.Text.RegularExpressions;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Services.Catalogs;

namespace WhatsAppCampaignApi.Helpers;

/// <summary>
/// Reads the optional contact fields (email, date of birth, company, city, ...) from a contacts
/// CSV and checks them with the same rules as the contact form: which are required comes from
/// Settings › Contacts, lengths, kinds and the age range from <see cref="ContactFieldCatalog"/>.
/// A column is recognised by the field's key or label in any of the usual spellings
/// ("dateOfBirth", "date_of_birth", "date of birth"), so adding a field to the catalog makes it
/// importable without touching the importer.
/// </summary>
public static class CsvContactColumns
{
    /// <summary>
    /// The fields read here. Name, phone, type, status and source have dedicated columns, and
    /// "assigned to" is resolved from assigned_id against user accounts by the importer.
    /// </summary>
    public static readonly ContactFieldCatalog.Field[] Importable =
        ContactFieldCatalog.Configurable.Where(f => f.Key != "assignedTo").ToArray();

    /// <summary>ISO dates only: day/month order is ambiguous between locales, and a guess would silently swap them.</summary>
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "yyyy/MM/dd", "yyyy.MM.dd"];

    /// <summary>The column name the sample file and error messages use for a field (snake_case).</summary>
    public static string HeaderFor(ContactFieldCatalog.Field field) =>
        Regex.Replace(field.Key, "(?<=[a-z0-9])([A-Z])", "_$1").ToLowerInvariant();

    /// <summary>One column of the import layout, as the sample file and the import dialog show it.</summary>
    public sealed record Column(string Header, string Label, bool Required, string Example);

    /// <summary>
    /// The columns a contacts CSV should have right now: the fixed ones, then every field the
    /// administrator has made required. <paramref name="optional"/> names the other fields the
    /// importer also reads when present.
    /// </summary>
    public static IReadOnlyList<Column> Layout(IReadOnlySet<string> required, string exampleType, out IReadOnlyList<string> optional)
    {
        var columns = new List<Column>
        {
            new("status_id", "Status", true, "1"),
            new("source_id", "Source", true, "1"),
            new("assigned_id", "Assigned to", required.Contains("assignedTo"), "1"),
            new("firstname", "First name", true, "Asha"),
            new("lastname", "Last name", true, "Rao"),
            new("type", "Type", true, exampleType),
            new("phone", "Phone", true, "+15551234567")
        };
        columns.AddRange(Importable.Where(f => required.Contains(f.Key))
            .Select(f => new Column(HeaderFor(f), f.Label, true, ExampleFor(f))));
        optional = Importable.Where(f => !required.Contains(f.Key)).Select(HeaderFor).ToList();
        return columns;
    }

    private static string ExampleFor(ContactFieldCatalog.Field field) => field.Kind switch
    {
        "email" => "name@example.com",
        "url" => "example.com",
        "date" => DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-ContactFieldCatalog.AgeYears.Default).ToString(DateFormats[0], CultureInfo.InvariantCulture),
        _ => field.Label
    };

    /// <summary>Field key → column index, for every importable field the header row names.</summary>
    public static Dictionary<string, int> Map(IReadOnlyList<string> headers)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in Importable)
        {
            var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                field.Key,
                HeaderFor(field),
                field.Label,
                field.Label.Replace(' ', '_')
            };
            var index = -1;
            for (var i = 0; i < headers.Count; i++)
            {
                if (aliases.Contains(headers[i].Trim())) { index = i; break; }
            }
            if (index >= 0) map[field.Key] = index;
        }
        return map;
    }

    /// <summary>
    /// Validates the row's optional fields and copies them onto <paramref name="contact"/>.
    /// Returns the first problem (row number left for the caller to fill in), or null when the
    /// row is fine.
    /// </summary>
    public static CsvRowError? Apply(Contact contact, IReadOnlyList<string> row, IReadOnlyDictionary<string, int> columns, IReadOnlySet<string> required)
    {
        foreach (var field in Importable)
        {
            var value = columns.TryGetValue(field.Key, out var index) && index < row.Count ? row[index].Trim() : string.Empty;
            var header = HeaderFor(field);

            if (value.Length == 0)
            {
                if (required.Contains(field.Key)) return Error(header, value, $"{field.Label} is required.");
                continue;
            }

            if (field.MaxLength is int max && value.Length > max)
                return Error(header, value, $"{field.Label} cannot be longer than {max} characters.");

            switch (field.Kind)
            {
                case "email":
                    if (!MailAddress.TryCreate(value, out var address) || !string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase))
                        return Error(header, value, "Enter a valid email address, e.g. name@example.com.");
                    break;
                case "url":
                    if (!Uri.TryCreate(value.Contains("://") ? value : "https://" + value, UriKind.Absolute, out _))
                        return Error(header, value, "Enter a valid website address.");
                    break;
                case "date":
                    if (!DateOnly.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                        return Error(header, value, $"{field.Label} must be a date written as YYYY-MM-DD.");
                    var today = DateOnly.FromDateTime(DateTime.UtcNow);
                    if (date > today) return Error(header, value, $"{field.Label} cannot be in the future.");
                    var ages = ContactFieldCatalog.AgeYears;
                    var age = ContactFieldCatalog.AgeOn(date, today);
                    if (age < ages.Min || age > ages.Max) return Error(header, value, $"Age must be between {ages.Min} and {ages.Max} years.");
                    break;
            }

            if (!Assign(contact, field.Key, value))
                return Error(header, value, $"{field.Label} cannot be imported from a file.");
        }
        return null;
    }

    private static bool Assign(Contact contact, string key, string value)
    {
        switch (key)
        {
            case "email": contact.Email = value; return true;
            case "dateOfBirth": contact.DateOfBirth = DateOnly.ParseExact(value, DateFormats, CultureInfo.InvariantCulture); return true;
            case "company": contact.Company = value; return true;
            case "website": contact.Website = value; return true;
            case "city": contact.City = value; return true;
            case "state": contact.State = value; return true;
            case "country": contact.Country = value; return true;
            case "timeZone": contact.TimeZone = value; return true;
            case "zipCode": contact.ZipCode = value; return true;
            case "address": contact.Address = value; return true;
            case "description": contact.Description = value; return true;
            default: return false;
        }
    }

    private static CsvRowError Error(string column, string value, string reason) =>
        new() { Column = column, Value = value, Reason = reason };
}
