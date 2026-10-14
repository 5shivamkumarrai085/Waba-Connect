using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services;

public class FlowLoaderService : IFlowLoaderService
{
    private readonly AppDbContext _dbContext;
    private readonly HotPathCache _cache;

    public FlowLoaderService(AppDbContext dbContext, HotPathCache cache)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    public async Task<BotFlow?> LoadFlowAsync(int flowId)
    {
        return await _dbContext.BotFlows.FirstOrDefaultAsync(f => f.Id == flowId);
    }

    // Read on every step of every conversation, changed only when a flow is saved: cached
    // (detached, read-only) and cleared by the save. See HotPathCache.
    public Task<List<FlowNode>> GetNodesForFlowAsync(int flowId) =>
        _cache.GetOrLoadAsync(HotPathCache.BotFlows, $"nodes:{flowId}",
            () => _dbContext.FlowNodes.AsNoTracking().Where(n => n.FlowId == flowId).ToListAsync());

    public Task<List<FlowEdge>> GetEdgesForFlowAsync(int flowId) =>
        _cache.GetOrLoadAsync(HotPathCache.BotFlows, $"edges:{flowId}",
            () => _dbContext.FlowEdges.AsNoTracking().Where(e => e.FlowId == flowId).ToListAsync());
}
