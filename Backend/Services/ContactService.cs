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

    public ContactService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
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
        DateTime? endDate = null)
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

        if (!string.IsNullOrEmpty(type) && Enum.TryParse<ContactType>(type, true, out var parsedType))
        {
            query = query.Where(c => c.Type == parsedType);
        }

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<ContactStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(c => c.Status == parsedStatus);
        }

        if (!string.IsNullOrEmpty(assignedTo))
        {
            query = query.Where(c => c.AssignedTo == assignedTo);
        }

        if (!string.IsNullOrEmpty(source) && Enum.TryParse<ContactSource>(source, true, out var parsedSource))
        {
            query = query.Where(c => c.Source == parsedSource);
        }

        if (groupId.HasValue)
        {
            query = query.Where(c => c.GroupMemberships.Any(gm => gm.GroupId == groupId.Value));
        }

        if (startDate.HasValue)
        {
            query = query.Where(c => c.CreatedAt >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(c => c.CreatedAt <= endDate.Value);
        }

        if (!string.IsNullOrEmpty(request.Search))
        {
            var search = request.Search.ToLower();
            query = query.Where(c => c.Name.ToLower().Contains(search) || c.Phone.Contains(search));
        }

        if (request.SortDescending)
        {
            query = request.SortBy?.ToLower() switch
            {
                "name" => query.OrderByDescending(c => c.Name),
                "createdat" => query.OrderByDescending(c => c.CreatedAt),
                _ => query.OrderByDescending(c => c.Id)
            };
        }
        else
        {
            query = request.SortBy?.ToLower() switch
            {
                "name" => query.OrderBy(c => c.Name),
                "createdat" => query.OrderBy(c => c.CreatedAt),
                _ => query.OrderBy(c => c.Id)
            };
        }

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
                existingContact.Type = Enum.Parse<ContactType>(request.Type, true);
                existingContact.Status = Enum.Parse<ContactStatus>(request.Status, true);
                existingContact.Source = Enum.Parse<ContactSource>(request.Source, true);
                existingContact.AssignedTo = request.AssignedTo;
                existingContact.Email = request.Email;
                existingContact.Company = request.Company;
                existingContact.Website = request.Website;
                existingContact.City = request.City;
                existingContact.State = request.State;
                existingContact.Country = request.Country;
                existingContact.ZipCode = request.ZipCode;
                existingContact.Address = request.Address;
                existingContact.Description = request.Description;
                existingContact.UpdatedAt = DateTime.UtcNow;

                // Clear and update group memberships
                var existingMemberships = await _dbContext.ContactGroupMembers
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
            Type = Enum.Parse<ContactType>(request.Type, true),
            Status = Enum.Parse<ContactStatus>(request.Status, true),
            Source = Enum.Parse<ContactSource>(request.Source, true),
            AssignedTo = request.AssignedTo,
            Email = request.Email,
            Company = request.Company,
            Website = request.Website,
            City = request.City,
            State = request.State,
            Country = request.Country,
            ZipCode = request.ZipCode,
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
        contact.Type = Enum.Parse<ContactType>(request.Type, true);
        contact.Status = Enum.Parse<ContactStatus>(request.Status, true);
        contact.Source = Enum.Parse<ContactSource>(request.Source, true);
        contact.AssignedTo = request.AssignedTo;
        contact.Email = request.Email;
        contact.Company = request.Company;
        contact.Website = request.Website;
        contact.City = request.City;
        contact.State = request.State;
        contact.Country = request.Country;
        contact.ZipCode = request.ZipCode;
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
    }

    public async Task<ContactResponse> ToggleActiveAsync(int id)
    {
        var contact = await _dbContext.Contacts.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id);
        if (contact == null)
            throw new KeyNotFoundException($"Contact with ID {id} not found.");

        contact.IsActive = !contact.IsActive;
        await _dbContext.SaveChangesAsync();

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
            ZipCode = c.ZipCode,
            Address = c.Address,
            Description = c.Description,
            Tags = c.Tags,
            IsActive = c.IsActive,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt,
            Groups = c.GroupMemberships.Select(gm => new ContactGroupBriefResponse
            {
                Id = gm.Group.Id,
                Name = gm.Group.Name
            }).ToList()
        };
    }
}
