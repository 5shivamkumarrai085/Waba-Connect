using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.Contacts;

namespace WhatsAppCampaignApi.Services.Interfaces;

/// <summary>
/// Service for managing contacts.
/// </summary>
public interface IContactService
{
    /// <summary>
    /// Gets a paginated list of contacts with optional filtering.
    /// </summary>
    Task<PagedResponse<ContactResponse>> GetAllAsync(
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
        string? groupName = null);

    /// <summary>
    /// Gets a single contact by ID.
    /// </summary>
    Task<ContactResponse> GetByIdAsync(int id);

    /// <summary>
    /// Creates a new contact.
    /// </summary>
    Task<ContactResponse> CreateAsync(CreateContactRequest request);

    /// <summary>
    /// Updates an existing contact.
    /// </summary>
    Task<ContactResponse> UpdateAsync(int id, UpdateContactRequest request);

    /// <summary>
    /// Soft-deletes a contact by setting IsActive to false.
    /// </summary>
    Task DeleteAsync(int id);

    /// <summary>Adds a note to a contact. Throws KeyNotFoundException for a missing or deleted contact.</summary>
    Task<ContactNote> AddNoteAsync(int contactId, string content);

    /// <summary>Deletes a contact's note. Returns false when it does not exist.</summary>
    Task<bool> DeleteNoteAsync(int contactId, int noteId);

    /// <summary>
    /// Toggles the active status of a contact.
    /// </summary>
    Task<ContactResponse> ToggleActiveAsync(int id);

    /// <summary>
    /// Gets contacts by a list of IDs.
    /// </summary>
    Task<List<ContactResponse>> GetByIdsAsync(List<int> ids);
}
