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
using WhatsAppCampaignApi.Services.Catalogs;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Validators;

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
    private readonly ILogger<ContactsController> _logger;
    private readonly IAuditService _auditService;
    private readonly ContactLookupValidatorCache _contactLookups;

    public ContactsController(
        IContactService contactService,
        AppDbContext dbContext,
        IDashboardCacheService dashboardCacheService,
        IChatService chatService,
        ILogger<ContactsController> logger,
        IAuditService auditService,
        ContactLookupValidatorCache contactLookups)
    {
        _contactService = contactService;
        _dbContext = dbContext;
        _dashboardCacheService = dashboardCacheService;
        _chatService = chatService;
        _logger = logger;
        _auditService = auditService;
        _contactLookups = contactLookups;
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
        [FromQuery] DateTime? endDate = null,
        [FromQuery] string? tag = null,
        [FromQuery] string? group = null)
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
            endDate,
            tag,
            group);
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

    /// <summary>
    /// How many active contacts there are of each type, for the campaign wizard's audience summary.
    /// One grouped query, so it stays cheap with millions of contacts (Type is indexed).
    /// </summary>
    [HttpGet("type-counts")]
    [RequiresPermission("Contact.View")]
    public async Task<IActionResult> GetTypeCounts(CancellationToken ct)
    {
        var counts = await _dbContext.Contacts
            .AsNoTracking()
            .Where(c => c.IsActive)
            .GroupBy(c => c.Type)
            .Select(g => new { type = g.Key, count = g.Count() })
            .ToListAsync(ct);

        return Ok(new ApiResponse<object> { Success = true, Data = counts });
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

        try
        {
            var note = await _contactService.AddNoteAsync(contactId, request.Content);
            return Ok(new ApiResponse<ContactNote> { Success = true, Data = note });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiResponse<ContactNote> { Success = false, Message = "Contact not found." });
        }
    }

    [HttpDelete("{contactId}/notes/{noteId}")]
    [RequiresPermission("Contact.Edit")]
    public async Task<ActionResult<ApiResponse>> DeleteNote(int contactId, int noteId)
    {
        if (!await _contactService.DeleteNoteAsync(contactId, noteId))
            return NotFound(new ApiResponse { Success = false, Message = "Note not found." });

        return Ok(new ApiResponse { Success = true, Message = "Note deleted successfully." });
    }

    [HttpGet("csv-sample")]
    [RequiresPermission("Contact.Import")]
    public async Task<IActionResult> GetCsvSample()
    {
        // Built from the same layout ImportCsv enforces, so a field an administrator makes
        // required shows up in the sample the moment the setting is saved.
        var columns = await CsvLayoutAsync();
        static string Cell(string value) => value.Contains(',') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
        var csvContent = string.Join(",", columns.Columns.Select(c => Cell(c.Header))) + "\n"
            + string.Join(",", columns.Columns.Select(c => Cell(c.Example))) + "\n";
        var bytes = System.Text.Encoding.UTF8.GetBytes(csvContent);
        return File(bytes, "text/csv", "contacts_sample.csv");
    }

    /// <summary>
    /// The columns a contacts CSV needs (with which are required and an example value) and the
    /// optional ones the importer also reads. The import dialog renders its table from this.
    /// </summary>
    [HttpGet("csv-layout")]
    [RequiresPermission("Contact.Import")]
    public async Task<ActionResult<ApiResponse<CsvContactLayoutResponse>>> GetCsvLayout() =>
        Ok(new ApiResponse<CsvContactLayoutResponse> { Success = true, Data = await CsvLayoutAsync() });

    private async Task<CsvContactLayoutResponse> CsvLayoutAsync()
    {
        var exampleType = await _dbContext.ContactTypes.AsNoTracking()
            .OrderBy(t => t.Id).Select(t => t.Value).FirstOrDefaultAsync() ?? string.Empty;
        var columns = CsvContactColumns.Layout(_contactLookups.RequiredFields(), exampleType, out var optional);
        return new CsvContactLayoutResponse(columns, optional);
    }

    /// <summary>
    /// Imports contacts from a CSV.
    ///
    /// Partial by design: valid rows are saved and invalid ones come back as row-level errors.
    /// This endpoint used to be fail-fast, returning the literal string "wrong format csv file"
    /// from eight different places in the parse loop — so a rejected file gave the user no way
    /// to tell a bad phone number from a missing column from a database outage.
    /// </summary>
    [HttpPost("csv-import")]
    [Consumes("multipart/form-data")]
    [RequiresPermission("Contact.Import")]
    public async Task<ActionResult<ApiResponse<CsvImportResponse>>> ImportCsv(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new ApiResponse<CsvImportResponse> { Success = false, Message = "No file was uploaded, or the file is empty." });
        }

        if (!Path.GetExtension(file.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new ApiResponse<CsvImportResponse> { Success = false, Message = "Only .csv files can be imported." });
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
                return BadRequest(new ApiResponse<CsvImportResponse> { Success = false, Message = "The file needs a header row and at least one data row." });
            }

            var headers = CsvHelper.SplitCsvRow(lines[0]).Select(h => h.ToLower().Trim()).ToList();

            int statusIdIdx = headers.FindIndex(h => h == "status_id");
            int sourceIdIdx = headers.FindIndex(h => h == "source_id");
            int assignedIdIdx = headers.FindIndex(h => h == "assigned_id");
            int firstNameIdx = headers.FindIndex(h => h == "firstname" || h == "first name" || h == "first_name");
            int lastNameIdx = headers.FindIndex(h => h == "lastname" || h == "last name" || h == "last_name");
            int typeIdx = headers.FindIndex(h => h == "type");
            int phoneIdx = headers.FindIndex(h => h == "phone" || h == "phone number" || h == "phoneno");

            // assigned_id is optional unless Settings › Contacts makes "Assigned to" required. The
            // parser used to demand it anyway and reject the whole file without saying so.
            var missingHeaders = new List<string>();
            if (statusIdIdx == -1) missingHeaders.Add("status_id");
            if (sourceIdIdx == -1) missingHeaders.Add("source_id");
            if (firstNameIdx == -1) missingHeaders.Add("firstname");
            if (lastNameIdx == -1) missingHeaders.Add("lastname");
            if (typeIdx == -1) missingHeaders.Add("type");
            if (phoneIdx == -1) missingHeaders.Add("phone");

            // The other contact fields (email, date of birth, company, city, ...) are read from
            // any column named after them, and the fields an administrator has made required
            // under Settings › Contacts must have a column — the same rules the contact form
            // follows, so an import cannot create contacts the form would refuse.
            var requiredFields = _contactLookups.RequiredFields();
            var optionalColumns = CsvContactColumns.Map(headers);
            foreach (var field in CsvContactColumns.Importable)
            {
                if (requiredFields.Contains(field.Key) && !optionalColumns.ContainsKey(field.Key))
                    missingHeaders.Add(CsvContactColumns.HeaderFor(field));
            }
            if (requiredFields.Contains("assignedTo") && assignedIdIdx == -1) missingHeaders.Add("assigned_id");

            if (missingHeaders.Count > 0)
            {
                return BadRequest(new ApiResponse<CsvImportResponse>
                {
                    Success = false,
                    Message = $"The file is missing required column(s): {string.Join(", ", missingHeaders)}."
                });
            }

            var contactsToImport = new List<Contact>();
            var errors = new List<CsvRowError>();
            var totalRecords = 0;

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

                totalRecords++;
                // 1-based and counting the header, so it matches the row number the user sees
                // when they open the file in a spreadsheet.
                var rowNumber = i + 1;

                var fields = CsvHelper.SplitCsvRow(line);
                if (fields.Count < headers.Count)
                {
                    errors.Add(new CsvRowError
                    {
                        RowNumber = rowNumber,
                        Column = null,
                        Value = line,
                        Reason = $"Row has {fields.Count} column(s) but the header row has {headers.Count}."
                    });
                    continue;
                }

                var firstName = fields[firstNameIdx].Trim();
                var lastName = fields[lastNameIdx].Trim();
                var phoneVal = fields[phoneIdx].Trim();
                var typeVal = fields[typeIdx].Trim();
                var statusVal = fields[statusIdIdx].Trim();
                var sourceVal = fields[sourceIdIdx].Trim();
                var assignedVal = assignedIdIdx != -1 && assignedIdIdx < fields.Count
                    ? fields[assignedIdIdx].Trim()
                    : string.Empty;

                if (firstName.Length < 2)
                {
                    errors.Add(new CsvRowError
                    {
                        RowNumber = rowNumber,
                        Column = "firstname",
                        Value = firstName,
                        Reason = "Name must be at least 2 characters long."
                    });
                    continue;
                }

                // Same normaliser as the campaign importer, so a phone column Excel typed as a
                // number ("919143000000.0") imports here too, and a genuinely bad value comes
                // back with a reason that names the actual problem.
                if (!PhoneNumberHelper.TryNormalize(phoneVal, out var cleanedPhone, out var phoneFailure))
                {
                    errors.Add(new CsvRowError
                    {
                        RowNumber = rowNumber,
                        Column = "phone",
                        Value = phoneVal,
                        Reason = phoneFailure!
                    });
                    continue;
                }

                // Type resolves against the lookup table like status and source. Same ordinal
                // fallback, so a CSV exported when Type was an enum still imports.
                var contactType = ResolveLookupValue(
                    typeVal, typeLookup, ordinal => Enum.IsDefined(typeof(ContactType), ordinal)
                        ? ((ContactType)ordinal).ToString()
                        : null);

                if (contactType is null)
                {
                    errors.Add(new CsvRowError
                    {
                        RowNumber = rowNumber,
                        Column = "type",
                        Value = typeVal,
                        Reason = "No contact type matches this value. Use a type from Setup → Type."
                    });
                    continue;
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
                    errors.Add(new CsvRowError
                    {
                        RowNumber = rowNumber,
                        Column = "status_id",
                        Value = statusVal,
                        Reason = "No contact status matches this value. Use a status from Setup → Status."
                    });
                    continue;
                }

                var contactSource = ResolveLookupValue(
                    sourceVal, sourceLookup, ordinal => Enum.IsDefined(typeof(ContactSource), ordinal)
                        ? ((ContactSource)ordinal).ToString()
                        : null);

                if (contactSource is null)
                {
                    errors.Add(new CsvRowError
                    {
                        RowNumber = rowNumber,
                        Column = "source_id",
                        Value = sourceVal,
                        Reason = "No contact source matches this value. Use a source from Setup → Source."
                    });
                    continue;
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

                if (assignedTo is null && requiredFields.Contains("assignedTo"))
                {
                    errors.Add(new CsvRowError { RowNumber = rowNumber, Column = "assigned_id", Value = assignedVal, Reason = "Assigned to is required." });
                    continue;
                }

                var fullName = $"{firstName} {lastName}".Trim();
                if (fullName.Length < ContactFieldCatalog.NameMinLength) fullName = firstName;

                var contact = new Contact
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
                };

                var fieldError = CsvContactColumns.Apply(contact, fields, optionalColumns, requiredFields);
                if (fieldError is not null)
                {
                    fieldError.RowNumber = rowNumber;
                    errors.Add(fieldError);
                    continue;
                }

                contactsToImport.Add(contact);
            }

            // One query for every candidate phone instead of an AnyAsync per row — on a remote
            // database that was one round trip per line of the file.
            //
            // IgnoreQueryFilters matters: Phone carries a unique index that a soft-deleted
            // contact still occupies. The filtered lookup could not see those rows, so importing
            // a previously-deleted contact threw a duplicate-key DbUpdateException — which the
            // blanket catch below then reported as "wrong format csv file", sending the user off
            // to fix a file that was never the problem.
            var candidatePhones = contactsToImport.Select(c => c.Phone).ToList();
            var existingPhones = await _dbContext.Contacts
                .IgnoreQueryFilters()
                .Where(c => candidatePhones.Contains(c.Phone))
                .Select(c => c.Phone)
                .ToListAsync();

            var takenPhones = new HashSet<string>(existingPhones, StringComparer.OrdinalIgnoreCase);
            var imported = new List<Contact>();
            var skippedDuplicates = 0;

            foreach (var contact in contactsToImport)
            {
                // Also guards a file that repeats the same number twice: the first row wins and
                // the second is counted as a duplicate rather than failing the whole save.
                if (takenPhones.Add(contact.Phone))
                {
                    _dbContext.Contacts.Add(contact);
                    imported.Add(contact);
                }
                else
                {
                    skippedDuplicates++;
                }
            }

            await _dbContext.SaveChangesAsync();

            // Batched, and only after the save so the ids exist. One insert for the whole
            // import — calling the per-contact overload in the loop above would be an N+1.
            await _chatService.EnsureConversationsForContactsAsync(
                imported.Select(c => c.Id).ToList());

            var result = new CsvImportResponse
            {
                TotalRecords = totalRecords,
                ImportedCount = imported.Count,
                SkippedDuplicates = skippedDuplicates,
                InvalidCount = errors.Count,
                Errors = errors
            };

            // One entry for the import, not one per contact: a 500-row file would otherwise bury
            // every other event in the trail. The counts and the file name are what an auditor
            // needs — the individual contacts are already in the contacts table.
            await _auditService.LogAsync(
                "Contact.Imported",
                "Data",
                $"Imported {imported.Count} contact(s) from \"{file.FileName}\" — " +
                $"{totalRecords} record(s) read, {skippedDuplicates} already existing, {errors.Count} rejected.",
                entityType: "Contact",
                entityId: string.Join(",", imported.Take(50).Select(c => c.Id)));

            return Ok(new ApiResponse<CsvImportResponse>
            {
                // Success reports "the file was processed", not "every row was perfect" — the
                // counts and Errors list carry that detail. A file whose rows all failed still
                // returns 200 so the UI can show which rows and why, rather than an opaque 400.
                Success = true,
                Data = result,
                Message = BuildImportSummary(result)
            });
        }
        catch (Exception ex)
        {
            // Deliberately not "wrong format csv file". Reaching here means the parse succeeded
            // and something else went wrong — a database failure, most likely — and telling the
            // user their file is malformed would be a false diagnosis.
            _logger.LogError(ex, "Contacts CSV import failed for file {FileName}", file.FileName);
            return StatusCode(500, new ApiResponse<CsvImportResponse>
            {
                Success = false,
                Message = "The import could not be completed because of a server error. No contacts were imported."
            });
        }
        finally
        {
            if (System.IO.File.Exists(tempPath))
            {
                System.IO.File.Delete(tempPath);
            }
        }
    }

    /// <summary>
    /// One sentence stating what actually happened, so the toast is useful on its own even
    /// before the user reads the row-error list.
    /// </summary>
    private static string BuildImportSummary(CsvImportResponse result)
    {
        if (result.ImportedCount == 0 && result.TotalRecords == 0)
            return "The file contained no data rows.";

        var parts = new List<string>
        {
            result.ImportedCount == 1 ? "1 contact imported" : $"{result.ImportedCount} contacts imported"
        };

        if (result.SkippedDuplicates > 0)
            parts.Add($"{result.SkippedDuplicates} skipped as already existing");

        if (result.InvalidCount > 0)
            parts.Add($"{result.InvalidCount} row(s) could not be imported");

        return $"{string.Join(", ", parts)} out of {result.TotalRecords} record(s).";
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
