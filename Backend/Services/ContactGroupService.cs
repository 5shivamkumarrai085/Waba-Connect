using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.Groups;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services;

public class ContactGroupService : IContactGroupService
{
    private readonly AppDbContext _dbContext;

    public ContactGroupService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResponse<GroupResponse>> GetAllAsync(PagedRequest request)
    {
        var query = _dbContext.ContactGroups.Include(g => g.Members).AsQueryable();

        if (!string.IsNullOrEmpty(request.Search))
        {
            var search = request.Search.ToLower();
            query = query.Where(g => g.Name.ToLower().Contains(search));
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(g => g.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync();

        return new PagedResponse<GroupResponse>
        {
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize,
            Items = items.Select(MapToResponse).ToList()
        };
    }

    public async Task<GroupResponse> GetByIdAsync(int id)
    {
        var group = await _dbContext.ContactGroups
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (group == null)
            throw new KeyNotFoundException($"Group with ID {id} not found.");

        return MapToResponse(group);
    }

    public async Task<GroupResponse> CreateAsync(CreateGroupRequest request)
    {
        var group = new ContactGroup
        {
            Name = request.Name,
            Description = request.Description
        };

        _dbContext.ContactGroups.Add(group);
        await _dbContext.SaveChangesAsync();

        return MapToResponse(group);
    }

    public async Task<GroupResponse> UpdateAsync(int id, UpdateGroupRequest request)
    {
        var group = await _dbContext.ContactGroups.FindAsync(id);
        if (group == null)
            throw new KeyNotFoundException($"Group with ID {id} not found.");

        group.Name = request.Name;
        group.Description = request.Description;

        await _dbContext.SaveChangesAsync();

        return MapToResponse(group);
    }

    public async Task DeleteAsync(int id)
    {
        var group = await _dbContext.ContactGroups.FindAsync(id);
        if (group == null)
            throw new KeyNotFoundException($"Group with ID {id} not found.");

        _dbContext.ContactGroups.Remove(group);
        await _dbContext.SaveChangesAsync();
    }

    public async Task AddMembersAsync(int groupId, GroupMembersRequest request)
    {
        var group = await _dbContext.ContactGroups.FindAsync(groupId);
        if (group == null) throw new KeyNotFoundException($"Group with ID {groupId} not found.");

        foreach (var contactId in request.ContactIds)
        {
            var existing = await _dbContext.ContactGroupMembers
                .AnyAsync(m => m.GroupId == groupId && m.ContactId == contactId);
                
            if (!existing)
            {
                var contactExists = await _dbContext.Contacts.AnyAsync(c => c.Id == contactId);
                if (contactExists)
                {
                    _dbContext.ContactGroupMembers.Add(new ContactGroupMember 
                    { 
                        GroupId = groupId, 
                        ContactId = contactId 
                    });
                }
            }
        }
        
        await _dbContext.SaveChangesAsync();
    }

    public async Task RemoveMembersAsync(int groupId, GroupMembersRequest request)
    {
        var members = await _dbContext.ContactGroupMembers
            .Where(m => m.GroupId == groupId && request.ContactIds.Contains(m.ContactId))
            .ToListAsync();
            
        _dbContext.ContactGroupMembers.RemoveRange(members);
        await _dbContext.SaveChangesAsync();
    }

    private static GroupResponse MapToResponse(ContactGroup g)
    {
        return new GroupResponse
        {
            Id = g.Id,
            Name = g.Name,
            Description = g.Description,
            MemberCount = g.Members?.Count ?? 0,
            CreatedAt = g.CreatedAt
        };
    }
}
