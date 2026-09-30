using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.Contacts;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Helpers;

namespace WhatsAppCampaignApi.Services;

public class ContactService : IContactService
{
    private readonly AppDbContext _dbContext;
    private readonly IAuditService _auditService;
    private readonly IChatService _chatConversationSeeder;

    private readonly Realtime.IDashboardNotifier? _dashboard;

    public ContactService(AppDbContext dbContext, IAuditService auditService, IChatService chatService, Realtime.IDashboardNotifier? dashboard = null)
    {
        _dashboard = dashboard;
        _dbContext = dbContext;
        _auditService = auditService;
        _chatConversationSeeder = chatService;
    }

    public async Task<PagedResponse<ContactResponse>> GetAllAsync(
        PagedRequest request, 
        string? type = null, 
        string? status = null, 
        bool? isActive = null,
        string? assignedTo = null,
        string? source = null,
        int? groupId = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? tag = null,
        string? groupName = null)
    {
        var query = _dbContext.Contacts
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(c => !c.IsDeleted)
            .Include(c => c.GroupMemberships)
                .ThenInclude(gm => gm.Group)
            .AsQueryable();

        if (isActive.HasValue)
        {
            query = query.Where(c => c.IsActive == isActive.Value);
        }

        // Type joins status and source as a plain string comparison. The Enum.TryParse guard
        // that used to wrap this would have silently ignored any type an administrator added.
        // A comma-separated list selects several types at once (the campaign wizard's audience).
        if (!string.IsNullOrEmpty(type) && !type.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            // Case-insensitive, as the list's filter has always behaved.
            var types = type.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => t.ToLower()).Distinct().ToList();
            query = types.Count == 1
                ? query.Where(c => c.Type.ToLower() == types[0])
                : query.Where(c => types.Contains(c.Type.ToLower()));
        }

        // Compared as plain strings now that statuses and sources are database-driven — an
        // Enum.TryParse here would silently drop any value an administrator added.
        if (!string.IsNullOrEmpty(status) && !status.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            // Case-insensitive, as the list's filter has always behaved.
            var statusLower = status.ToLower();
            query = query.Where(c => c.Status.ToLower() == statusLower);
        }

        if (!string.IsNullOrEmpty(assignedTo) && !assignedTo.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            query = assignedTo.Equals("Unassigned", StringComparison.OrdinalIgnoreCase)
                ? query.Where(c => c.AssignedTo == null || c.AssignedTo == "")
                : query.Where(c => c.AssignedTo == assignedTo);
        }

        if (!string.IsNullOrWhiteSpace(groupName) && !groupName.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(c => c.GroupMemberships.Any(gm => gm.Group != null && gm.Group.Name == groupName));
        }

        if (!string.IsNullOrWhiteSpace(tag) && !tag.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            // Tags are stored comma-separated: match a whole tag, not a substring of another.
            var needle = "," + tag.Trim().ToLower() + ",";
            query = query.Where(c => c.Tags != null && ("," + c.Tags.ToLower().Replace(", ", ",") + ",").Contains(needle));
        }

        if (!string.IsNullOrEmpty(source) && !source.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            // Case-insensitive, as the list's filter has always behaved.
            var sourceLower = source.ToLower();
            query = query.Where(c => c.Source.ToLower() == sourceLower);
        }

        if (groupId.HasValue)
        {
            query = query.Where(c => c.GroupMemberships.Any(gm => gm.GroupId == groupId.Value));
        }

        if (startDate.HasValue)
        {
            var from = DateTime.SpecifyKind(startDate.Value.Date, DateTimeKind.Utc);
            query = query.Where(c => c.CreatedAt >= from);
        }

        if (endDate.HasValue)
        {
            // The whole end day, not just its first instant.
            var toExclusive = DateTime.SpecifyKind(endDate.Value.Date.AddDays(1), DateTimeKind.Utc);
            query = query.Where(c => c.CreatedAt < toExclusive);
        }

        if (!string.IsNullOrEmpty(request.Search))
        {
            var search = request.Search.ToLower();

            // Every field the contact list can show. Searching a company, an owner, a status or a
            // source returned nothing before, even though all four are columns on the table.
            query = query.Where(c =>
                c.Name.ToLower().Contains(search) ||
                c.Phone.ToLower().Contains(search) ||
                (c.Email != null && c.Email.ToLower().Contains(search)) ||
                (c.Company != null && c.Company.ToLower().Contains(search)) ||
                c.Type.ToLower().Contains(search) ||
                c.Status.ToLower().Contains(search) ||
                c.Source.ToLower().Contains(search) ||
                (c.AssignedTo != null && c.AssignedTo.ToLower().Contains(search)) ||
                (c.Tags != null && c.Tags.ToLower().Contains(search)) ||
                (c.City != null && c.City.ToLower().Contains(search)) ||
                (c.State != null && c.State.ToLower().Contains(search)) ||
                (c.Country != null && c.Country.ToLower().Contains(search)));
        }

        var desc = request.SortDescending;
        query = (request.SortBy ?? string.Empty).ToLowerInvariant() switch
        {
            "name" => desc ? query.OrderByDescending(c => c.Name) : query.OrderBy(c => c.Name),
            "createdat" => desc ? query.OrderByDescending(c => c.CreatedAt) : query.OrderBy(c => c.CreatedAt),
            "type" => desc ? query.OrderByDescending(c => c.Type) : query.OrderBy(c => c.Type),
            "phone" => desc ? query.OrderByDescending(c => c.Phone) : query.OrderBy(c => c.Phone),
            "email" => desc ? query.OrderByDescending(c => c.Email) : query.OrderBy(c => c.Email),
            "status" => desc ? query.OrderByDescending(c => c.Status) : query.OrderBy(c => c.Status),
            "source" => desc ? query.OrderByDescending(c => c.Source) : query.OrderBy(c => c.Source),
            "assignedto" => desc ? query.OrderByDescending(c => c.AssignedTo) : query.OrderBy(c => c.AssignedTo),
            "company" => desc ? query.OrderByDescending(c => c.Company) : query.OrderBy(c => c.Company),
            _ => desc ? query.OrderByDescending(c => c.Id) : query.OrderBy(c => c.Id)
        };

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync();

        return new PagedResponse<ContactResponse>
        {
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize,
            Items = items.Select(MapToResponse).ToList()
        };
    }

    public async Task<ContactResponse> GetByIdAsync(int id)
    {
        var contact = await _dbContext.Contacts
            .IgnoreQueryFilters()
            .Include(c => c.GroupMemberships)
                .ThenInclude(gm => gm.Group)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (contact == null)
            throw new KeyNotFoundException($"Contact with ID {id} not found.");

        return MapToResponse(contact);
    }

    public async Task<List<ContactResponse>> GetByIdsAsync(List<int> ids)
    {
        var contacts = await _dbContext.Contacts
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(c => c.GroupMemberships)
                .ThenInclude(gm => gm.Group)
            .Where(c => ids.Contains(c.Id))
            .ToListAsync();

        return contacts.Select(MapToResponse).ToList();
    }

    public async Task<ContactResponse> CreateAsync(CreateContactRequest request)
    {
        var normalizedPhone = PhoneNumberHelper.NormalizePhoneNumber(request.Phone);
        var existingContact = await _dbContext.Contacts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Phone == normalizedPhone);

        if (existingContact != null)
        {
            if (existingContact.IsDeleted)
            {
                // Restore existing soft-deleted record
                existingContact.IsDeleted = false;
                existingContact.IsActive = true;
                existingContact.Name = request.Name;
                existingContact.Type = request.Type;
                existingContact.Status = request.Status;
                existingContact.Source = request.Source;
                existingContact.AssignedTo = request.AssignedTo;
                existingContact.Email = request.Email;
                existingContact.Company = request.Company;
                existingContact.Website = request.Website;
                existingContact.City = request.City;
                existingContact.State = request.State;
                existingContact.Country = request.Country;
                existingContact.TimeZone = NormalizeTimeZone(request.TimeZone);
                existingContact.ZipCode = request.ZipCode;
                existingContact.DateOfBirth = request.DateOfBirth;
                existingContact.Address = request.Address;
                existingContact.Description = request.Description;
                existingContact.UpdatedAt = DateTime.UtcNow;

                // Clear and update group memberships.
                //
                // IgnoreQueryFilters is required here: ContactGroupMember is now filtered on
                // !Contact.IsDeleted, and the restore above has only set that flag in memory —
                // the database row still says deleted until SaveChanges. Without this the query
                // returns nothing, the old memberships survive, and re-adding the same groups
                // violates the unique index.
                var existingMemberships = await _dbContext.ContactGroupMembers
                    .IgnoreQueryFilters()
                    .Where(gm => gm.ContactId == existingContact.Id)
                    .ToListAsync();
                _dbContext.ContactGroupMembers.RemoveRange(existingMemberships);
                existingContact.GroupMemberships.Clear();

                if (request.GroupIds != null && request.GroupIds.Any())
                {
                    foreach (var groupId in request.GroupIds)
                    {
                        var groupExists = await _dbContext.ContactGroups.AnyAsync(g => g.Id == groupId);
                        if (groupExists)
                        {
                            existingContact.GroupMemberships.Add(new ContactGroupMember { ContactId = existingContact.Id, GroupId = groupId });
                        }
                    }
                }

                await _dbContext.SaveChangesAsync();

                // A restored contact may have been created before its connections existed, or
                // had rows removed while deleted — top them up the same way a new one gets them.
                await _chatConversationSeeder.EnsureConversationsForContactAsync(existingContact.Id);

                _dashboard?.Changed();
                return await GetByIdAsync(existingContact.Id);
            }
            else
            {
                throw new InvalidOperationException("A contact with this phone number already exists.");
            }
        }

        var contact = new Contact
        {
            Name = request.Name,
            Phone = normalizedPhone,
            Type = request.Type,
            Status = request.Status,
            Source = request.Source,
            AssignedTo = request.AssignedTo,
            Email = request.Email,
            Company = request.Company,
            Website = request.Website,
            City = request.City,
            State = request.State,
            Country = request.Country,
            TimeZone = NormalizeTimeZone(request.TimeZone),
            ZipCode = request.ZipCode,
            DateOfBirth = request.DateOfBirth,
            Address = request.Address,
            Description = request.Description,
            IsDeleted = false,
            IsActive = true
        };

        if (request.GroupIds != null && request.GroupIds.Any())
        {
            foreach (var groupId in request.GroupIds)
            {
                var groupExists = await _dbContext.ContactGroups.AnyAsync(g => g.Id == groupId);
                if (groupExists)
                {
                    contact.GroupMemberships.Add(new ContactGroupMember { GroupId = groupId });
                }
            }
        }

        _dbContext.Contacts.Add(contact);
        await _dbContext.SaveChangesAsync();

        // Audited after the save, never before: AuditService shares this scoped DbContext, so
        // logging first would commit the half-built contact along with the audit row.
        await _auditService.LogAsync(
            "Contact.Created", "Data",
            $"Created contact \"{contact.Name}\" ({contact.Phone}).",
            "Contact", contact.Id.ToString());

        // Give the contact its conversation rows now, at write time. The chat list used to
        // build these on every read; doing it once here is what lets that read stay a read.
        await _chatConversationSeeder.EnsureConversationsForContactAsync(contact.Id);

        _dashboard?.Changed();
        return await GetByIdAsync(contact.Id);
    }

    public async Task<ContactResponse> UpdateAsync(int id, UpdateContactRequest request)
    {
        var contact = await _dbContext.Contacts
            .IgnoreQueryFilters()
            .Include(c => c.GroupMemberships)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (contact == null)
            throw new KeyNotFoundException($"Contact with ID {id} not found.");

        var normalizedPhone = PhoneNumberHelper.NormalizePhoneNumber(request.Phone);
        if (contact.Phone != normalizedPhone)
        {
            var existing = await _dbContext.Contacts.IgnoreQueryFilters().AnyAsync(c => c.Phone == normalizedPhone);
            if (existing)
                throw new InvalidOperationException("Another contact with this phone number already exists.");
        }

        contact.Name = request.Name;
        contact.Phone = normalizedPhone;
        contact.Type = request.Type;
        contact.Status = request.Status;
        contact.Source = request.Source;
        contact.AssignedTo = request.AssignedTo;
        contact.Email = request.Email;
        contact.Company = request.Company;
        contact.Website = request.Website;
        contact.City = request.City;
        contact.State = request.State;
        contact.Country = request.Country;
        contact.TimeZone = NormalizeTimeZone(request.TimeZone);
        contact.ZipCode = request.ZipCode;
        contact.DateOfBirth = request.DateOfBirth;
        contact.Address = request.Address;
        contact.Description = request.Description;
        contact.UpdatedAt = DateTime.UtcNow;

        if (request.GroupIds != null)
        {
            var existingMemberships = await _dbContext.ContactGroupMembers.Where(gm => gm.ContactId == contact.Id).ToListAsync();
            _dbContext.ContactGroupMembers.RemoveRange(existingMemberships);
            contact.GroupMemberships.Clear();
            
            foreach (var groupId in request.GroupIds)
            {
                var groupExists = await _dbContext.ContactGroups.AnyAsync(g => g.Id == groupId);
                if (groupExists)
                {
                    contact.GroupMemberships.Add(new ContactGroupMember { ContactId = contact.Id, GroupId = groupId });
                }
            }
        }

        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            "Contact.Updated", "Data",
            $"Updated contact \"{contact.Name}\" ({contact.Phone}).",
            "Contact", contact.Id.ToString());

        return await GetByIdAsync(contact.Id);
    }

    public async Task DeleteAsync(int id)
    {
        var contact = await _dbContext.Contacts.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id);
        if (contact == null)
            throw new KeyNotFoundException($"Contact with ID {id} not found.");

        contact.IsDeleted = true;
        contact.IsActive = false;
        contact.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            "Contact.Deleted", "Data",
            $"Deleted contact \"{contact.Name}\" ({contact.Phone}).",
            "Contact", contact.Id.ToString());
        _dashboard?.Changed();
    }

    public async Task<ContactResponse> ToggleActiveAsync(int id)
    {
        var contact = await _dbContext.Contacts.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id);
        if (contact == null)
            throw new KeyNotFoundException($"Contact with ID {id} not found.");

        contact.IsActive = !contact.IsActive;
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            "Contact.StatusChanged", "Data",
            $"Set contact \"{contact.Name}\" to {(contact.IsActive ? "active" : "inactive")}.",
            "Contact", contact.Id.ToString());

        _dashboard?.Changed();
        return await GetByIdAsync(contact.Id);
    }

    private static ContactResponse MapToResponse(Contact c)
    {
        return new ContactResponse
        {
            Id = c.Id,
            Name = c.Name,
            Phone = c.Phone,
            Type = c.Type.ToString(),
            Status = c.Status.ToString(),
            Source = c.Source.ToString(),
            AssignedTo = c.AssignedTo,
            Email = c.Email,
            Company = c.Company,
            Website = c.Website,
            City = c.City,
            State = c.State,
            Country = c.Country,
            TimeZone = c.TimeZone,
            AdSourceId = c.AdSourceId,
            AdSourceUrl = c.AdSourceUrl,
            AdHeadline = c.AdHeadline,
            AdAttributedAt = c.AdAttributedAt,
            ZipCode = c.ZipCode,
            DateOfBirth = c.DateOfBirth,
            Age = c.DateOfBirth is { } dob ? Catalogs.ContactFieldCatalog.AgeOn(dob, DateOnly.FromDateTime(DateTime.UtcNow)) : null,
            Address = c.Address,
            Description = c.Description,
            Tags = c.Tags,
            IsActive = c.IsActive,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt,
            Groups = c.GroupMemberships.Select(gm => new ContactGroupBriefResponse
            {
                Id = gm.Group.Id,
                Name = gm.Group.Name,
                Color = gm.Group.Color
            }).ToList()
        };
    }

    /// <summary>A known time zone id, or null. An unknown value is rejected rather than stored and ignored.</summary>
    private static string? NormalizeTimeZone(string? timeZone)
    {
        if (string.IsNullOrWhiteSpace(timeZone)) return null;
        var id = timeZone.Trim();
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(id, out _))
            throw new ArgumentException($"'{id}' is not a recognised time zone. Use an IANA name such as Asia/Kolkata.");
        return id;
    }
}
