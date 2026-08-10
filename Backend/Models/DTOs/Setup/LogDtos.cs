using System;
using System.Collections.Generic;

namespace WhatsAppCampaignApi.Models.DTOs.Setup;

public class LogFileResponse
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime ModifiedAt { get; set; }
    /// <summary>The active file. Cannot be deleted — Serilog holds it open.</summary>
    public bool IsToday { get; set; }
}

public class LogEntryResponse
{
    public string Level { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Exception { get; set; }
    public string Environment { get; set; } = string.Empty;
    public Dictionary<string, string> Properties { get; set; } = new();
    /// <summary>Pretty-printed server-side so the viewer renders it as-is.</summary>
    public string RawJson { get; set; } = string.Empty;
}

public class LogEntriesResponse
{
    public List<LogEntryResponse> Items { get; set; } = new();
    public int TotalCount { get; set; }
    /// <summary>Per-level totals for the filter pills, returned alongside the page so the UI
    /// doesn't need a second round-trip.</summary>
    public Dictionary<string, int> LevelCounts { get; set; } = new();
    /// <summary>True when the file exceeded the scan cap and only part of it was read.</summary>
    public bool Truncated { get; set; }
}
