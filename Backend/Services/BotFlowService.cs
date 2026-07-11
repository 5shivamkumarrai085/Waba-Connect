using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.BotFlow;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services;

public class BotFlowService : IBotFlowService
{
    private readonly AppDbContext _dbContext;

    public BotFlowService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResponse<BotFlowResponse>> GetPagedAsync(
        PagedRequest request, 
        bool? isActive)
    {
        var query = _dbContext.BotFlows.AsQueryable();

        if (isActive.HasValue)
        {
            query = query.Where(f => f.IsActive == isActive.Value);
        }

        if (!string.IsNullOrEmpty(request.Search))
        {
            var search = request.Search.ToLower();
            query = query.Where(f => 
                f.Name.ToLower().Contains(search) || 
                (f.Description != null && f.Description.ToLower().Contains(search))
            );
        }

        // Default sort descending by ID
        query = query.OrderByDescending(f => f.Id);

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync();

        return new PagedResponse<BotFlowResponse>
        {
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize,
            Items = items.Select(MapToResponse).ToList()
        };
    }

    public async Task<BotFlowResponse> GetByIdAsync(int id)
    {
        var flow = await _dbContext.BotFlows.FindAsync(id);
        if (flow == null)
        {
            throw new KeyNotFoundException($"Bot flow with ID {id} not found.");
        }
        return MapToResponse(flow);
    }

    public async Task<BotFlowResponse> CreateAsync(CreateBotFlowRequest request)
    {
        if (await _dbContext.BotFlows.AnyAsync(f => f.Name.ToLower() == request.Name.ToLower()))
        {
            throw new InvalidOperationException($"Bot Flow with Name '{request.Name}' already exists.");
        }

        var flow = new BotFlow
        {
            Name = request.Name,
            Description = request.Description,
            IsActive = request.IsActive,
            FlowData = request.FlowData ?? "{}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.BotFlows.Add(flow);
        await _dbContext.SaveChangesAsync();

        return MapToResponse(flow);
    }

    public async Task<BotFlowResponse> UpdateAsync(int id, UpdateBotFlowRequest request)
    {
        var flow = await _dbContext.BotFlows.FindAsync(id);
        if (flow == null)
        {
            throw new KeyNotFoundException($"Bot flow with ID {id} not found.");
        }

        if (flow.Name.ToLower() != request.Name.ToLower() &&
            await _dbContext.BotFlows.AnyAsync(f => f.Name.ToLower() == request.Name.ToLower()))
        {
            throw new InvalidOperationException($"Bot Flow with Name '{request.Name}' already exists.");
        }

        flow.Name = request.Name;
        flow.Description = request.Description;
        flow.IsActive = request.IsActive;
        if (request.FlowData != null)
        {
            flow.FlowData = request.FlowData;
        }
        flow.UpdatedAt = DateTime.UtcNow;

        _dbContext.BotFlows.Entry(flow).State = EntityState.Modified;
        await _dbContext.SaveChangesAsync();

        return MapToResponse(flow);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var flow = await _dbContext.BotFlows.FindAsync(id);
        if (flow == null)
        {
            return false;
        }

        _dbContext.BotFlows.Remove(flow);
        await _dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<BotFlowResponse> ToggleActiveAsync(int id)
    {
        var flow = await _dbContext.BotFlows.FindAsync(id);
        if (flow == null)
        {
            throw new KeyNotFoundException($"Bot flow with ID {id} not found.");
        }

        flow.IsActive = !flow.IsActive;
        flow.UpdatedAt = DateTime.UtcNow;

        _dbContext.BotFlows.Entry(flow).State = EntityState.Modified;
        await _dbContext.SaveChangesAsync();

        return MapToResponse(flow);
    }

    private static BotFlowResponse MapToResponse(BotFlow flow)
    {
        return new BotFlowResponse
        {
            Id = flow.Id,
            Name = flow.Name,
            Description = flow.Description,
            IsActive = flow.IsActive,
            FlowData = flow.FlowData,
            CreatedAt = flow.CreatedAt,
            UpdatedAt = flow.UpdatedAt
        };
    }
}
