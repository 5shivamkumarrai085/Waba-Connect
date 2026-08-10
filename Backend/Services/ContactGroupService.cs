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
    private readonly IAuditService _auditService;

    public ContactGroupService(AppDbContext dbContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _auditService = auditService;
    }

    public async Task<PagedResponse<GroupResponse>> GetAllAsync(PagedRequest request)
    {
        var query = _dbContext.ContactGroups.AsNoTracking().Include(g => g.Members).AsQueryable();

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
            .AsNoTracking()
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
            Description = request.Description,
            Color = request.Color
        };

        _dbContext.ContactGroups.Add(group);
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            "ContactGroup.Created", "Data",
            $"Created contact group \"{group.Name}\".",
            "ContactGroup", group.Id.ToString());

        return MapToResponse(group);
    }

    public async Task<GroupResponse> UpdateAsync(int id, UpdateGroupRequest request)
    {
        var group = await _dbContext.ContactGroups.FindAsync(id);
        if (group == null)
            throw new KeyNotFoundException($"Group with ID {id} not found.");

        var previousName = group.Name;
        group.Name = request.Name;
        group.Description = request.Description;
        group.Color = request.Color;

        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            "ContactGroup.Updated", "Data",
            previousName == group.Name
                ? $"Updated contact group \"{group.Name}\"."
                : $"Renamed contact group \"{previousName}\" to \"{group.Name}\".",
            "ContactGroup", group.Id.ToString());

        return MapToResponse(group);
    }

    public async Task DeleteAsync(int id)
    {
        var group = await _dbContext.ContactGroups.FindAsync(id);
        if (group == null)
            throw new KeyNotFoundException($"Group with ID {id} not found.");

        // Captured before Remove: after SaveChanges the entity is detached and Id reads 0.
        var groupName = group.Name;
        var groupId = group.Id;

        _dbContext.ContactGroups.Remove(group);
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            "ContactGroup.Deleted", "Data",
            $"Deleted contact group \"{groupName}\".",
            "ContactGroup", groupId.ToString());
    }

    public async Task AddMembersAsync(int groupId, GroupMembersRequest request)
    {
        var group = await _dbContext.ContactGroups.FindAsync(groupId);
        if (group == null) throw new KeyNotFoundException($"Group with ID {groupId} not found.");

        var addedCount = 0;

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
                    addedCount++;
                }
            }
        }

        await _dbContext.SaveChangesAsync();

        // Reports what actually changed, not what was asked for — contacts already in the group
        // and ids that don't exist are both skipped above.
        await _auditService.LogAsync(
            "ContactGroup.MembersAdded", "Data",
            $"Added {addedCount} contact(s) to group \"{group.Name}\".",
            "ContactGroup", groupId.ToString());
    }

    public async Task RemoveMembersAsync(int groupId, GroupMembersRequest request)
    {
        var members = await _dbContext.ContactGroupMembers
            .Where(m => m.GroupId == groupId && request.ContactIds.Contains(m.ContactId))
            .ToListAsync();

        var removedCount = members.Count;

        _dbContext.ContactGroupMembers.RemoveRange(members);
        await _dbContext.SaveChangesAsync();

        var groupName = await _dbContext.ContactGroups
            .Where(g => g.Id == groupId)
            .Select(g => g.Name)
            .FirstOrDefaultAsync() ?? $"#{groupId}";

        await _auditService.LogAsync(
            "ContactGroup.MembersRemoved", "Data",
            $"Removed {removedCount} contact(s) from group \"{groupName}\".",
            "ContactGroup", groupId.ToString());
    }

    private static GroupResponse MapToResponse(ContactGroup g)
    {
        return new GroupResponse
        {
            Id = g.Id,
            Name = g.Name,
            Description = g.Description,
            Color = g.Color,
            MemberCount = g.Members?.Count ?? 0,
            CreatedAt = g.CreatedAt
        };
    }
}
