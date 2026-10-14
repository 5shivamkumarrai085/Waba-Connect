using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services;

public class ConversationStateService : IConversationStateService
{
    private readonly AppDbContext _dbContext;

    public ConversationStateService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ConversationState?> GetActiveStateAsync(string phoneNumber, int? connectionId = null)
    {
        // Newest first: if two builds or a race ever left two Active rows, the conversation follows
        // the latest one rather than whichever the database happens to return.
        return await _dbContext.ConversationStates
            .Include(s => s.Flow)
            .Where(s => s.PhoneNumber == phoneNumber && s.Status == "Active" &&
                (connectionId.HasValue ? s.ConnectionId == connectionId.Value : s.ConnectionId == null))
            .OrderByDescending(s => s.UpdatedAt).ThenByDescending(s => s.Id)
            .FirstOrDefaultAsync();
    }

    public async Task<ConversationState> CreateOrUpdateStateAsync(
        string phoneNumber,
        int flowId,
        string currentNodeId,
        Dictionary<string, string> variables,
        int? connectionId = null)
    {
        var existing = await _dbContext.ConversationStates
            .Where(s => s.PhoneNumber == phoneNumber && s.Status == "Active" &&
                (connectionId.HasValue ? s.ConnectionId == connectionId.Value : s.ConnectionId == null))
            .OrderByDescending(s => s.UpdatedAt).ThenByDescending(s => s.Id)
            .FirstOrDefaultAsync();

        // Another flow's session is closed rather than repointed: keeping the old FlowId with the
        // new flow's node id made the next message look for a node that does not exist.
        if (existing != null && existing.FlowId != flowId)
        {
            await DeleteStateAsync(phoneNumber, connectionId);
            existing = null;
        }

        if (existing != null)
        {
            if (connectionId.HasValue)
            {
                existing.ConnectionId = connectionId.Value;
            }
            existing.CurrentNodeId = currentNodeId;
            existing.UpdatedAt = DateTime.UtcNow;

            // Merge variables
            try
            {
                var currentVars = JsonSerializer.Deserialize<Dictionary<string, string>>(existing.VariablesJson) 
                                  ?? new Dictionary<string, string>();
                
                foreach (var kv in variables)
                {
                    currentVars[kv.Key] = kv.Value;
                }
                existing.VariablesJson = JsonSerializer.Serialize(currentVars);
            }
            catch
            {
                existing.VariablesJson = JsonSerializer.Serialize(variables);
            }

            await _dbContext.SaveChangesAsync();
            return existing;
        }

        var newState = new ConversationState
        {
            PhoneNumber = phoneNumber,
            FlowId = flowId,
            ConnectionId = connectionId,
            CurrentNodeId = currentNodeId,
            VariablesJson = JsonSerializer.Serialize(variables),
            Status = "Active",
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.ConversationStates.Add(newState);
        await _dbContext.SaveChangesAsync();
        return newState;
    }

    public async Task DeleteStateAsync(string phoneNumber, int? connectionId = null)
    {
        // Every Active row for this conversation, not just the first: a leftover duplicate would
        // otherwise surface on the next message as a menu the customer already left.
        var open = await _dbContext.ConversationStates
            .Where(s => s.PhoneNumber == phoneNumber && s.Status == "Active" &&
                (connectionId.HasValue ? s.ConnectionId == connectionId.Value : s.ConnectionId == null))
            .ToListAsync();
        if (open.Count == 0) return;

        foreach (var state in open)
        {
            state.Status = "Completed";
            state.UpdatedAt = DateTime.UtcNow;
        }
        await _dbContext.SaveChangesAsync();
    }

    public async Task<int> CloseAllForPhoneAsync(string phoneNumber)
    {
        var normalized = phoneNumber.Replace("+", string.Empty).Trim();
        return await _dbContext.ConversationStates
            .Where(s => s.PhoneNumber == normalized && s.Status == "Active")
            .ExecuteUpdateAsync(u => u
                .SetProperty(s => s.Status, "Completed")
                .SetProperty(s => s.UpdatedAt, DateTime.UtcNow));
    }
}
