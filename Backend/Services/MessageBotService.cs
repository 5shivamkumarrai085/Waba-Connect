using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.MessageBot;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services;

public class MessageBotService : IMessageBotService
{
    private readonly AppDbContext _dbContext;

    public MessageBotService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResponse<MessageBotResponse>> GetPagedAsync(
        PagedRequest request,
        string? relationType,
        bool? isActive,
        int? connectionId = null)
    {
        var query = _dbContext.MessageBots.Include(b => b.Connection).AsQueryable();

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
                b.TriggerKeyword.ToLower().Contains(search)
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
            else if (propName == "type")
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

        return new PagedResponse<MessageBotResponse>
        {
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize,
            Items = items.Select(MapToResponse).ToList()
        };
    }

    public async Task<MessageBotResponse> GetByIdAsync(int id)
    {
        var bot = await _dbContext.MessageBots.Include(b => b.Connection).FirstOrDefaultAsync(b => b.Id == id);
        if (bot == null)
        {
            throw new KeyNotFoundException($"Message bot with ID {id} not found.");
        }
        return MapToResponse(bot);
    }

    public async Task<MessageBotResponse> CreateAsync(CreateMessageBotRequest request)
    {
        if (await _dbContext.MessageBots.AnyAsync(b => b.Name.ToLower() == request.Name.ToLower()))
        {
            throw new InvalidOperationException($"Bot Name '{request.Name}' already exists.");
        }

        if (await _dbContext.MessageBots.AnyAsync(b => b.TriggerKeyword.ToLower() == request.TriggerKeyword.ToLower()))
        {
            throw new InvalidOperationException($"Trigger Keyword '{request.TriggerKeyword}' already exists.");
        }

        var bot = new MessageBot
        {
            Name = request.Name,
            RelationType = request.RelationType,
            ReplyText = request.ReplyText,
            ReplyType = request.ReplyType,
            TriggerKeyword = request.TriggerKeyword,
            Header = request.Header,
            Footer = request.Footer,
            IsActive = request.IsActive,
            OptionType = request.OptionType,
            
            Button1 = request.Button1,
            Button1Id = request.Button1Id,
            Button2 = request.Button2,
            Button2Id = request.Button2Id,
            Button3 = request.Button3,
            Button3Id = request.Button3Id,
            
            CtaButtonName = request.CtaButtonName,
            CtaButtonLink = request.CtaButtonLink,
            
            FileType = request.FileType,
            FileName = request.FileName,
            FileUrl = request.FileUrl,
            
            AssistantName = request.AssistantName,
            ConnectionId = request.ConnectionId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.MessageBots.Add(bot);
        await _dbContext.SaveChangesAsync();

        return MapToResponse(bot);
    }

    public async Task<MessageBotResponse> UpdateAsync(int id, UpdateMessageBotRequest request)
    {
        var bot = await _dbContext.MessageBots.FindAsync(id);
        if (bot == null)
        {
            throw new KeyNotFoundException($"Message bot with ID {id} not found.");
        }

        if (bot.Name.ToLower() != request.Name.ToLower() &&
            await _dbContext.MessageBots.AnyAsync(b => b.Name.ToLower() == request.Name.ToLower()))
        {
            throw new InvalidOperationException($"Bot Name '{request.Name}' already exists.");
        }

        if (bot.TriggerKeyword.ToLower() != request.TriggerKeyword.ToLower() &&
            await _dbContext.MessageBots.AnyAsync(b => b.TriggerKeyword.ToLower() == request.TriggerKeyword.ToLower()))
        {
            throw new InvalidOperationException($"Trigger Keyword '{request.TriggerKeyword}' already exists.");
        }

        bot.Name = request.Name;
        bot.RelationType = request.RelationType;
        bot.ReplyText = request.ReplyText;
        bot.ReplyType = request.ReplyType;
        bot.TriggerKeyword = request.TriggerKeyword;
        bot.Header = request.Header;
        bot.Footer = request.Footer;
        bot.IsActive = request.IsActive;
        bot.OptionType = request.OptionType;
        
        // Options update (reset options not selected to preserve data integrity if needed, or simply assign all)
        bot.Button1 = request.Button1;
        bot.Button1Id = request.Button1Id;
        bot.Button2 = request.Button2;
        bot.Button2Id = request.Button2Id;
        bot.Button3 = request.Button3;
        bot.Button3Id = request.Button3Id;
        
        bot.CtaButtonName = request.CtaButtonName;
        bot.CtaButtonLink = request.CtaButtonLink;
        
        bot.FileType = request.FileType;
        bot.FileName = request.FileName;
        bot.FileUrl = request.FileUrl;
        
        bot.AssistantName = request.AssistantName;
        bot.ConnectionId = request.ConnectionId;
        bot.UpdatedAt = DateTime.UtcNow;

        _dbContext.MessageBots.Entry(bot).State = EntityState.Modified;
        await _dbContext.SaveChangesAsync();

        return MapToResponse(bot);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var bot = await _dbContext.MessageBots.FindAsync(id);
        if (bot == null)
        {
            return false;
        }

        _dbContext.MessageBots.Remove(bot);
        await _dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<MessageBotResponse> CloneAsync(int id)
    {
        var bot = await _dbContext.MessageBots.FindAsync(id);
        if (bot == null)
        {
            throw new KeyNotFoundException($"Message bot with ID {id} not found.");
        }

        // Clone name with index or just Clone text
        var baseName = bot.Name;
        var cloneName = baseName;
        
        // Find a unique name
        int idx = 1;
        while (await _dbContext.MessageBots.AnyAsync(b => b.Name == $"{baseName} (Copy {idx})"))
        {
            idx++;
        }
        cloneName = $"{baseName} (Copy {idx})";

        var clonedBot = new MessageBot
        {
            Name = cloneName,
            RelationType = bot.RelationType,
            ReplyText = bot.ReplyText,
            ReplyType = bot.ReplyType,
            TriggerKeyword = bot.TriggerKeyword,
            Header = bot.Header,
            Footer = bot.Footer,
            IsActive = bot.IsActive,
            OptionType = bot.OptionType,
            
            Button1 = bot.Button1,
            Button1Id = bot.Button1Id,
            Button2 = bot.Button2,
            Button2Id = bot.Button2Id,
            Button3 = bot.Button3,
            Button3Id = bot.Button3Id,
            
            CtaButtonName = bot.CtaButtonName,
            CtaButtonLink = bot.CtaButtonLink,
            
            FileType = bot.FileType,
            FileName = bot.FileName,
            FileUrl = bot.FileUrl,
            
            AssistantName = bot.AssistantName,
            ConnectionId = bot.ConnectionId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.MessageBots.Add(clonedBot);
        await _dbContext.SaveChangesAsync();

        return MapToResponse(clonedBot);
    }

    public async Task<MessageBotResponse> ToggleActiveAsync(int id)
    {
        var bot = await _dbContext.MessageBots.FindAsync(id);
        if (bot == null)
        {
            throw new KeyNotFoundException($"Message bot with ID {id} not found.");
        }

        bot.IsActive = !bot.IsActive;
        bot.UpdatedAt = DateTime.UtcNow;

        _dbContext.MessageBots.Entry(bot).State = EntityState.Modified;
        await _dbContext.SaveChangesAsync();

        return MapToResponse(bot);
    }

    private static MessageBotResponse MapToResponse(MessageBot bot)
    {
        return new MessageBotResponse
        {
            Id = bot.Id,
            Name = bot.Name,
            RelationType = bot.RelationType,
            ReplyText = bot.ReplyText,
            ReplyType = bot.ReplyType,
            TriggerKeyword = bot.TriggerKeyword,
            Header = bot.Header,
            Footer = bot.Footer,
            IsActive = bot.IsActive,
            OptionType = bot.OptionType,
            
            Button1 = bot.Button1,
            Button1Id = bot.Button1Id,
            Button2 = bot.Button2,
            Button2Id = bot.Button2Id,
            Button3 = bot.Button3,
            Button3Id = bot.Button3Id,
            
            CtaButtonName = bot.CtaButtonName,
            CtaButtonLink = bot.CtaButtonLink,
            
            FileType = bot.FileType,
            FileName = bot.FileName,
            FileUrl = bot.FileUrl,
            
            AssistantName = bot.AssistantName,
            ConnectionId = bot.ConnectionId,
            ConnectionName = bot.Connection?.Name,
            CreatedAt = bot.CreatedAt,
            UpdatedAt = bot.UpdatedAt
        };
    }
}
