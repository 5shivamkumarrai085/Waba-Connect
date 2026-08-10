using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.TemplateBot;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services;

public class TemplateBotService : ITemplateBotService
{
    private readonly AppDbContext _dbContext;
    private readonly IAuditService _auditService;

    public TemplateBotService(AppDbContext dbContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _auditService = auditService;
    }

    public async Task<PagedResponse<TemplateBotResponse>> GetPagedAsync(
        PagedRequest request,
        string? relationType,
        bool? isActive,
        int? connectionId = null)
    {
        var query = _dbContext.TemplateBots
            .Include(b => b.Template)
            .Include(b => b.Variables)
            .Include(b => b.Connection)
            .AsQueryable();

        // Filters
        if (!string.IsNullOrEmpty(relationType))
        {
            query = query.Where(b => b.RelationType == relationType);
        }

        if (isActive.HasValue)
        {
            query = query.Where(b => b.IsActive == isActive.Value);
        }

        if (connectionId.HasValue)
        {
            // A connection's bot list includes its own scoped bots plus global (unscoped) bots, since those still apply.
            query = query.Where(b => b.ConnectionId == null || b.ConnectionId == connectionId.Value);
        }

        // Search
        if (!string.IsNullOrEmpty(request.Search))
        {
            var search = request.Search.ToLower();
            query = query.Where(b => 
                b.Name.ToLower().Contains(search) || 
                b.TriggerKeyword.ToLower().Contains(search) ||
                b.Template.Name.ToLower().Contains(search)
            );
        }

        // Sorting
        if (!string.IsNullOrEmpty(request.SortBy))
        {
            var propName = request.SortBy.ToLower();
            if (propName == "name")
            {
                query = request.SortDescending ? query.OrderByDescending(b => b.Name) : query.OrderBy(b => b.Name);
            }
            else if (propName == "replytype")
            {
                query = request.SortDescending ? query.OrderByDescending(b => b.ReplyType) : query.OrderBy(b => b.ReplyType);
            }
            else if (propName == "triggerkeyword")
            {
                query = request.SortDescending ? query.OrderByDescending(b => b.TriggerKeyword) : query.OrderBy(b => b.TriggerKeyword);
            }
            else if (propName == "relationtype")
            {
                query = request.SortDescending ? query.OrderByDescending(b => b.RelationType) : query.OrderBy(b => b.RelationType);
            }
            else if (propName == "active")
            {
                query = request.SortDescending ? query.OrderByDescending(b => b.IsActive) : query.OrderBy(b => b.IsActive);
            }
            else if (propName == "createdat")
            {
                query = request.SortDescending ? query.OrderByDescending(b => b.CreatedAt) : query.OrderBy(b => b.CreatedAt);
            }
            else
            {
                query = request.SortDescending ? query.OrderByDescending(b => b.Id) : query.OrderBy(b => b.Id);
            }
        }
        else
        {
            query = query.OrderByDescending(b => b.Id);
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync();

        return new PagedResponse<TemplateBotResponse>
        {
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize,
            Items = items.Select(MapToResponse).ToList()
        };
    }

    public async Task<TemplateBotResponse> GetByIdAsync(int id)
    {
        var bot = await _dbContext.TemplateBots
            .Include(b => b.Template)
            .Include(b => b.Variables)
            .Include(b => b.Connection)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (bot == null)
        {
            throw new KeyNotFoundException($"Template bot with ID {id} not found.");
        }

        return MapToResponse(bot);
    }

    public async Task<TemplateBotResponse> CreateAsync(CreateTemplateBotRequest request)
    {
        if (await _dbContext.TemplateBots.AnyAsync(b => b.Name.ToLower() == request.Name.ToLower()))
        {
            throw new InvalidOperationException($"Bot Name '{request.Name}' already exists.");
        }

        if (await _dbContext.TemplateBots.AnyAsync(b => b.TriggerKeyword.ToLower() == request.TriggerKeyword.ToLower()))
        {
            throw new InvalidOperationException($"Trigger Keyword '{request.TriggerKeyword}' already exists.");
        }

        var template = await _dbContext.Templates.FindAsync(request.TemplateId);
        if (template == null)
        {
            throw new ArgumentException($"Template with ID {request.TemplateId} not found.");
        }

        var bot = new TemplateBot
        {
            Name = request.Name,
            RelationType = request.RelationType,
            TemplateId = request.TemplateId,
            ReplyType = request.ReplyType,
            TriggerKeyword = request.TriggerKeyword,
            IsActive = request.IsActive,
            ConnectionId = request.ConnectionId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        if (request.Variables != null)
        {
            foreach (var v in request.Variables)
            {
                bot.Variables.Add(new TemplateBotVariable
                {
                    VariableName = v.VariableName,
                    VariableValue = v.VariableValue,
                    MergeField = v.MergeField
                });
            }
        }

        _dbContext.TemplateBots.Add(bot);
        await _dbContext.SaveChangesAsync();

        // Audited after the save: AuditService shares this scoped DbContext, so logging first
        // would commit the half-built bot along with the audit row.
        await _auditService.LogAsync(
            "TemplateBot.Created", "Data",
            $"Created template bot \"{bot.Name}\".",
            "TemplateBot", bot.Id.ToString());

        // Reload to include navigation properties
        return await GetByIdAsync(bot.Id);
    }

    public async Task<TemplateBotResponse> UpdateAsync(int id, UpdateTemplateBotRequest request)
    {
        var bot = await _dbContext.TemplateBots
            .Include(b => b.Variables)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (bot == null)
        {
            throw new KeyNotFoundException($"Template bot with ID {id} not found.");
        }

        if (bot.Name.ToLower() != request.Name.ToLower() &&
            await _dbContext.TemplateBots.AnyAsync(b => b.Name.ToLower() == request.Name.ToLower()))
        {
            throw new InvalidOperationException($"Bot Name '{request.Name}' already exists.");
        }

        if (bot.TriggerKeyword.ToLower() != request.TriggerKeyword.ToLower() &&
            await _dbContext.TemplateBots.AnyAsync(b => b.TriggerKeyword.ToLower() == request.TriggerKeyword.ToLower()))
        {
            throw new InvalidOperationException($"Trigger Keyword '{request.TriggerKeyword}' already exists.");
        }

        var template = await _dbContext.Templates.FindAsync(request.TemplateId);
        if (template == null)
        {
            throw new ArgumentException($"Template with ID {request.TemplateId} not found.");
        }

        bot.Name = request.Name;
        bot.RelationType = request.RelationType;
        bot.TemplateId = request.TemplateId;
        bot.ReplyType = request.ReplyType;
        bot.TriggerKeyword = request.TriggerKeyword;
        bot.IsActive = request.IsActive;
        bot.ConnectionId = request.ConnectionId;
        bot.UpdatedAt = DateTime.UtcNow;

        // Clear existing variables
        _dbContext.TemplateBotVariables.RemoveRange(bot.Variables);
        bot.Variables.Clear();

        // Add updated variables
        if (request.Variables != null)
        {
            foreach (var v in request.Variables)
            {
                bot.Variables.Add(new TemplateBotVariable
                {
                    VariableName = v.VariableName,
                    VariableValue = v.VariableValue,
                    MergeField = v.MergeField
                });
            }
        }

        _dbContext.TemplateBots.Entry(bot).State = EntityState.Modified;
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            "TemplateBot.Updated", "Data",
            $"Updated template bot \"{bot.Name}\".",
            "TemplateBot", bot.Id.ToString());

        return await GetByIdAsync(bot.Id);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var bot = await _dbContext.TemplateBots.FindAsync(id);
        if (bot == null)
        {
            return false;
        }

        // Captured before Remove: after SaveChanges the entity is detached and Id reads 0.
        var botName = bot.Name;
        var botId = bot.Id;

        _dbContext.TemplateBots.Remove(bot);
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            "TemplateBot.Deleted", "Data",
            $"Deleted template bot \"{botName}\".",
            "TemplateBot", botId.ToString());

        return true;
    }

    public async Task<TemplateBotResponse> CloneAsync(int id)
    {
        var bot = await _dbContext.TemplateBots
            .Include(b => b.Variables)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (bot == null)
        {
            throw new KeyNotFoundException($"Template bot with ID {id} not found.");
        }

        var baseName = bot.Name;
        int idx = 1;
        while (await _dbContext.TemplateBots.AnyAsync(b => b.Name == $"{baseName} (Copy {idx})"))
        {
            idx++;
        }
        var cloneName = $"{baseName} (Copy {idx})";

        var clonedBot = new TemplateBot
        {
            Name = cloneName,
            RelationType = bot.RelationType,
            TemplateId = bot.TemplateId,
            ReplyType = bot.ReplyType,
            TriggerKeyword = bot.TriggerKeyword,
            IsActive = bot.IsActive,
            ConnectionId = bot.ConnectionId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        foreach (var v in bot.Variables)
        {
            clonedBot.Variables.Add(new TemplateBotVariable
            {
                VariableName = v.VariableName,
                VariableValue = v.VariableValue,
                MergeField = v.MergeField
            });
        }

        _dbContext.TemplateBots.Add(clonedBot);
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            "TemplateBot.Cloned", "Data",
            $"Cloned template bot \"{bot.Name}\" as \"{clonedBot.Name}\".",
            "TemplateBot", clonedBot.Id.ToString());

        return await GetByIdAsync(clonedBot.Id);
    }

    public async Task<TemplateBotResponse> ToggleActiveAsync(int id)
    {
        var bot = await _dbContext.TemplateBots.FindAsync(id);
        if (bot == null)
        {
            throw new KeyNotFoundException($"Template bot with ID {id} not found.");
        }

        bot.IsActive = !bot.IsActive;
        bot.UpdatedAt = DateTime.UtcNow;

        _dbContext.TemplateBots.Entry(bot).State = EntityState.Modified;
        await _dbContext.SaveChangesAsync();

        await _auditService.LogAsync(
            "TemplateBot.StatusChanged", "Data",
            $"Set template bot \"{bot.Name}\" to {(bot.IsActive ? "active" : "inactive")}.",
            "TemplateBot", bot.Id.ToString());

        return await GetByIdAsync(bot.Id);
    }

    private static TemplateBotResponse MapToResponse(TemplateBot bot)
    {
        return new TemplateBotResponse
        {
            Id = bot.Id,
            Name = bot.Name,
            RelationType = bot.RelationType,
            TemplateId = bot.TemplateId,
            TemplateName = bot.Template?.Name ?? string.Empty,
            ReplyType = bot.ReplyType,
            TriggerKeyword = bot.TriggerKeyword,
            IsActive = bot.IsActive,
            ConnectionId = bot.ConnectionId,
            ConnectionName = bot.Connection?.Name,
            CreatedAt = bot.CreatedAt,
            UpdatedAt = bot.UpdatedAt,
            Variables = bot.Variables.Select(v => new TemplateBotVariableDto
            {
                VariableName = v.VariableName,
                VariableValue = v.VariableValue,
                MergeField = v.MergeField
            }).ToList()
        };
    }

    public async Task<List<string>> CheckKeywordsAsync(string keywords, int ignoreTemplateBotId, int ignoreBotFlowId)
    {
        var list = (keywords ?? "")
            .Split(',')
            .Select(k => k.Trim().ToLower())
            .Where(k => !string.IsNullOrEmpty(k))
            .Distinct()
            .ToList();

        var warnings = new List<string>();

        if (!list.Any())
        {
            return warnings;
        }

        // 1. Check active Template Bots
        var activeTemplateBots = await _dbContext.TemplateBots
            .Where(b => b.IsActive && b.Id != ignoreTemplateBotId)
            .ToListAsync();

        foreach (var bot in activeTemplateBots)
        {
            var botKeywords = (bot.TriggerKeyword ?? "")
                .Split(',')
                .Select(k => k.Trim().ToLower())
                .ToList();

            foreach (var kw in list)
            {
                if (botKeywords.Contains(kw))
                {
                    warnings.Add($"Keyword '{kw}' is already used in Template Bot '{bot.Name}'.");
                }
            }
        }

        // 2. Check active Bot Flows
        var activeBotFlows = await _dbContext.BotFlows
            .Where(f => f.IsActive && f.Id != ignoreBotFlowId)
            .ToListAsync();

        foreach (var flow in activeBotFlows)
        {
            var triggerNode = await _dbContext.FlowNodes
                .FirstOrDefaultAsync(n => n.FlowId == flow.Id && n.NodeType == "Start Trigger");
            if (triggerNode == null) continue;

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(triggerNode.DataJson);
                if (doc.RootElement.TryGetProperty("keywords", out var kwProp))
                {
                    var flowKeywords = new List<string>();
                    if (kwProp.ValueKind == JsonValueKind.Array)
                    {
                        var parsed = JsonSerializer.Deserialize<List<string>>(kwProp.GetRawText());
                        if (parsed != null)
                        {
                            flowKeywords = parsed.Select(k => k.Trim().ToLower()).ToList();
                        }
                    }
                    else
                    {
                        flowKeywords = (kwProp.GetString() ?? "")
                            .Split(',')
                            .Select(k => k.Trim().ToLower())
                            .ToList();
                    }

                    foreach (var kw in list)
                    {
                        if (flowKeywords.Contains(kw))
                        {
                            warnings.Add($"Keyword '{kw}' is already used in Bot Flow '{flow.Name}'.");
                        }
                    }
                }
            }
            catch
            {
                // Ignore parsing errors of older/corrupt flows
            }
        }

        return warnings;
    }
}
