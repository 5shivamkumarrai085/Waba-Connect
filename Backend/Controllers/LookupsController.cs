using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.Setup;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Controllers;

/// <summary>
/// CRUD for the contact status, source and language lookups.
///
/// <para>
/// The immutable <c>Value</c> is generated from the name on create and never editable
/// afterwards, because contacts store it. Renaming is therefore always safe — it changes a
/// label and touches no contact rows.
/// </para>
/// </summary>
[ApiController]
[Route("api/setup")]
[Authorize]
public class LookupsController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IAuditService _auditService;

    public LookupsController(AppDbContext dbContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _auditService = auditService;
    }

    // ── Statuses ─────────────────────────────────────────────────────────────

    [HttpGet("statuses")]
    [RequiresPermission("Status.View")]
    public async Task<IActionResult> GetStatuses()
    {
        var statuses = await _dbContext.ContactStatuses.AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync();
        // One grouped query rather than a count per row.
        var usage = await _dbContext.Contacts.IgnoreQueryFilters()
            .Where(c => !c.IsDeleted)
            .GroupBy(c => c.Status)
            .Select(g => new { Value = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Value, g => g.Count);

        var data = statuses.Select(s => new ContactStatusResponse
        {
            Id = s.Id,
            Value = s.Value,
            Name = s.Name,
            Color = s.Color,
            IsActive = s.IsActive,
            IsSystem = s.IsSystem,
            SortOrder = s.SortOrder,
            UsageCount = usage.GetValueOrDefault(s.Value)
        }).ToList();

        return Ok(new ApiResponse<List<ContactStatusResponse>> { Success = true, Data = data });
    }

    [HttpPost("statuses")]
    [RequiresPermission("Status.Create")]
    public async Task<IActionResult> CreateStatus([FromBody] SaveContactStatusRequest request)
    {
        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new ApiResponse { Success = false, Message = "Name is required." });

        var value = await GenerateUniqueValueAsync(name, v => _dbContext.ContactStatuses.AnyAsync(s => s.Value == v));

        if (await _dbContext.ContactStatuses.AnyAsync(s => s.Name.ToLower() == name.ToLower()))
            return Conflict(new ApiResponse { Success = false, Message = "A status with this name already exists." });

        var entity = new ContactStatusLookup
        {
            Value = value,
            Name = name,
            Color = request.Color,
            IsActive = request.IsActive,
            IsSystem = false,
            SortOrder = request.SortOrder
        };

        _dbContext.ContactStatuses.Add(entity);
        await _dbContext.SaveChangesAsync();
        await _auditService.LogAsync("Status.Created", "Settings", $"Created contact status '{name}'.", nameof(ContactStatusLookup), entity.Id.ToString());

        return Ok(new ApiResponse { Success = true, Message = "Status created successfully." });
    }

    [HttpPut("statuses/{id:int}")]
    [RequiresPermission("Status.Edit")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] SaveContactStatusRequest request)
    {
        var entity = await _dbContext.ContactStatuses.FirstOrDefaultAsync(s => s.Id == id);
        if (entity is null) return NotFound(new ApiResponse { Success = false, Message = "Status not found." });

        var name = request.Name.Trim();
        if (await _dbContext.ContactStatuses.AnyAsync(s => s.Name.ToLower() == name.ToLower() && s.Id != id))
            return Conflict(new ApiResponse { Success = false, Message = "A status with this name already exists." });

        // Value is intentionally never updated — contacts reference it.
        entity.Name = name;
        entity.Color = request.Color;
        entity.IsActive = request.IsActive;
        entity.SortOrder = request.SortOrder;
        entity.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
        await _auditService.LogAsync("Status.Updated", "Settings", $"Updated contact status '{name}'.", nameof(ContactStatusLookup), id.ToString());

        return Ok(new ApiResponse { Success = true, Message = "Status updated successfully." });
    }

    [HttpDelete("statuses/{id:int}")]
    [RequiresPermission("Status.Delete")]
    public async Task<IActionResult> DeleteStatus(int id)
    {
        var entity = await _dbContext.ContactStatuses.FirstOrDefaultAsync(s => s.Id == id);
        if (entity is null) return NotFound(new ApiResponse { Success = false, Message = "Status not found." });

        if (entity.IsSystem)
            return Conflict(new ApiResponse { Success = false, Message = $"'{entity.Name}' is a built-in status and cannot be deleted. Deactivate it instead." });

        var inUse = await _dbContext.Contacts.IgnoreQueryFilters().CountAsync(c => !c.IsDeleted && c.Status == entity.Value);
        if (inUse > 0)
        {
            return Conflict(new ApiResponse
            {
                Success = false,
                Message = $"'{entity.Name}' is used by {inUse} contact(s). Reassign them or deactivate this status instead."
            });
        }

        _dbContext.ContactStatuses.Remove(entity);
        await _dbContext.SaveChangesAsync();
        await _auditService.LogAsync("Status.Deleted", "Settings", $"Deleted contact status '{entity.Name}'.", nameof(ContactStatusLookup), id.ToString());

        return Ok(new ApiResponse { Success = true, Message = "Status deleted successfully." });
    }

    // ── Types ────────────────────────────────────────────────────────────────
    //
    // Deliberately identical in shape to Statuses above. Contact.Type stores the immutable
    // Value, so renaming a type is a one-row update; deleting one is blocked while contacts
    // still reference it.

    [HttpGet("types")]
    [RequiresPermission("ContactType.View")]
    public async Task<IActionResult> GetTypes()
    {
        var types = await _dbContext.ContactTypes.AsNoTracking().OrderBy(t => t.SortOrder).ToListAsync();
        var usage = await _dbContext.Contacts.IgnoreQueryFilters()
            .Where(c => !c.IsDeleted)
            .GroupBy(c => c.Type)
            .Select(g => new { Value = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Value, g => g.Count);

        var data = types.Select(t => new ContactTypeResponse
        {
            Id = t.Id,
            Value = t.Value,
            Name = t.Name,
            Color = t.Color,
            IsActive = t.IsActive,
            IsSystem = t.IsSystem,
            SortOrder = t.SortOrder,
            UsageCount = usage.GetValueOrDefault(t.Value)
        }).ToList();

        return Ok(new ApiResponse<List<ContactTypeResponse>> { Success = true, Data = data });
    }

    [HttpPost("types")]
    [RequiresPermission("ContactType.Create")]
    public async Task<IActionResult> CreateType([FromBody] SaveContactTypeRequest request)
    {
        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new ApiResponse { Success = false, Message = "Name is required." });

        if (await _dbContext.ContactTypes.AnyAsync(t => t.Name.ToLower() == name.ToLower()))
            return Conflict(new ApiResponse { Success = false, Message = "A type with this name already exists." });

        var value = await GenerateUniqueValueAsync(name, v => _dbContext.ContactTypes.AnyAsync(t => t.Value == v));

        var entity = new ContactTypeLookup
        {
            Value = value,
            Name = name,
            Color = request.Color,
            IsActive = request.IsActive,
            IsSystem = false,
            SortOrder = request.SortOrder
        };

        _dbContext.ContactTypes.Add(entity);
        await _dbContext.SaveChangesAsync();
        await _auditService.LogAsync("ContactType.Created", "Settings", $"Created contact type '{name}'.", nameof(ContactTypeLookup), entity.Id.ToString());

        return Ok(new ApiResponse { Success = true, Message = "Type created successfully." });
    }

    [HttpPut("types/{id:int}")]
    [RequiresPermission("ContactType.Edit")]
    public async Task<IActionResult> UpdateType(int id, [FromBody] SaveContactTypeRequest request)
    {
        var entity = await _dbContext.ContactTypes.FirstOrDefaultAsync(t => t.Id == id);
        if (entity is null) return NotFound(new ApiResponse { Success = false, Message = "Type not found." });

        var name = request.Name.Trim();
        if (await _dbContext.ContactTypes.AnyAsync(t => t.Name.ToLower() == name.ToLower() && t.Id != id))
            return Conflict(new ApiResponse { Success = false, Message = "A type with this name already exists." });

        // Value is intentionally never updated — contacts reference it.
        entity.Name = name;
        entity.Color = request.Color;
        entity.IsActive = request.IsActive;
        entity.SortOrder = request.SortOrder;
        entity.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
        await _auditService.LogAsync("ContactType.Updated", "Settings", $"Updated contact type '{name}'.", nameof(ContactTypeLookup), id.ToString());

        return Ok(new ApiResponse { Success = true, Message = "Type updated successfully." });
    }

    [HttpDelete("types/{id:int}")]
    [RequiresPermission("ContactType.Delete")]
    public async Task<IActionResult> DeleteType(int id)
    {
        var entity = await _dbContext.ContactTypes.FirstOrDefaultAsync(t => t.Id == id);
        if (entity is null) return NotFound(new ApiResponse { Success = false, Message = "Type not found." });

        if (entity.IsSystem)
            return Conflict(new ApiResponse { Success = false, Message = $"'{entity.Name}' is a built-in type and cannot be deleted. Deactivate it instead." });

        var inUse = await _dbContext.Contacts.IgnoreQueryFilters().CountAsync(c => !c.IsDeleted && c.Type == entity.Value);
        if (inUse > 0)
        {
            return Conflict(new ApiResponse
            {
                Success = false,
                Message = $"'{entity.Name}' is used by {inUse} contact(s). Reassign them or deactivate this type instead."
            });
        }

        _dbContext.ContactTypes.Remove(entity);
        await _dbContext.SaveChangesAsync();
        await _auditService.LogAsync("ContactType.Deleted", "Settings", $"Deleted contact type '{entity.Name}'.", nameof(ContactTypeLookup), id.ToString());

        return Ok(new ApiResponse { Success = true, Message = "Type deleted successfully." });
    }

    // ── Sources ──────────────────────────────────────────────────────────────

    [HttpGet("sources")]
    [RequiresPermission("Source.View")]
    public async Task<IActionResult> GetSources()
    {
        var sources = await _dbContext.ContactSources.AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync();
        var usage = await _dbContext.Contacts.IgnoreQueryFilters()
            .Where(c => !c.IsDeleted)
            .GroupBy(c => c.Source)
            .Select(g => new { Value = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Value, g => g.Count);

        var data = sources.Select(s => new ContactSourceResponse
        {
            Id = s.Id,
            Value = s.Value,
            Name = s.Name,
            Color = s.Color,
            IsActive = s.IsActive,
            IsSystem = s.IsSystem,
            SortOrder = s.SortOrder,
            UsageCount = usage.GetValueOrDefault(s.Value)
        }).ToList();

        return Ok(new ApiResponse<List<ContactSourceResponse>> { Success = true, Data = data });
    }

    [HttpPost("sources")]
    [RequiresPermission("Source.Create")]
    public async Task<IActionResult> CreateSource([FromBody] SaveContactSourceRequest request)
    {
        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new ApiResponse { Success = false, Message = "Name is required." });

        if (await _dbContext.ContactSources.AnyAsync(s => s.Name.ToLower() == name.ToLower()))
            return Conflict(new ApiResponse { Success = false, Message = "A source with this name already exists." });

        var value = await GenerateUniqueValueAsync(name, v => _dbContext.ContactSources.AnyAsync(s => s.Value == v));

        var entity = new ContactSourceLookup
        {
            Value = value,
            Name = name,
            IsActive = request.IsActive,
            IsSystem = false,
            SortOrder = request.SortOrder
        };

        _dbContext.ContactSources.Add(entity);
        await _dbContext.SaveChangesAsync();
        await _auditService.LogAsync("Source.Created", "Settings", $"Created contact source '{name}'.", nameof(ContactSourceLookup), entity.Id.ToString());

        return Ok(new ApiResponse { Success = true, Message = "Source created successfully." });
    }

    [HttpPut("sources/{id:int}")]
    [RequiresPermission("Source.Edit")]
    public async Task<IActionResult> UpdateSource(int id, [FromBody] SaveContactSourceRequest request)
    {
        var entity = await _dbContext.ContactSources.FirstOrDefaultAsync(s => s.Id == id);
        if (entity is null) return NotFound(new ApiResponse { Success = false, Message = "Source not found." });

        var name = request.Name.Trim();
        if (await _dbContext.ContactSources.AnyAsync(s => s.Name.ToLower() == name.ToLower() && s.Id != id))
            return Conflict(new ApiResponse { Success = false, Message = "A source with this name already exists." });

        entity.Name = name;
        entity.IsActive = request.IsActive;
        entity.SortOrder = request.SortOrder;
        entity.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
        await _auditService.LogAsync("Source.Updated", "Settings", $"Updated contact source '{name}'.", nameof(ContactSourceLookup), id.ToString());

        return Ok(new ApiResponse { Success = true, Message = "Source updated successfully." });
    }

    [HttpDelete("sources/{id:int}")]
    [RequiresPermission("Source.Delete")]
    public async Task<IActionResult> DeleteSource(int id)
    {
        var entity = await _dbContext.ContactSources.FirstOrDefaultAsync(s => s.Id == id);
        if (entity is null) return NotFound(new ApiResponse { Success = false, Message = "Source not found." });

        if (entity.IsSystem)
            return Conflict(new ApiResponse { Success = false, Message = $"'{entity.Name}' is a built-in source and cannot be deleted. Deactivate it instead." });

        var inUse = await _dbContext.Contacts.IgnoreQueryFilters().CountAsync(c => !c.IsDeleted && c.Source == entity.Value);
        if (inUse > 0)
        {
            return Conflict(new ApiResponse
            {
                Success = false,
                Message = $"'{entity.Name}' is used by {inUse} contact(s). Reassign them or deactivate this source instead."
            });
        }

        _dbContext.ContactSources.Remove(entity);
        await _dbContext.SaveChangesAsync();
        await _auditService.LogAsync("Source.Deleted", "Settings", $"Deleted contact source '{entity.Name}'.", nameof(ContactSourceLookup), id.ToString());

        return Ok(new ApiResponse { Success = true, Message = "Source deleted successfully." });
    }

    // ── Languages ────────────────────────────────────────────────────────────

    [HttpGet("languages")]
    [RequiresPermission("Language.View")]
    public async Task<IActionResult> GetLanguages()
    {
        var data = await _dbContext.Languages
            .AsNoTracking()
            .OrderBy(l => l.SortOrder)
            .Select(l => new LanguageResponse
            {
                Id = l.Id,
                Code = l.Code,
                Name = l.Name,
                Color = l.Color,
                IsActive = l.IsActive,
                IsDefault = l.IsDefault,
                SortOrder = l.SortOrder,
                TranslationCount = l.Translations.Count
            })
            .ToListAsync();

        return Ok(new ApiResponse<List<LanguageResponse>> { Success = true, Data = data });
    }

    [HttpPost("languages")]
    [RequiresPermission("Language.Create")]
    public async Task<IActionResult> CreateLanguage([FromBody] SaveLanguageRequest request)
    {
        var code = request.Code.Trim().ToLowerInvariant();
        var name = request.Name.Trim();

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
            return BadRequest(new ApiResponse { Success = false, Message = "Code and name are required." });

        if (await _dbContext.Languages.AnyAsync(l => l.Code == code))
            return Conflict(new ApiResponse { Success = false, Message = $"A language with code '{code}' already exists." });

        var entity = new Language
        {
            Code = code,
            Name = name,
            IsActive = request.IsActive,
            IsDefault = false,
            SortOrder = request.SortOrder
        };

        _dbContext.Languages.Add(entity);
        await _dbContext.SaveChangesAsync();

        if (request.IsDefault) await SetDefaultLanguageAsync(entity.Id);

        await _auditService.LogAsync("Language.Created", "Settings", $"Created language '{name}' ({code}).", nameof(Language), entity.Id.ToString());
        return Ok(new ApiResponse { Success = true, Message = "Language created successfully." });
    }

    [HttpPut("languages/{id:int}")]
    [RequiresPermission("Language.Edit")]
    public async Task<IActionResult> UpdateLanguage(int id, [FromBody] SaveLanguageRequest request)
    {
        var entity = await _dbContext.Languages.FirstOrDefaultAsync(l => l.Id == id);
        if (entity is null) return NotFound(new ApiResponse { Success = false, Message = "Language not found." });

        var code = request.Code.Trim().ToLowerInvariant();
        if (await _dbContext.Languages.AnyAsync(l => l.Code == code && l.Id != id))
            return Conflict(new ApiResponse { Success = false, Message = $"A language with code '{code}' already exists." });

        // The default language must stay usable, so it can't also be deactivated.
        if (entity.IsDefault && !request.IsActive)
            return Conflict(new ApiResponse { Success = false, Message = "The default language cannot be deactivated. Make another language the default first." });

        entity.Code = code;
        entity.Name = request.Name.Trim();
        entity.IsActive = request.IsActive;
        entity.SortOrder = request.SortOrder;
        entity.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        if (request.IsDefault && !entity.IsDefault) await SetDefaultLanguageAsync(id);

        await _auditService.LogAsync("Language.Updated", "Settings", $"Updated language '{entity.Name}'.", nameof(Language), id.ToString());
        return Ok(new ApiResponse { Success = true, Message = "Language updated successfully." });
    }

    [HttpDelete("languages/{id:int}")]
    [RequiresPermission("Language.Delete")]
    public async Task<IActionResult> DeleteLanguage(int id)
    {
        var entity = await _dbContext.Languages.FirstOrDefaultAsync(l => l.Id == id);
        if (entity is null) return NotFound(new ApiResponse { Success = false, Message = "Language not found." });

        if (entity.IsDefault)
            return Conflict(new ApiResponse { Success = false, Message = "The default language cannot be deleted. Make another language the default first." });

        // Translations cascade with the language, which is correct — they're meaningless without it.
        _dbContext.Languages.Remove(entity);
        await _dbContext.SaveChangesAsync();
        await _auditService.LogAsync("Language.Deleted", "Settings", $"Deleted language '{entity.Name}'.", nameof(Language), id.ToString());

        return Ok(new ApiResponse { Success = true, Message = "Language deleted successfully." });
    }

    // ── Translations ─────────────────────────────────────────────────────────

    [HttpGet("languages/{id:int}/translations")]
    [RequiresPermission("Language.Translate", "Language.View")]
    public async Task<IActionResult> GetTranslations(int id)
    {
        if (!await _dbContext.Languages.AnyAsync(l => l.Id == id))
            return NotFound(new ApiResponse { Success = false, Message = "Language not found." });

        var data = await _dbContext.Translations
            .AsNoTracking()
            .Where(t => t.LanguageId == id)
            .OrderBy(t => t.Key)
            .Select(t => new TranslationResponse { Id = t.Id, Key = t.Key, Value = t.Value })
            .ToListAsync();

        return Ok(new ApiResponse<List<TranslationResponse>> { Success = true, Data = data });
    }

    /// <summary>
    /// Upserts translations for a language. An empty value deletes the entry rather than
    /// storing a blank, so a cleared field falls back to the key's default.
    /// </summary>
    [HttpPut("languages/{id:int}/translations")]
    [RequiresPermission("Language.Translate")]
    public async Task<IActionResult> SaveTranslations(int id, [FromBody] SaveTranslationsRequest request)
    {
        var language = await _dbContext.Languages.FirstOrDefaultAsync(l => l.Id == id);
        if (language is null) return NotFound(new ApiResponse { Success = false, Message = "Language not found." });

        var existing = await _dbContext.Translations.Where(t => t.LanguageId == id).ToListAsync();
        var byKey = existing.ToDictionary(t => t.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in request.Entries)
        {
            var key = entry.Key?.Trim();
            if (string.IsNullOrWhiteSpace(key)) continue;

            byKey.TryGetValue(key, out var row);

            if (string.IsNullOrWhiteSpace(entry.Value))
            {
                if (row is not null) _dbContext.Translations.Remove(row);
                continue;
            }

            if (row is null)
            {
                _dbContext.Translations.Add(new Translation { LanguageId = id, Key = key, Value = entry.Value });
            }
            else
            {
                row.Value = entry.Value;
            }
        }

        await _dbContext.SaveChangesAsync();
        await _auditService.LogAsync("Language.TranslationsSaved", "Settings", $"Updated translations for '{language.Name}'.", nameof(Language), id.ToString());

        return Ok(new ApiResponse { Success = true, Message = "Translations saved." });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task SetDefaultLanguageAsync(int languageId)
    {
        var all = await _dbContext.Languages.ToListAsync();
        foreach (var language in all) language.IsDefault = language.Id == languageId;
        await _dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Derives a stable, code-safe Value from a display name ("In Review" → "InReview"),
    /// appending a counter if it collides. This becomes immutable once stored.
    /// </summary>
    private static async Task<string> GenerateUniqueValueAsync(string name, Func<string, Task<bool>> exists)
    {
        var baseValue = new string(name.Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrWhiteSpace(baseValue)) baseValue = "Custom";
        if (baseValue.Length > 40) baseValue = baseValue[..40];

        var candidate = baseValue;
        var suffix = 2;
        while (await exists(candidate))
        {
            candidate = $"{baseValue}{suffix++}";
        }

        return candidate;
    }
}
