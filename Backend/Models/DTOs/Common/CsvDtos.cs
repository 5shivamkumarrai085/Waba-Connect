namespace WhatsAppCampaignApi.Models.DTOs.Common;

/// <summary>
/// One rejected row from a CSV upload, identifying exactly which row and column failed and why.
///
/// Lives in Common because both importers report through it: the bulk-campaign csv-validate /
/// csv-create pair, and the contacts csv-import. Previously it sat under DTOs.Campaigns, which
/// is why the contacts importer never adopted it and shipped a single opaque
/// "wrong format csv file" for every possible failure instead.
/// </summary>
public class CsvRowError
{
    /// <summary>1-based and counting the header, so it matches the row number a spreadsheet shows.</summary>
    public int RowNumber { get; set; }
    public string? Column { get; set; }
    public string Value { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Outcome of a contacts CSV import.
///
/// The import is partial by design: valid rows are saved and invalid ones are reported, rather
/// than one bad row rejecting an otherwise good file. Duplicates are counted separately from
/// errors — a contact that already exists is not a mistake the user needs to fix.
/// </summary>
public class CsvImportResponse
{
    /// <summary>Non-blank data rows seen, excluding the header.</summary>
    public int TotalRecords { get; set; }
    public int ImportedCount { get; set; }
    public int SkippedDuplicates { get; set; }
    public int InvalidCount { get; set; }
    public List<CsvRowError> Errors { get; set; } = new();
}
