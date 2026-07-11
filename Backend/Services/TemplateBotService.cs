using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

    public TemplateBotService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResponse<TemplateBotResponse>> GetPagedAsync(
        PagedRequest request, 
        string? relationType, 
        bool? isActive)
    {
        var query = _dbContext.TemplateBots
            .Include(b => b.Template)
            .Include(b => b.Variables)
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

        return await GetByIdAsync(bot.Id);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var bot = await _dbContext.TemplateBots.FindAsync(id);
        if (bot == null)
        {
            return false;
        }

        _dbContext.TemplateBots.Remove(bot);
        await _dbContext.SaveChangesAsync();
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
}
