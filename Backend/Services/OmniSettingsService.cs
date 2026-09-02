using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Setup;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Services.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace WhatsAppCampaignApi.Services;

/// <inheritdoc />
public class OmniSettingsService : IOmniSettingsService
{
    private readonly AppDbContext _dbContext;
    private readonly IEncryptionService _encryption;
    private readonly IAuditService _auditService;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<OmniSettingsService> _logger;
    private readonly IMemoryCache _cache;

    /// <summary>Single cache slot holding every stored setting. See GetCachedValuesAsync.</summary>
    private const string CacheKey = "omni-settings:values";

    /// <summary>
    /// What a stored secret is replaced with in any log or audit entry.
    ///
    /// The audit trail records that a credential changed, never what it changed to. A before/after
    /// diff of an API key in a table that support staff can read would defeat encrypting it in the
    /// first place.
    /// </summary>
    private const string SecretMask = "********";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public OmniSettingsService(
        AppDbContext dbContext,
        IEncryptionService encryption,
        IAuditService auditService,
        ICurrentUserService currentUser,
        ILogger<OmniSettingsService> logger,
        IMemoryCache cache)
    {
        _dbContext = dbContext;
        _encryption = encryption;
        _auditService = auditService;
        _currentUser = currentUser;
        _logger = logger;
        _cache = cache;
    }

    // ── Read ─────────────────────────────────────────────────────────────────

    public async Task<OmniSettingsSchemaDto> GetSchemaAsync()
    {
        var stored = await _dbContext.AppSettings.AsNoTracking()
            .ToDictionaryAsync(s => s.Key, s => s.Value);

        // Every option list this request could need, fetched once. Resolving them per field would
        // mean the same three queries repeated for each select on the page.
        var options = await ResolveOptionsAsync();

        var sections = OmniSettingsCatalog.Sections.Select(section => new OmniSettingsSectionDto
        {
            Key = section.Key,
            Label = section.Label,
            Description = section.Description,
            Icon = section.Icon,
            Notes = section.Notes,
            Fields = section.Fields.Select(field => Describe(field, stored, options)).ToList()
        }).ToList();

        return new OmniSettingsSchemaDto { Sections = sections };
    }

    /// <summary>
    /// One field, with its resolved options and current value.
    ///
    /// A new DTO rather than the catalogue's own instance: the catalogue is a static shared
    /// structure, and writing this request's values onto it would leak one caller's settings into
    /// the next request's response.
    /// </summary>
    private static OmniSettingsFieldDto Describe(
        OmniSettingsFieldDto field,
        IReadOnlyDictionary<string, string?> stored,
        IReadOnlyDictionary<string, List<OmniSettingsOptionDto>> options)
    {
        stored.TryGetValue(field.Key, out var raw);

        var described = new OmniSettingsFieldDto
        {
            Key = field.Key,
            Label = field.Label,
            Type = field.Type,
            Helper = field.Helper,
            Placeholder = field.Placeholder,
            Unit = field.Unit,
            Required = field.Required,
            RequiredWhenKey = field.RequiredWhenKey,
            OptionSource = field.OptionSource,
            IsSecret = field.IsSecret,
            Min = field.Min,
            Max = field.Max,
            Options = field.OptionSource is not null && options.TryGetValue(field.OptionSource, out var list)
                ? list
                : new List<OmniSettingsOptionDto>()
        };

        if (field.IsSecret)
        {
            // Write-only: the client learns that a key exists, never what it is.
            described.HasValue = !string.IsNullOrEmpty(raw);
            described.Value = null;
            return described;
        }

        described.Value = ParseForClient(field, raw);
        described.HasValue = raw is not null;
        return described;
    }

    /// <summary>
    /// Turns the stored text into the shape the client expects for that field's type.
    ///
    /// Typed rather than handing back strings: a toggle bound to the string "false" is on, and a
    /// number input bound to "" behaves differently from one bound to null. Unparseable values fall
    /// back to the type's empty state instead of throwing — a settings page that will not open
    /// because one row holds a stale value is worse than one field showing its default.
    /// </summary>
    private static object? ParseForClient(OmniSettingsFieldDto field, string? raw) => field.Type switch
    {
        "toggle" => bool.TryParse(raw, out var flag) && flag,
        "number" => int.TryParse(raw, out var number) ? number : (int?)null,
        "tags" or "multiselect" => DeserializeList(raw),
        _ => raw
    };

    private static List<string> DeserializeList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new List<string>();

        try
        {
            return JsonSerializer.Deserialize<List<string>>(raw, JsonOptions) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// Builds every named option list.
    ///
    /// <para>
    /// Statuses, sources and agents come from the tables an operator actually edits, so a status
    /// added under Setup appears here without anyone touching this file. Only the two external
    /// vocabularies — Meta's webhook fields and OpenAI's chat models — come from the catalogue,
    /// because there is no table of ours that could be their source of truth.
    /// </para>
    /// </summary>
    private async Task<Dictionary<string, List<OmniSettingsOptionDto>>> ResolveOptionsAsync()
    {
        var statuses = await _dbContext.ContactStatuses.AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Name)
            .Select(s => new OmniSettingsOptionDto { Value = s.Value, Label = s.Name })
            .ToListAsync();

        var sources = await _dbContext.ContactSources.AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Name)
            .Select(s => new OmniSettingsOptionDto { Value = s.Value, Label = s.Name })
            .ToListAsync();

        // Deleted users are excluded but inactive ones are not: an account can be suspended and
        // reinstated, and dropping it from the list would silently clear whoever leads are
        // assigned to. The id is the value so a rename never breaks the stored setting.
        var users = await _dbContext.AppUsers.AsNoTracking()
            .Where(u => !u.IsDeleted)
            .OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
            .Select(u => new OmniSettingsOptionDto
            {
                Value = u.Id.ToString(),
                Label = u.LastName != null && u.LastName != ""
                    ? u.FirstName + " " + u.LastName
                    : u.FirstName
            })
            .ToListAsync();

        return new Dictionary<string, List<OmniSettingsOptionDto>>
        {
            [OmniSettingsCatalog.LeadStatuses] = statuses,
            [OmniSettingsCatalog.LeadSources] = sources,
            [OmniSettingsCatalog.Users] = users,
            [OmniSettingsCatalog.AiModels] = OmniSettingsCatalog.AiModelCatalogue
                .Select(m => new OmniSettingsOptionDto { Value = m.Value, Label = m.Label }).ToList(),
            [OmniSettingsCatalog.WebhookEvents] = OmniSettingsCatalog.WebhookFieldCatalogue
                .Select(f => new OmniSettingsOptionDto { Value = f.Value, Label = f.Label }).ToList(),
            [OmniSettingsCatalog.HttpMethods] = OmniSettingsCatalog.HttpMethodCatalogue
                .Select(m => new OmniSettingsOptionDto { Value = m.Value, Label = m.Label }).ToList()
        };
    }

    // ── Write ────────────────────────────────────────────────────────────────

    public async Task<OmniSettingsSchemaDto> SaveSectionAsync(string sectionKey, SaveOmniSettingsRequest request)
    {
        var section = OmniSettingsCatalog.FindSection(sectionKey)
            ?? throw new KeyNotFoundException($"There is no settings section called \"{sectionKey}\".");

        var incoming = request?.Values ?? new Dictionary<string, object?>();
        var options = await ResolveOptionsAsync();

        // The effective state of the whole section after this save — needed before writing, because
        // "required when the toggle is on" has to be judged against the toggle's *new* value, not
        // the one still in the database.
        var stored = await _dbContext.AppSettings
            .Where(s => section.Fields.Select(f => f.Key).Contains(s.Key))
            .ToListAsync();

        var effective = section.Fields.ToDictionary(
            f => f.Key,
            f => incoming.TryGetValue(f.Key, out var supplied)
                ? Stringify(f, supplied)
                : stored.FirstOrDefault(s => s.Key == f.Key)?.Value);

        // Secrets that already have a value on file. Required-ness is judged against this, not
        // against the payload: the field posts blank whenever it is left alone, so a stored key
        // has to count as satisfying "required" or the section becomes unsavable without
        // re-typing the credential.
        var secretsOnFile = stored
            .Where(row => !string.IsNullOrEmpty(row.Value))
            .Select(row => row.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Validate(section, effective, options, secretsOnFile);

        var now = DateTime.UtcNow;
        var changed = new List<string>();

        foreach (var field in section.Fields)
        {
            // Only the keys this section owns. A payload carrying another section's key is ignored
            // rather than written — one page must not be able to rewrite another page's settings.
            if (!incoming.TryGetValue(field.Key, out var supplied)) continue;

            var value = Stringify(field, supplied);

            if (field.IsSecret)
            {
                // Blank means "leave the stored key alone". Without this, opening the page and
                // saving an unrelated toggle would wipe the credential, because the field renders
                // empty by design and would post empty.
                if (string.IsNullOrWhiteSpace(value)) continue;
                value = _encryption.Encrypt(value);
            }

            var row = stored.FirstOrDefault(s => s.Key == field.Key);

            if (row is null)
            {
                _dbContext.AppSettings.Add(new AppSetting
                {
                    Key = field.Key,
                    Value = value,
                    UpdatedByUserId = _currentUser.UserId,
                    UpdatedByName = _currentUser.UserName,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                changed.Add(field.Label);
                continue;
            }

            if (row.Value == value) continue;

            row.Value = value;
            row.UpdatedByUserId = _currentUser.UserId;
            row.UpdatedByName = _currentUser.UserName;
            row.UpdatedAt = now;
            changed.Add(field.Label);
        }

        await _dbContext.SaveChangesAsync();

        // Before the audit write, not after: the settings are live the instant the rows are
        // committed, and a reader that arrives in between must not be served the old values.
        InvalidateCache();

        await _auditService.LogAsync(
            "Settings.Updated", "Settings",
            changed.Count == 0
                ? $"Saved \"{section.Label}\" settings; nothing changed."
                : $"Updated \"{section.Label}\" settings: {string.Join(", ", changed)}.",
            "AppSetting", section.Key);

        return await GetSchemaAsync();
    }

    /// <summary>
    /// Normalises whatever JSON the client sent into the text this field stores.
    ///
    /// The client posts real JSON types — a boolean for a toggle, an array for tags — and the
    /// storage column is text, so the conversion happens once, here, rather than being guessed at
    /// on both sides.
    /// </summary>
    private static string? Stringify(OmniSettingsFieldDto field, object? supplied)
    {
        if (supplied is null) return null;

        var element = supplied is JsonElement json ? json : JsonSerializer.SerializeToElement(supplied, JsonOptions);

        switch (field.Type)
        {
            case "toggle":
                return element.ValueKind switch
                {
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    // A toggle that arrives as a string still has an unambiguous meaning; anything
                    // else is treated as off rather than rejected, so one malformed field cannot
                    // block a whole section from saving.
                    JsonValueKind.String => bool.TryParse(element.GetString(), out var parsed) && parsed ? "true" : "false",
                    _ => "false"
                };

            case "number":
                if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var number))
                    return number.ToString();
                if (element.ValueKind == JsonValueKind.String
                    && int.TryParse(element.GetString(), out var parsedNumber))
                    return parsedNumber.ToString();
                return null;

            case "tags":
            case "multiselect":
            {
                if (element.ValueKind != JsonValueKind.Array) return JsonSerializer.Serialize(new List<string>(), JsonOptions);

                var items = element.EnumerateArray()
                    .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Select(item => item!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return JsonSerializer.Serialize(items, JsonOptions);
            }

            default:
            {
                var text = element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
                return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            }
        }
    }

    /// <summary>
    /// Checks the section against its own declarations before anything is written.
    ///
    /// <para>
    /// Server-side because the schema the client renders from is advisory — it is a convenience so
    /// the form can show errors early, not a control. Every rule here is enforced against the state
    /// the section will be in after the save, not the one it is in now.
    /// </para>
    /// </summary>
    private static void Validate(
        OmniSettingsSectionDto section,
        IReadOnlyDictionary<string, string?> effective,
        IReadOnlyDictionary<string, List<OmniSettingsOptionDto>> options,
        IReadOnlySet<string> secretsOnFile)
    {
        foreach (var field in section.Fields)
        {
            effective.TryGetValue(field.Key, out var value);

            var isRequired = field.Required;
            if (!isRequired && field.RequiredWhenKey is not null)
            {
                effective.TryGetValue(field.RequiredWhenKey, out var gate);
                isRequired = bool.TryParse(gate, out var on) && on;
            }

            // A secret already on file satisfies "required": the field posts blank when unchanged,
            // and demanding it again on every save would mean re-entering the key to change the
            // model beside it. Keyed on what is stored rather than on what was posted, because
            // "left alone" arrives as an empty string, not as an absent key.
            var secretAlreadyStored = field.IsSecret && secretsOnFile.Contains(field.Key);

            if (isRequired && !secretAlreadyStored && IsEmpty(field, value))
            {
                throw new InvalidOperationException($"\"{field.Label}\" is required.");
            }

            if (IsEmpty(field, value)) continue;

            // The webhook destination is the one field whose value this server will later make an
            // outbound request to, so what it may contain is checked when it is set rather than
            // discovered at three in the morning when an event fails to forward.
            if (string.Equals(field.Key, "webhook.resendUrl", StringComparison.OrdinalIgnoreCase))
            {
                if (!Uri.TryCreate(value!.Trim(), UriKind.Absolute, out var destination)
                    || (destination.Scheme != Uri.UriSchemeHttp && destination.Scheme != Uri.UriSchemeHttps))
                {
                    throw new InvalidOperationException(
                        $"\"{field.Label}\" must be a full http:// or https:// URL.");
                }
            }

            if (field.Type == "number" && int.TryParse(value, out var number))
            {
                if (field.Min.HasValue && number < field.Min.Value)
                    throw new InvalidOperationException($"\"{field.Label}\" must be at least {field.Min.Value}.");

                if (field.Max.HasValue && number > field.Max.Value)
                    throw new InvalidOperationException($"\"{field.Label}\" must be {field.Max.Value} or less.");
            }

            // A select may only hold something its own option list offers. Without this, a stale
            // form could store a status that was deleted, and the feature reading that setting
            // would fail later, far from the change that caused it.
            if (field.Type == "select" && field.OptionSource is not null
                && options.TryGetValue(field.OptionSource, out var allowed) && allowed.Count > 0
                && !allowed.Any(o => string.Equals(o.Value, value, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"\"{value}\" is not a valid choice for \"{field.Label}\".");
            }

            if (field.Type == "multiselect" && field.OptionSource is not null
                && options.TryGetValue(field.OptionSource, out var permitted) && permitted.Count > 0)
            {
                var chosen = DeserializeList(value);
                var unknown = chosen.FirstOrDefault(c =>
                    !permitted.Any(o => string.Equals(o.Value, c, StringComparison.OrdinalIgnoreCase)));

                if (unknown is not null)
                    throw new InvalidOperationException($"\"{unknown}\" is not a valid choice for \"{field.Label}\".");
            }

            if (field.Key.EndsWith("Url", StringComparison.OrdinalIgnoreCase)
                && !Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                throw new InvalidOperationException($"\"{field.Label}\" must be a full URL, including https://.");
            }
            else if (field.Key.EndsWith("Url", StringComparison.OrdinalIgnoreCase)
                     && Uri.TryCreate(value, UriKind.Absolute, out var parsedUri)
                     && parsedUri.Scheme != Uri.UriSchemeHttps && parsedUri.Scheme != Uri.UriSchemeHttp)
            {
                throw new InvalidOperationException($"\"{field.Label}\" must be an http or https URL.");
            }
        }
    }

    private static bool IsEmpty(OmniSettingsFieldDto field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;

        // An empty list is stored as "[]", which is not whitespace but is still nothing chosen.
        return field.Type is "tags" or "multiselect" && DeserializeList(value).Count == 0;
    }

    // ── Consumption ──────────────────────────────────────────────────────────

    /// <summary>
    /// Every stored setting, as one cached dictionary.
    ///
    /// <para>
    /// The consumers of these settings sit on hot paths — every inbound WhatsApp message reads the
    /// auto-lead and stop-bot settings, every chat query reads the agent restriction — and each
    /// individual read was a separate round trip to Postgres. The whole table is at most a couple
    /// of dozen short rows, so it is loaded once and held until something writes to it.
    /// </para>
    /// <para>
    /// Values are cached exactly as stored: secrets stay encrypted in the cache and are decrypted
    /// per call, so a memory dump never contains a plaintext API key.
    /// </para>
    /// </summary>
    private async Task<Dictionary<string, string?>> GetCachedValuesAsync()
    {
        if (_cache.TryGetValue(CacheKey, out Dictionary<string, string?>? cached) && cached is not null)
        {
            return cached;
        }

        var rows = await _dbContext.AppSettings.AsNoTracking()
            .Select(s => new { s.Key, s.Value })
            .ToListAsync();

        var values = rows.ToDictionary(r => r.Key, r => r.Value, StringComparer.OrdinalIgnoreCase);

        // No expiry: the cache is dropped when a section is saved, which is the only thing that can
        // make it stale. A timeout would only add a window where a saved setting is ignored.
        _cache.Set(CacheKey, values);

        return values;
    }

    /// <inheritdoc />
    public void InvalidateCache() => _cache.Remove(CacheKey);

    public async Task<string?> GetValueAsync(string key)
    {
        var field = OmniSettingsCatalog.AllFields
            .FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));

        var values = await GetCachedValuesAsync();
        var raw = values.GetValueOrDefault(key);

        if (raw is null || field is null || !field.IsSecret) return raw;

        try
        {
            return _encryption.Decrypt(raw);
        }
        catch (Exception ex)
        {
            // A key encrypted under a different Encryption:Key cannot be recovered. Reported as
            // absent rather than thrown, so a rotated deployment degrades to "no credential
            // configured" instead of failing every request that reads it.
            _logger.LogError(ex, "Could not decrypt setting {Key}. It must be re-entered.", key);
            return null;
        }
    }

    public async Task<bool> GetFlagAsync(string key, bool fallback = false)
    {
        var raw = await GetValueAsync(key);
        return bool.TryParse(raw, out var flag) ? flag : fallback;
    }

    /// <inheritdoc />
    public async Task<int> GetNumberAsync(string key, int fallback)
    {
        var raw = await GetValueAsync(key);
        return int.TryParse(raw, out var number) ? number : fallback;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetListAsync(string key)
    {
        var raw = await GetValueAsync(key);
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();

        try
        {
            return JsonSerializer.Deserialize<List<string>>(raw, JsonOptions) ?? [];
        }
        catch (JsonException ex)
        {
            // A hand-edited row should not take down the feature reading it.
            _logger.LogWarning(ex, "Setting {Key} does not hold a JSON array; treating it as empty.", key);
            return Array.Empty<string>();
        }
    }
}
