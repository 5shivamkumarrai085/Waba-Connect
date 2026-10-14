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
    private readonly IAuditService _auditService;

    public BotFlowService(AppDbContext dbContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _auditService = auditService;
    }

    public async Task<PagedResponse<BotFlowResponse>> GetPagedAsync(
        PagedRequest request,
        bool? isActive,
        int? connectionId = null)
    {
        var query = _dbContext.BotFlows.Include(f => f.Connection).AsQueryable();

        if (isActive.HasValue)
        {
            query = query.Where(f => f.IsActive == isActive.Value);
        }

        if (connectionId.HasValue)
        {
            // A connection's flow list includes its own scoped flows plus global (unscoped) flows, since those still apply.
            query = query.Where(f => f.ConnectionId == null || f.ConnectionId == connectionId.Value);
        }

        if (!string.IsNullOrEmpty(request.Search))
        {
            var search = request.Search.ToLower();
            query = query.Where(f => 
                f.Name.ToLower().Contains(search) ||
                (f.Description != null && f.Description.ToLower().Contains(search)) ||
                // A flow is identified in the list by the connection it runs on as much
                // as by its name, so that is worth matching too.
                (f.Connection != null && f.Connection.Name.ToLower().Contains(search))
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
        var flow = await _dbContext.BotFlows.Include(f => f.Connection).FirstOrDefaultAsync(f => f.Id == id);
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

        ValidateFlowData(request.FlowData, request.IsActive);

        var flow = new BotFlow
        {
            Name = request.Name,
            Description = request.Description,
            IsActive = request.IsActive,
            FlowData = request.FlowData ?? "{}",
            ConnectionId = request.ConnectionId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.BotFlows.Add(flow);
        await _dbContext.SaveChangesAsync();

        await SyncNodesAndEdgesAsync(flow);

        // Audited after the node/edge sync so the entry means "the flow exists and is complete",
        // not "a row was inserted".
        await _auditService.LogAsync(
            "BotFlow.Created", "Data",
            $"Created bot flow \"{flow.Name}\".",
            "BotFlow", flow.Id.ToString());

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

        // Parse the canvas before touching anything, so a bad save is refused instead of leaving
        // the flow with no nodes (and so a trigger that silently never fires).
        ValidateFlowData(request.FlowData ?? flow.FlowData, request.IsActive ?? flow.IsActive);

        flow.Name = request.Name;
        flow.Description = request.Description;
        if (request.IsActive is { } isActive)
        {
            flow.IsActive = isActive;
        }
        if (request.FlowData != null)
        {
            flow.FlowData = request.FlowData;
        }
        if (request.ConnectionIdSpecified)
        {
            flow.ConnectionId = request.ConnectionId;
        }
        flow.UpdatedAt = DateTime.UtcNow;

        _dbContext.BotFlows.Entry(flow).State = EntityState.Modified;
        await _dbContext.SaveChangesAsync();

        await SyncNodesAndEdgesAsync(flow);

        await _auditService.LogAsync(
            "BotFlow.Updated", "Data",
            $"Updated bot flow \"{flow.Name}\".",
            "BotFlow", flow.Id.ToString());

        return MapToResponse(flow);
    }

    /// <summary>
    /// Refuses a canvas the engine could not run: unreadable JSON, nodes without an id or type, or
    /// an active flow whose Start Trigger has no keywords (it would never fire, silently). Drafts
    /// that are switched off may still be saved half-finished.
    /// </summary>
    private static void ValidateFlowData(string? flowData, bool willBeActive)
    {
        if (string.IsNullOrWhiteSpace(flowData) || flowData == "{}") return;

        System.Text.Json.JsonDocument doc;
        try
        {
            doc = System.Text.Json.JsonDocument.Parse(flowData);
        }
        catch (System.Text.Json.JsonException)
        {
            throw new ArgumentException("The flow could not be read. Reload the designer and try again.");
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != System.Text.Json.JsonValueKind.Array) return;

            foreach (var node in nodes.EnumerateArray())
            {
                var id = node.TryGetProperty("id", out var idProp) && idProp.ValueKind == System.Text.Json.JsonValueKind.String ? idProp.GetString() : null;
                var type = node.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == System.Text.Json.JsonValueKind.String ? typeProp.GetString() : null;
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(type))
                    throw new ArgumentException("Every step in the flow needs an id and a type. Reload the designer and try again.");

                if (willBeActive && type == "Start Trigger")
                {
                    var data = node.TryGetProperty("data", out var dataProp) ? dataProp.GetRawText() : "{}";
                    if (BotFlowTriggers.Parse(new FlowNode { NodeType = type, DataJson = data }) is null)
                        throw new ArgumentException("Add at least one trigger keyword to the Start Trigger before switching this flow on.");
                }
            }
        }
    }

    private async Task SyncNodesAndEdgesAsync(BotFlow flow)
    {
        var oldNodes = await _dbContext.FlowNodes.Where(n => n.FlowId == flow.Id).ToListAsync();
        _dbContext.FlowNodes.RemoveRange(oldNodes);

        var oldEdges = await _dbContext.FlowEdges.Where(e => e.FlowId == flow.Id).ToListAsync();
        _dbContext.FlowEdges.RemoveRange(oldEdges);

        await _dbContext.SaveChangesAsync();

        if (string.IsNullOrWhiteSpace(flow.FlowData) || flow.FlowData == "{}")
        {
            return;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(flow.FlowData);
            var root = doc.RootElement;

            if (root.TryGetProperty("nodes", out var nodesProp) && nodesProp.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var nodeElem in nodesProp.EnumerateArray())
                {
                    string nodeId = nodeElem.GetProperty("id").GetString() ?? "";
                    string nodeType = nodeElem.GetProperty("type").GetString() ?? "";
                    
                    double posX = 0;
                    double posY = 0;
                    if (nodeElem.TryGetProperty("position", out var posProp))
                    {
                        posX = posProp.TryGetProperty("x", out var xProp) ? xProp.GetDouble() : 0;
                        posY = posProp.TryGetProperty("y", out var yProp) ? yProp.GetDouble() : 0;
                    }
                    else
                    {
                        posX = nodeElem.TryGetProperty("x", out var xProp) ? xProp.GetDouble() : 0;
                        posY = nodeElem.TryGetProperty("y", out var yProp) ? yProp.GetDouble() : 0;
                    }

                    string dataJson = "{}";
                    if (nodeElem.TryGetProperty("data", out var dataProp))
                    {
                        dataJson = dataProp.GetRawText();
                    }

                    var dbNode = new FlowNode
                    {
                        FlowId = flow.Id,
                        NodeId = nodeId,
                        NodeType = nodeType,
                        PositionX = posX,
                        PositionY = posY,
                        DataJson = dataJson
                    };
                    _dbContext.FlowNodes.Add(dbNode);
                }
            }

            System.Text.Json.JsonElement edgesProp;
            bool hasEdges = root.TryGetProperty("edges", out edgesProp);
            if (!hasEdges)
            {
                hasEdges = root.TryGetProperty("connections", out edgesProp);
            }

            if (hasEdges && edgesProp.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var edgeElem in edgesProp.EnumerateArray())
                {
                    string source = string.Empty;
                    string target = string.Empty;
                    string? sourceHandle = null;

                    if (edgeElem.TryGetProperty("source", out var srcProp))
                    {
                        source = srcProp.GetString() ?? "";
                    }
                    else if (edgeElem.TryGetProperty("sourceId", out var srcIdProp))
                    {
                        source = srcIdProp.GetString() ?? "";
                    }

                    if (edgeElem.TryGetProperty("target", out var tgtProp))
                    {
                        target = tgtProp.GetString() ?? "";
                    }
                    else if (edgeElem.TryGetProperty("targetId", out var tgtIdProp))
                    {
                        target = tgtIdProp.GetString() ?? "";
                    }

                    if (edgeElem.TryGetProperty("sourceHandle", out var shProp))
                    {
                        sourceHandle = shProp.GetString();
                    }
                    else if (edgeElem.TryGetProperty("sourcePortId", out var spIdProp))
                    {
                        sourceHandle = spIdProp.GetString();
                    }

                    var dbEdge = new FlowEdge
                    {
                        FlowId = flow.Id,
                        Source = source,
                        Target = target,
                        SourceHandle = sourceHandle
                    };
                    _dbContext.FlowEdges.Add(dbEdge);
                }
            }

            await _dbContext.SaveChangesAsync();
        }
        catch (Exception)
        {
            // Ignore parse errors on save
        }
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var flow = await _dbContext.BotFlows.FindAsync(id);
        if (flow == null)
        {
            return false;
        }

        // Captured before Remove: after SaveChanges the entity is detached and Id reads 0.
        var flowName = flow.Name;
        var flowId = flow.Id;

        _dbContext.BotFlows.Remove(flow);
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            "BotFlow.Deleted", "Data",
            $"Deleted bot flow \"{flowName}\".",
            "BotFlow", flowId.ToString());

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

        await _auditService.LogAsync(
            "BotFlow.StatusChanged", "Data",
            $"Set bot flow \"{flow.Name}\" to {(flow.IsActive ? "active" : "inactive")}.",
            "BotFlow", flow.Id.ToString());

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
            ConnectionId = flow.ConnectionId,
            ConnectionName = flow.Connection?.Name,
            CreatedAt = flow.CreatedAt,
            UpdatedAt = flow.UpdatedAt
        };
    }
}
