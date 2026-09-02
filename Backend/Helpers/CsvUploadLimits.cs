namespace WhatsAppCampaignApi.Helpers;

/// <summary>
/// How large a bulk-campaign CSV is allowed to be.
///
/// <para>
/// Kestrel caps a request body at 30 MB by default and the form reader caps a multipart section at
/// 128 MB, and neither of those numbers was chosen for this feature — they were simply whatever
/// ASP.NET ships. An upload above the smaller of them fails before any of this project's code runs,
/// so the user saw a generic network error rather than anything about their file.
/// </para>
/// <para>
/// One constant, referenced by the endpoint attributes and by the server configuration in
/// <c>Program.cs</c>, so the three limits cannot drift apart and leave a file that one layer
/// accepts and the next rejects.
/// </para>
/// <para>
/// 256 MB is far beyond any realistic contact list — a row of name, phone, email and country is
/// roughly 70 bytes, so this is on the order of three million recipients. The importer streams, so
/// the ceiling is about bounding abuse, not about what the parser can cope with.
/// </para>
/// </summary>
public static class CsvUploadLimits
{
    /// <summary>Maximum accepted upload size in bytes. Must be a constant: attributes need it.</summary>
    public const long MaxBytes = 256L * 1024 * 1024;

    /// <summary>The same limit, phrased for a message to the user.</summary>
    public const string MaxDescription = "256 MB";
}
