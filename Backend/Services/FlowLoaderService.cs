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

    public FlowLoaderService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<BotFlow?> LoadFlowAsync(int flowId)
    {
        return await _dbContext.BotFlows.FirstOrDefaultAsync(f => f.Id == flowId);
    }

    public async Task<List<FlowNode>> GetNodesForFlowAsync(int flowId)
    {
        return await _dbContext.FlowNodes.Where(n => n.FlowId == flowId).ToListAsync();
    }

    public async Task<List<FlowEdge>> GetEdgesForFlowAsync(int flowId)
    {
        return await _dbContext.FlowEdges.Where(e => e.FlowId == flowId).ToListAsync();
    }
}
