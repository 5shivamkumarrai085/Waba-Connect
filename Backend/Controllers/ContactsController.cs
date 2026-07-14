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
}
