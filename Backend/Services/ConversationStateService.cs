using System;
using System.Collections.Generic;
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
        return await _dbContext.ConversationStates
            .Include(s => s.Flow)
            .FirstOrDefaultAsync(s => s.PhoneNumber == phoneNumber && s.Status == "Active" && 
                (connectionId.HasValue ? s.ConnectionId == connectionId.Value : s.ConnectionId == null));
    }

    public async Task<ConversationState> CreateOrUpdateStateAsync(
        string phoneNumber,
        int flowId,
        string currentNodeId,
        Dictionary<string, string> variables,
        int? connectionId = null)
    {
        var existing = await _dbContext.ConversationStates
            .FirstOrDefaultAsync(s => s.PhoneNumber == phoneNumber && s.Status == "Active" && 
                (connectionId.HasValue ? s.ConnectionId == connectionId.Value : s.ConnectionId == null));

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
        var state = await _dbContext.ConversationStates
            .FirstOrDefaultAsync(s => s.PhoneNumber == phoneNumber && s.Status == "Active" && 
                (connectionId.HasValue ? s.ConnectionId == connectionId.Value : s.ConnectionId == null));

        if (state != null)
        {
            state.Status = "Completed";
            state.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
        }
    }
}
