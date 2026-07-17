using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.Contacts;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ContactsController : ControllerBase
{
    private readonly IContactService _contactService;
    private readonly AppDbContext _dbContext;

    public ContactsController(IContactService contactService, AppDbContext dbContext)
    {
        _contactService = contactService;
        _dbContext = dbContext;
    }

    [HttpGet]
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
    public async Task<ActionResult<ApiResponse<ContactResponse>>> GetById(int id)
    {
        var data = await _contactService.GetByIdAsync(id);
        return Ok(new ApiResponse<ContactResponse> { Success = true, Data = data });
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<ContactResponse>>> Create([FromBody] CreateContactRequest request)
    {
        var data = await _contactService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = data.Id }, new ApiResponse<ContactResponse> { Success = true, Data = data });
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<ContactResponse>>> Update(int id, [FromBody] UpdateContactRequest request)
    {
        var data = await _contactService.UpdateAsync(id, request);
        return Ok(new ApiResponse<ContactResponse> { Success = true, Data = data });
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse>> Delete(int id)
    {
        await _contactService.DeleteAsync(id);
        return Ok(new ApiResponse { Success = true, Message = "Contact deleted successfully." });
    }

    public class BulkDeleteRequest
    {
        public List<int> Ids { get; set; } = [];
    }

    [HttpPost("bulk-delete")]
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

        return Ok(new ApiResponse { Success = true, Message = "Contacts deleted successfully." });
    }

    [HttpPatch("{id}/toggle-active")]
    public async Task<ActionResult<ApiResponse<ContactResponse>>> ToggleActive(int id)
    {
        var data = await _contactService.ToggleActiveAsync(id);
        return Ok(new ApiResponse<ContactResponse> { Success = true, Data = data });
    }

    [HttpGet("statuses")]
    public IActionResult GetStatuses()
    {
        var list = new[]
        {
            new { id = "New", name = "New" },
            new { id = "InProgress", name = "In Progress" },
            new { id = "Contacted", name = "Contacted" },
            new { id = "Qualified", name = "Qualified" },
            new { id = "Closed", name = "Closed" }
        };
        return Ok(list);
    }

    [HttpGet("sources")]
    public IActionResult GetSources()
    {
        var list = new[]
        {
            new { id = "Facebook", name = "facebook" },
            new { id = "WhatsApp", name = "WhatsApp" },
            new { id = "Saas", name = "saas" }
        };
        return Ok(list);
    }

    [HttpGet("settings")]
    public IActionResult GetSettings()
    {
        var config = new { groupNotAssignedText = "Group not assigned" };
        return Ok(new ApiResponse<object> { Success = true, Data = config });
    }

    [HttpGet("assigned-users")]
    public IActionResult GetAssignedUsers()
    {
        var list = new[]
        {
            new { id = "superAdmin", name = "superAdmin" },
            new { id = "johnMicheal", name = "John Micheal" },
            new { id = "gunaratnam", name = "Gunaratnam" }
        };
        return Ok(list);
    }

    [HttpGet("types")]
    public IActionResult GetTypes()
    {
        var list = System.Enum.GetNames(typeof(ContactType))
            .Select(name => new { id = name, name = name.ToLower() })
            .ToArray();
        return Ok(list);
    }

    [HttpGet("languages")]
    public IActionResult GetLanguages()
    {
        var list = new[]
        {
            new { id = "en", name = "English" },
            new { id = "ms", name = "Malay" },
            new { id = "zh", name = "Chinese" }
        };
        return Ok(list);
    }

    [HttpGet("countries")]
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
    public IActionResult GetCsvSample()
    {
        var csvContent = "status_id,source_id,assigned_id,firstname,lastname,company,type,email,phone\n1,1,1,sample data,sample data,,lead,abc@gmail.com,+1 555 123 4567\n";
        var bytes = System.Text.Encoding.UTF8.GetBytes(csvContent);
        return File(bytes, "text/csv", "contacts_sample.csv");
    }

    [HttpPost("csv-import")]
    [Consumes("multipart/form-data")]
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

                if (!Enum.TryParse<ContactType>(typeVal, true, out var contactType))
                {
                    return BadRequest(new ApiResponse { Success = false, Message = "wrong format csv file" });
                }

                ContactStatus contactStatus = ContactStatus.New;
                if (int.TryParse(statusVal, out var statusInt))
                {
                    if (Enum.IsDefined(typeof(ContactStatus), statusInt))
                    {
                        contactStatus = (ContactStatus)statusInt;
                    }
                    else
                    {
                        return BadRequest(new ApiResponse { Success = false, Message = "wrong format csv file" });
                    }
                }
                else if (Enum.TryParse<ContactStatus>(statusVal, true, out var parsedStatus))
                {
                    contactStatus = parsedStatus;
                }
                else
                {
                    return BadRequest(new ApiResponse { Success = false, Message = "wrong format csv file" });
                }

                ContactSource contactSource = ContactSource.WhatsApp;
                if (int.TryParse(sourceVal, out var sourceInt))
                {
                    if (Enum.IsDefined(typeof(ContactSource), sourceInt))
                    {
                        contactSource = (ContactSource)sourceInt;
                    }
                    else
                    {
                        return BadRequest(new ApiResponse { Success = false, Message = "wrong format csv file" });
                    }
                }
                else if (Enum.TryParse<ContactSource>(sourceVal, true, out var parsedSource))
                {
                    contactSource = parsedSource;
                }
                else
                {
                    return BadRequest(new ApiResponse { Success = false, Message = "wrong format csv file" });
                }

                string? assignedTo = null;
                if (assignedVal == "1") assignedTo = "superAdmin";
                else if (assignedVal == "2") assignedTo = "johnMicheal";
                else if (assignedVal == "3") assignedTo = "gunaratnam";
                else if (!string.IsNullOrEmpty(assignedVal)) assignedTo = assignedVal;

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
            foreach (var contact in contactsToImport)
            {
                var exists = await _dbContext.Contacts.AnyAsync(c => c.Phone == contact.Phone);
                if (!exists)
                {
                    _dbContext.Contacts.Add(contact);
                    importCount++;
                }
            }

            await _dbContext.SaveChangesAsync();

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
}
