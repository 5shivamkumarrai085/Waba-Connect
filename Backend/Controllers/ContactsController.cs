using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.Contacts;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ContactsController : ControllerBase
{
    private readonly IContactService _contactService;
    private readonly AppDbContext _dbContext;
    private readonly IDashboardCacheService _dashboardCacheService;

    private readonly IChatService _chatService;

    public ContactsController(
        IContactService contactService,
        AppDbContext dbContext,
        IDashboardCacheService dashboardCacheService,
        IChatService chatService)
    {
        _contactService = contactService;
        _dbContext = dbContext;
        _dashboardCacheService = dashboardCacheService;
        _chatService = chatService;
    }

    [HttpGet]
    [RequiresPermission("Contact.View")]
    public async Task<ActionResult<ApiResponse<PagedResponse<ContactResponse>>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? type = null,
        [FromQuery] string? status = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool sortDescending = false,
        [FromQuery] string? assignedTo = null,
        [FromQuery] string? source = null,
        [FromQuery] int? groupId = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var request = new PagedRequest
        {
            Page = page,
            PageSize = pageSize,
            Search = search,
            SortBy = sortBy,
            SortDescending = sortDescending
        };

        var data = await _contactService.GetAllAsync(
            request, 
            type, 
            status, 
            isActive,
            assignedTo,
            source,
            groupId,
            startDate,
            endDate);
        return Ok(new ApiResponse<PagedResponse<ContactResponse>> { Success = true, Data = data });
    }

    [HttpGet("{id}")]
    [RequiresPermission("Contact.View")]
    public async Task<ActionResult<ApiResponse<ContactResponse>>> GetById(int id)
    {
        var data = await _contactService.GetByIdAsync(id);
        return Ok(new ApiResponse<ContactResponse> { Success = true, Data = data });
    }

    [HttpPost]
    [RequiresPermission("Contact.Create")]
    public async Task<ActionResult<ApiResponse<ContactResponse>>> Create([FromBody] CreateContactRequest request)
    {
        var data = await _contactService.CreateAsync(request);
        _dashboardCacheService.InvalidateCache();
        return CreatedAtAction(nameof(GetById), new { id = data.Id }, new ApiResponse<ContactResponse> { Success = true, Data = data });
    }

    [HttpPut("{id}")]
    [RequiresPermission("Contact.Edit")]
    public async Task<ActionResult<ApiResponse<ContactResponse>>> Update(int id, [FromBody] UpdateContactRequest request)
    {
        var data = await _contactService.UpdateAsync(id, request);
        _dashboardCacheService.InvalidateCache();
        return Ok(new ApiResponse<ContactResponse> { Success = true, Data = data });
    }

    [HttpDelete("{id}")]
    [RequiresPermission("Contact.Delete")]
    public async Task<ActionResult<ApiResponse>> Delete(int id)
    {
        await _contactService.DeleteAsync(id);
        _dashboardCacheService.InvalidateCache();
        return Ok(new ApiResponse { Success = true, Message = "Contact deleted successfully." });
    }

    public class BulkDeleteRequest
    {
        public List<int> Ids { get; set; } = [];
    }

    [HttpPost("bulk-delete")]
    [RequiresPermission("Contact.Delete")]
    public async Task<ActionResult<ApiResponse>> BulkDelete([FromBody] BulkDeleteRequest request)
    {
        if (request?.Ids == null || !request.Ids.Any())
            return BadRequest(new ApiResponse { Success = false, Message = "No contact IDs provided." });

        foreach (var id in request.Ids)
        {
            try
            {
                await _contactService.DeleteAsync(id);
            }
            catch (KeyNotFoundException) { }
        }

        _dashboardCacheService.InvalidateCache();
        return Ok(new ApiResponse { Success = true, Message = "Contacts deleted successfully." });
    }

    [HttpPatch("{id}/toggle-active")]
    [RequiresPermission("Contact.Edit")]
    public async Task<ActionResult<ApiResponse<ContactResponse>>> ToggleActive(int id)
    {
        var data = await _contactService.ToggleActiveAsync(id);
        _dashboardCacheService.InvalidateCache();
        return Ok(new ApiResponse<ContactResponse> { Success = true, Data = data });
    }

    /// <remarks>
    /// Now served from the editable ContactStatuses lookup rather than a literal array.
    /// The response shape is unchanged — a bare array of { id, name } where id is the stored
    /// value — so every existing caller keeps working without a change. `color` is added as an
    /// extra key, which existing consumers simply ignore.
    /// </remarks>
    [HttpGet("statuses")]
    [RequiresPermission("Contact.View")]
    public async Task<IActionResult> GetStatuses()
    {
        var list = await _dbContext.ContactStatuses
            .AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.SortOrder)
            .Select(s => new { id = s.Value, name = s.Name, color = s.Color })
            .ToListAsync();

        return Ok(list);
    }

    [HttpGet("sources")]
    [RequiresPermission("Contact.View")]
    public async Task<IActionResult> GetSources()
    {
        var list = await _dbContext.ContactSources
            .AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.SortOrder)
            .Select(s => new { id = s.Value, name = s.Name, color = s.Color })
            .ToListAsync();

        return Ok(list);
    }

    [HttpGet("settings")]
    [RequiresPermission("Contact.View")]
    public IActionResult GetSettings()
    {
        var config = new { groupNotAssignedText = "Group not assigned" };
        return Ok(new ApiResponse<object> { Success = true, Data = config });
    }

    /// <remarks>
    /// Real staff accounts, replacing the three hardcoded names this used to return. `id` stays
    /// the display name because Contact.AssignedTo stores a name rather than a foreign key —
    /// changing that is a data migration in its own right and out of scope here.
    /// </remarks>
    [HttpGet("assigned-users")]
    [RequiresPermission("Contact.View")]
    public async Task<IActionResult> GetAssignedUsers()
    {
        var list = await _dbContext.AppUsers
            .AsNoTracking()
            .Where(u => !u.IsDeleted && u.IsActive)
            .OrderBy(u => u.FirstName)
            .Select(u => new
            {
                id = u.LastName == null ? u.FirstName : u.FirstName + " " + u.LastName,
                name = u.LastName == null ? u.FirstName : u.FirstName + " " + u.LastName
            })
            .ToListAsync();

        return Ok(list);
    }

    /// <remarks>
    /// Reads the editable lookup table instead of the old enum. The previous version returned
    /// <c>name.ToLower()</c>, which is why the type dropdown read "lead" rather than "Lead", and
    /// meant an admin could not add a type without a code change.
    /// </remarks>
    [HttpGet("types")]
    [RequiresPermission("Contact.View")]
    public async Task<IActionResult> GetTypes()
    {
        var list = await _dbContext.ContactTypes
            .AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.SortOrder)
            .Select(t => new { id = t.Value, name = t.Name, color = t.Color })
            .ToArrayAsync();

        return Ok(list);
    }

    [HttpGet("languages")]
    [RequiresPermission("Contact.View")]
    public async Task<IActionResult> GetLanguages()
    {
        var list = await _dbContext.Languages
            .AsNoTracking()
            .Where(l => l.IsActive)
            .OrderBy(l => l.SortOrder)
            .Select(l => new { id = l.Code, name = l.Name })
            .ToListAsync();

        return Ok(list);
    }

    [HttpGet("countries")]
    [RequiresPermission("Contact.View")]
    public IActionResult GetCountries()
    {
        var list = new[]
        {
            new { id = "India", name = "India (+91)", code = "IN", dialCode = "+91" },
            new { id = "Malaysia", name = "Malaysia (+60)", code = "MY", dialCode = "+60" },
            new { id = "Singapore", name = "Singapore (+65)", code = "SG", dialCode = "+65" },
            new { id = "UnitedStates", name = "United States (+1)", code = "US", dialCode = "+1" },
            new { id = "UnitedKingdom", name = "United Kingdom (+44)", code = "GB", dialCode = "+44" }
        };
        return Ok(list);
    }

    [HttpGet("{contactId}/notes")]
    [RequiresPermission("Contact.View")]
    public async Task<ActionResult<ApiResponse<List<ContactNote>>>> GetNotes(int contactId)
    {
        var notes = await _dbContext.ContactNotes
            .Where(n => n.ContactId == contactId)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync();
        return Ok(new ApiResponse<List<ContactNote>> { Success = true, Data = notes });
    }

    public class CreateNoteRequest
    {
        public string Content { get; set; } = string.Empty;
    }

    [HttpPost("{contactId}/notes")]
    [RequiresPermission("Contact.Edit")]
    public async Task<ActionResult<ApiResponse<ContactNote>>> CreateNote(int contactId, [FromBody] CreateNoteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest(new ApiResponse { Success = false, Message = "Note content is required." });

        var note = new ContactNote
        {
            ContactId = contactId,
            Content = request.Content.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.ContactNotes.Add(note);
        await _dbContext.SaveChangesAsync();

        return Ok(new ApiResponse<ContactNote> { Success = true, Data = note });
    }

    [HttpDelete("{contactId}/notes/{noteId}")]
    [RequiresPermission("Contact.Edit")]
    public async Task<ActionResult<ApiResponse>> DeleteNote(int contactId, int noteId)
    {
        var note = await _dbContext.ContactNotes
            .FirstOrDefaultAsync(n => n.ContactId == contactId && n.Id == noteId);

        if (note == null)
            return NotFound(new ApiResponse { Success = false, Message = "Note not found." });

        _dbContext.ContactNotes.Remove(note);
        await _dbContext.SaveChangesAsync();

        return Ok(new ApiResponse { Success = true, Message = "Note deleted successfully." });
    }

    [HttpGet("csv-sample")]
    [RequiresPermission("Contact.Import")]
    public IActionResult GetCsvSample()
    {
        var csvContent = "status_id,source_id,assigned_id,firstname,lastname,company,type,email,phone\n1,1,1,sample data,sample data,,lead,abc@gmail.com,+1 555 123 4567\n";
        var bytes = System.Text.Encoding.UTF8.GetBytes(csvContent);
        return File(bytes, "text/csv", "contacts_sample.csv");
    }

    [HttpPost("csv-import")]
    [Consumes("multipart/form-data")]
    [RequiresPermission("Contact.Import")]
    public async Task<ActionResult<ApiResponse>> ImportCsv(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new ApiResponse { Success = false, Message = "wrong format csv file" });
        }

        if (!Path.GetExtension(file.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new ApiResponse { Success = false, Message = "wrong format csv file" });
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.csv");
        using (var fileStream = new FileStream(tempPath, FileMode.Create))
        {
            await file.CopyToAsync(fileStream);
        }

        try
        {
            var lines = await System.IO.File.ReadAllLinesAsync(tempPath);
            if (lines.Length < 2)
            {
                return BadRequest(new ApiResponse { Success = false, Message = "wrong format csv file" });
            }

            var headers = SplitCsvRow(lines[0]).Select(h => h.ToLower().Trim()).ToList();

            int statusIdIdx = headers.FindIndex(h => h == "status_id");
            int sourceIdIdx = headers.FindIndex(h => h == "source_id");
            int assignedIdIdx = headers.FindIndex(h => h == "assigned_id");
            int firstNameIdx = headers.FindIndex(h => h == "firstname" || h == "first name" || h == "first_name");
            int lastNameIdx = headers.FindIndex(h => h == "lastname" || h == "last name" || h == "last_name");
            int companyIdx = headers.FindIndex(h => h == "company");
            int typeIdx = headers.FindIndex(h => h == "type");
            int emailIdx = headers.FindIndex(h => h == "email");
            int phoneIdx = headers.FindIndex(h => h == "phone" || h == "phone number" || h == "phoneno");

            if (statusIdIdx == -1 || sourceIdIdx == -1 || assignedIdIdx == -1 || 
                firstNameIdx == -1 || lastNameIdx == -1 || typeIdx == -1 || phoneIdx == -1)
            {
                return BadRequest(new ApiResponse { Success = false, Message = "wrong format csv file" });
            }

            var phoneRegex = new System.Text.RegularExpressions.Regex(@"^\+[1-9]\d{6,14}$");
            var contactsToImport = new List<Contact>();

            // Loaded once rather than per row — a large import would otherwise issue three
            // queries for every line. Unfiltered by IsActive so a CSV referencing a retired
            // status still resolves rather than failing the whole file.
            var statusLookup = await _dbContext.ContactStatuses
                .AsNoTracking()
                .Select(s => new LookupPair(s.Value, s.Name))
                .ToListAsync();

            var sourceLookup = await _dbContext.ContactSources
                .AsNoTracking()
                .Select(s => new LookupPair(s.Value, s.Name))
                .ToListAsync();

            var typeLookup = await _dbContext.ContactTypes
                .AsNoTracking()
                .Select(t => new LookupPair(t.Value, t.Name))
                .ToListAsync();

            var assignableUsers = await _dbContext.AppUsers
                .AsNoTracking()
                .Where(u => !u.IsDeleted)
                .Select(u => new AssignableUserRef(
                    u.Id,
                    u.LastName == null ? u.FirstName : u.FirstName + " " + u.LastName,
                    u.Email))
                .ToListAsync();

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                var fields = SplitCsvRow(line);
                if (fields.Count < headers.Count)
                {
                    return BadRequest(new ApiResponse { Success = false, Message = "wrong format csv file" });
                }

                var firstName = fields[firstNameIdx].Trim();
                var lastName = fields[lastNameIdx].Trim();
                var phoneVal = fields[phoneIdx].Trim();
                var emailVal = emailIdx != -1 && emailIdx < fields.Count ? fields[emailIdx].Trim() : string.Empty;
                var typeVal = fields[typeIdx].Trim();
                var statusVal = fields[statusIdIdx].Trim();
                var sourceVal = fields[sourceIdIdx].Trim();
                var assignedVal = fields[assignedIdIdx].Trim();

                if (firstName.Length < 2)
                {
                    return BadRequest(new ApiResponse { Success = false, Message = "wrong format csv file" });
                }

                var cleanedPhone = phoneVal.Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "");
                if (!cleanedPhone.StartsWith("+"))
                {
                    cleanedPhone = "+" + cleanedPhone;
                }

                if (!phoneRegex.IsMatch(cleanedPhone))
                {
                    return BadRequest(new ApiResponse { Success = false, Message = "wrong format csv file" });
                }

                // Type resolves against the lookup table like status and source. Same ordinal
                // fallback, so a CSV exported when Type was an enum still imports.
                var contactType = ResolveLookupValue(
                    typeVal, typeLookup, ordinal => Enum.IsDefined(typeof(ContactType), ordinal)
                        ? ((ContactType)ordinal).ToString()
                        : null);

                if (contactType is null)
                {
                    return BadRequest(new ApiResponse { Success = false, Message = "wrong format csv file" });
                }

                // Status and source now resolve against the editable lookup tables. Numeric
                // values are still accepted and mapped through the original enum ordinals, so
                // CSV files exported before this change keep importing unchanged.
                var contactStatus = ResolveLookupValue(
                    statusVal, statusLookup, ordinal => Enum.IsDefined(typeof(ContactStatus), ordinal)
                        ? ((ContactStatus)ordinal).ToString()
                        : null);

                if (contactStatus is null)
                {
                    return BadRequest(new ApiResponse { Success = false, Message = "wrong format csv file" });
                }

                var contactSource = ResolveLookupValue(
                    sourceVal, sourceLookup, ordinal => Enum.IsDefined(typeof(ContactSource), ordinal)
                        ? ((ContactSource)ordinal).ToString()
                        : null);

                if (contactSource is null)
                {
                    return BadRequest(new ApiResponse { Success = false, Message = "wrong format csv file" });
                }

                // Resolves against real user accounts instead of the old three-name ladder.
                // A numeric value is treated as a user id; anything else is matched on name or
                // email, and falls through as free text so imports never fail on this column.
                string? assignedTo = null;
                if (!string.IsNullOrWhiteSpace(assignedVal))
                {
                    if (int.TryParse(assignedVal, out var assignedUserId))
                    {
                        assignedTo = assignableUsers.FirstOrDefault(u => u.Id == assignedUserId)?.Name ?? assignedVal;
                    }
                    else
                    {
                        var match = assignableUsers.FirstOrDefault(u =>
                            string.Equals(u.Name, assignedVal, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(u.Email, assignedVal, StringComparison.OrdinalIgnoreCase));
                        assignedTo = match?.Name ?? assignedVal;
                    }
                }

                var fullName = $"{firstName} {lastName}".Trim();
                if (fullName.Length < 2) fullName = firstName;

                contactsToImport.Add(new Contact
                {
                    Name = fullName,
                    Phone = cleanedPhone,
                    Type = contactType,
                    Status = contactStatus,
                    Source = contactSource,
                    AssignedTo = assignedTo,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }

            int importCount = 0;
            var imported = new List<Contact>();
            foreach (var contact in contactsToImport)
            {
                var exists = await _dbContext.Contacts.AnyAsync(c => c.Phone == contact.Phone);
                if (!exists)
                {
                    _dbContext.Contacts.Add(contact);
                    imported.Add(contact);
                    importCount++;
                }
            }

            await _dbContext.SaveChangesAsync();

            // Batched, and only after the save so the ids exist. One insert for the whole
            // import — calling the per-contact overload in the loop above would be an N+1.
            await _chatService.EnsureConversationsForContactsAsync(
                imported.Select(c => c.Id).ToList());

            return Ok(new ApiResponse
            {
                Success = true,
                Message = $"Successfully imported {importCount} contacts."
            });
        }
        catch (Exception)
        {
            return BadRequest(new ApiResponse { Success = false, Message = "wrong format csv file" });
        }
        finally
        {
            if (System.IO.File.Exists(tempPath))
            {
                System.IO.File.Delete(tempPath);
            }
        }
    }

    private static List<string> SplitCsvRow(string line)
    {
        var result = new List<string>();
        var inQuotes = false;
        var currentField = new System.Text.StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(currentField.ToString().Trim(' ', '"'));
                currentField.Clear();
            }
            else
            {
                currentField.Append(c);
            }
        }
        result.Add(currentField.ToString().Trim(' ', '"'));
        return result;
    }

    private record LookupPair(string Value, string Name);
    private record AssignableUserRef(int Id, string Name, string Email);

    /// <summary>
    /// Resolves a CSV cell to a lookup's stored Value.
    ///
    /// Accepts three forms, in order: a numeric legacy enum ordinal (so CSV files produced
    /// before statuses became database-driven still import), the lookup's Value, or its
    /// display Name. Returns null when nothing matches, which the caller turns into a
    /// row-level rejection.
    /// </summary>
    private static string? ResolveLookupValue(
        string? cell,
        IReadOnlyCollection<LookupPair> lookup,
        Func<int, string?> legacyOrdinalToValue)
    {
        if (string.IsNullOrWhiteSpace(cell)) return null;
        var trimmed = cell.Trim();

        if (int.TryParse(trimmed, out var ordinal))
        {
            var legacyValue = legacyOrdinalToValue(ordinal);
            // Only accept the ordinal if that value still exists in the lookup — an admin may
            // have deleted it since.
            return legacyValue is not null && lookup.Any(l => l.Value == legacyValue)
                ? legacyValue
                : null;
        }

        return lookup.FirstOrDefault(l =>
            string.Equals(l.Value, trimmed, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(l.Name, trimmed, StringComparison.OrdinalIgnoreCase))?.Value;
    }
}
