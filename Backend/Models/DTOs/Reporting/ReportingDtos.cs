namespace WhatsAppCampaignApi.Models.DTOs.Reporting;

// Mirrors Frontend/src/types/reporting.ts exactly — property names are serialized
// to camelCase by the default System.Text.Json web options, matching the frontend types 1:1.

public class MetricCardDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? BadgeText { get; set; }
    public string? BadgeType { get; set; } // excellent | fast | needs-review | stale | low | high | available
    public string IconName { get; set; } = string.Empty; // lucide-react icon name
}

public class AccuracyRecordDto
{
    public string Id { get; set; } = string.Empty;
    public string Entity { get; set; } = string.Empty;
    public int ExpectedCount { get; set; }
    public int VerifiedCount { get; set; }
    public string Status { get; set; } = "verified"; // verified | unverified
}

public class FreshnessRecordDto
{
    public string Id { get; set; } = string.Empty;
    public string Entity { get; set; } = string.Empty;
    public string LatestRecord { get; set; } = string.Empty; // humanized, e.g. "4 days ago"
    public string FreshnessValue { get; set; } = string.Empty; // badge text, e.g. "Stale"
    public string FreshnessType { get; set; } = "fresh"; // fresh | warning | stale
}

public class ExportItemDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string IconName { get; set; } = string.Empty;
    public string ActionType { get; set; } = "download"; // download | external
}
