using System.Text;
using System.Text.Json;
using Serilog.Events;
using Serilog.Formatting.Compact.Reader;
using WhatsAppCampaignApi.Models.DTOs.Setup;

namespace WhatsAppCampaignApi.Services;

public interface ILogFileService
{
    List<LogFileResponse> GetFiles();
    Task<LogEntriesResponse> GetEntriesAsync(string fileName, string? level, string? search, int page, int pageSize);
    /// <returns>An error message, or null on success.</returns>
    Task<string?> DeleteFileAsync(string fileName);
    Task<(int Deleted, int Skipped)> ClearAllAsync();
}

/// <summary>
/// Reads the Serilog file sink written since startup.
///
/// <para>
/// The sink writes compact JSON (CLEF), one object per line, precisely so entries can be read
/// back as structured records — level, timestamp, properties, exception — instead of being
/// regex-scraped out of rendered text.
/// </para>
/// </summary>
public class LogFileService : ILogFileService
{
    private readonly ILogger<LogFileService> _logger;
    private readonly string _logDirectory;

    /// <summary>Matches the sink's naming, and doubles as the path-traversal guard.</summary>
    private static readonly System.Text.RegularExpressions.Regex FileNamePattern =
        new(@"^waba-\d{8}\.log$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Caps how much of a file is parsed in one request. A log left running for days can reach
    /// hundreds of megabytes, and the viewer only ever shows a page at a time.
    /// </summary>
    private const int MaxLinesScanned = 50_000;

    public LogFileService(ILogger<LogFileService> logger)
    {
        _logger = logger;
        _logDirectory = Path.Combine(AppContext.BaseDirectory, "Logs");
    }

    public List<LogFileResponse> GetFiles()
    {
        if (!Directory.Exists(_logDirectory)) return new List<LogFileResponse>();

        var today = TodayFileName();

        return Directory.GetFiles(_logDirectory, "waba-*.log")
            .Select(path => new FileInfo(path))
            .Where(info => FileNamePattern.IsMatch(info.Name))
            .OrderByDescending(info => info.Name)
            .Select(info => new LogFileResponse
            {
                Name = info.Name,
                DisplayName = info.Name,
                SizeBytes = info.Length,
                ModifiedAt = info.LastWriteTimeUtc,
                IsToday = info.Name == today
            })
            .ToList();
    }

    public async Task<LogEntriesResponse> GetEntriesAsync(
        string fileName, string? level, string? search, int page, int pageSize)
    {
        var response = new LogEntriesResponse();
        var path = ResolveSafePath(fileName);
        if (path is null || !File.Exists(path)) return response;

        var entries = new List<LogEntryResponse>();
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var scanned = 0;

        try
        {
            // FileShare.ReadWrite is required: Serilog holds the current file open for writing.
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var logReader = new LogEventReader(reader);

            while (logReader.TryRead(out var logEvent))
            {
                if (++scanned > MaxLinesScanned)
                {
                    response.Truncated = true;
                    break;
                }

                var entry = MapEntry(logEvent!);
                counts[entry.Level] = counts.GetValueOrDefault(entry.Level) + 1;

                if (!string.IsNullOrWhiteSpace(level) && !string.Equals(entry.Level, level, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrWhiteSpace(search) &&
                    entry.Message.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 &&
                    (entry.Exception ?? string.Empty).IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                entries.Add(entry);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read log file {File}.", fileName);
            return response;
        }

        // Newest first — the reason anyone opens a log.
        entries.Reverse();

        response.LevelCounts = counts;
        response.TotalCount = entries.Count;
        response.Items = entries.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return response;
    }

    public Task<string?> DeleteFileAsync(string fileName)
    {
        var path = ResolveSafePath(fileName);
        if (path is null) return Task.FromResult<string?>("That log file name is not valid.");
        if (!File.Exists(path)) return Task.FromResult<string?>("Log file not found.");

        // Serilog keeps a write handle on the current day's file. On Windows the delete throws;
        // refusing up front gives a comprehensible message rather than an IOException.
        if (Path.GetFileName(path) == TodayFileName())
            return Task.FromResult<string?>("The active log file cannot be deleted; it rolls over at midnight.");

        try
        {
            File.Delete(path);
            return Task.FromResult<string?>(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete log file {File}.", fileName);
            return Task.FromResult<string?>("The log file is in use and could not be deleted.");
        }
    }

    public Task<(int Deleted, int Skipped)> ClearAllAsync()
    {
        if (!Directory.Exists(_logDirectory)) return Task.FromResult((0, 0));

        var today = TodayFileName();
        var deleted = 0;
        var skipped = 0;

        foreach (var path in Directory.GetFiles(_logDirectory, "waba-*.log"))
        {
            var name = Path.GetFileName(path);
            if (!FileNamePattern.IsMatch(name)) continue;

            if (name == today) { skipped++; continue; }

            try { File.Delete(path); deleted++; }
            catch (Exception ex)
            {
                skipped++;
                _logger.LogWarning(ex, "Could not delete log file {File} during clear-all.", name);
            }
        }

        return Task.FromResult((deleted, skipped));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string TodayFileName() => $"waba-{DateTime.Now:yyyyMMdd}.log";

    /// <summary>
    /// Validates a requested file name and resolves it inside the log directory.
    ///
    /// Two independent checks: the name must match the sink's pattern, and the resolved
    /// absolute path must still sit inside the log directory. The second catches anything the
    /// first misses, so "../../appsettings.json" cannot escape.
    /// </summary>
    private string? ResolveSafePath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || !FileNamePattern.IsMatch(fileName)) return null;

        var candidate = Path.GetFullPath(Path.Combine(_logDirectory, fileName));
        var root = Path.GetFullPath(_logDirectory);

        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? candidate : null;
    }

    private static LogEntryResponse MapEntry(LogEvent logEvent)
    {
        var properties = logEvent.Properties.ToDictionary(
            p => p.Key,
            p => p.Value.ToString().Trim('"'));

        properties.TryGetValue("Environment", out var environment);

        var entry = new LogEntryResponse
        {
            // Compact JSON omits @l entirely for Information — it is the default level — so a
            // naive parser reports every info line as null. LogEventReader restores it.
            Level = logEvent.Level.ToString(),
            Timestamp = logEvent.Timestamp.UtcDateTime,
            Message = logEvent.RenderMessage(),
            Exception = logEvent.Exception?.ToString(),
            Environment = environment ?? "Unknown",
            Properties = properties
        };

        // Serialized server-side so the viewer can render it directly without guessing at
        // formatting.
        entry.RawJson = JsonSerializer.Serialize(new
        {
            level = entry.Level,
            timestamp = entry.Timestamp,
            message = entry.Message,
            exception = entry.Exception,
            properties = entry.Properties
        }, new JsonSerializerOptions { WriteIndented = true });

        return entry;
    }
}
