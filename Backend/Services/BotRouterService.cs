using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Executors;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services;

public class BotRouterService : IBotRouterService
{
    private readonly AppDbContext _dbContext;
    private readonly IFlowExecutionService _flowExecutionService;
    private readonly IWhatsAppService _whatsAppService;
    private readonly IAiProvider _groqProvider;
    private readonly IEncryptionService _encryptionService;
    private readonly MessageBotExecutor _messageBotExecutor;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BotRouterService> _logger;

    public BotRouterService(
        AppDbContext dbContext,
        IFlowExecutionService flowExecutionService,
        IWhatsAppService whatsAppService,
        IAiProvider groqProvider,
        IEncryptionService encryptionService,
        MessageBotExecutor messageBotExecutor,
        IConfiguration configuration,
        ILogger<BotRouterService> logger)
    {
        _dbContext = dbContext;
        _flowExecutionService = flowExecutionService;
        _whatsAppService = whatsAppService;
        _groqProvider = groqProvider;
        _encryptionService = encryptionService;
        _messageBotExecutor = messageBotExecutor;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<bool> RouteMessageAsync(string phoneNumber, string incomingMessage, Contact contact)
    {
        string normalizedPhone = phoneNumber.Replace("+", "").Trim();
        string cleanMessage = incomingMessage.Trim();

        _logger.LogInformation("Routing incoming message from {Phone}: '{Message}'", normalizedPhone, cleanMessage);

        // 1. Stop Check
        var activeSession = await _dbContext.AiSessions
            .Include(s => s.MessageBot)
            .FirstOrDefaultAsync(s => s.PhoneNumber == normalizedPhone && s.IsActive);

        var botFlowState = await _dbContext.ConversationStates
            .FirstOrDefaultAsync(s => s.PhoneNumber == normalizedPhone);

        bool isBotFlowAiStop = false;
        if (botFlowState != null)
        {
            var currentNode = await _dbContext.FlowNodes
                .FirstOrDefaultAsync(n => n.FlowId == botFlowState.FlowId && n.NodeId == botFlowState.CurrentNodeId);
            
            if (currentNode != null && string.Equals(currentNode.NodeType, "AI Personal Assistant", StringComparison.OrdinalIgnoreCase))
            {
                var stopKeyword = "stop"; // Default for bot flow AI
                if (string.Equals(cleanMessage, stopKeyword, StringComparison.OrdinalIgnoreCase))
                {
                    isBotFlowAiStop = true;
                }
            }
        }

        if (activeSession != null || isBotFlowAiStop)
        {
            string stopKeyword = "stop";
            if (activeSession != null)
            {
                var assistantName = activeSession.AssistantName;
                stopKeyword = _configuration[$"PersonalAssistants:{assistantName}:StopKeyword"] ?? "stop";
            }

            if (string.Equals(cleanMessage, stopKeyword, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Stop keyword matched. Terminating AI session/flow for phone {Phone}", normalizedPhone);
                
                if (activeSession != null)
                {
                    activeSession.IsActive = false;
                    activeSession.UpdatedAt = DateTime.UtcNow;
                }
                else if (isBotFlowAiStop)
                {
                    // Even if there was no active Message Bot session, if they stopped from a Bot Flow AI,
                    // we should record an inactive session so that future AI nodes know the AI is stopped.
                    var existingSession = await _dbContext.AiSessions
                        .FirstOrDefaultAsync(s => s.PhoneNumber == normalizedPhone);
                    if (existingSession != null)
                    {
                        existingSession.IsActive = false;
                        existingSession.UpdatedAt = DateTime.UtcNow;
                    }
                    else
                    {
                        var firstBot = await _dbContext.MessageBots.FirstOrDefaultAsync();
                        if (firstBot != null)
                        {
                            var newInactiveSession = new AiSession
                            {
                                PhoneNumber = normalizedPhone,
                                MessageBotId = firstBot.Id,
                                IsActive = false,
                                CreatedAt = DateTime.UtcNow,
                                UpdatedAt = DateTime.UtcNow,
                                AssistantName = firstBot.AssistantName ?? "OmniBot"
                            };
                            _dbContext.AiSessions.Add(newInactiveSession);
                        }
                        else
                        {
                            _logger.LogWarning("No MessageBot found in the database. Cannot create inactive AiSession.");
                        }
                    }
                }
                
                if (botFlowState != null)
                {
                    _dbContext.ConversationStates.Remove(botFlowState);
                }

                await _dbContext.SaveChangesAsync();

                string stopConfirmation = "AI Personal Assistant stopped.";
                var sendResult = await _whatsAppService.SendTextMessageAsync(normalizedPhone, stopConfirmation);

                await LogOutgoingMessageAsync(contact, sendResult.MessageId, stopConfirmation);
                return true;
            }
        }

        // 2. Existing AI Session Check
        if (activeSession != null)
        {
            _logger.LogInformation("Active AI session exists for phone {Phone}. Routing directly to Groq assistant: '{AssistantName}'", normalizedPhone, activeSession.AssistantName);
            await ProcessAiAssistantRequestAsync(contact, normalizedPhone, cleanMessage, activeSession);
            return true;
        }

        // 3. Bot Flow Fallback
        _logger.LogDebug("Evaluating Bot Flow triggers for phone {Phone}", normalizedPhone);
        bool triggeredBotFlow = await _flowExecutionService.ExecuteFlowStepAsync(normalizedPhone, cleanMessage);
        if (triggeredBotFlow)
        {
            _logger.LogInformation("Message handled by Bot Flow engine for phone {Phone}", normalizedPhone);
            return true;
        }

        // 4. Template Bot Fallback
        _logger.LogDebug("Evaluating Template Bot triggers for phone {Phone}", normalizedPhone);
        bool triggeredTemplate = await _whatsAppService.TryTriggerTemplateBotAsync(normalizedPhone, cleanMessage, contact);
        if (triggeredTemplate)
        {
            _logger.LogInformation("Message matched and handled by Template Bot for phone {Phone}", normalizedPhone);
            return true;
        }

        // 5. Message Bot Keyword Matching
        _logger.LogDebug("Evaluating Message Bot triggers for phone {Phone}", normalizedPhone);
        var activeBots = await _dbContext.MessageBots
            .Where(b => b.IsActive)
            .ToListAsync();

        MessageBot? matchedBot = null;

        foreach (var bot in activeBots)
        {
            var keywords = bot.TriggerKeyword.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            bool isMatch = false;

            foreach (var kw in keywords)
            {
                if (string.Equals(bot.ReplyType, "Contains", StringComparison.OrdinalIgnoreCase))
                {
                    if (cleanMessage.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    {
                        isMatch = true;
                        break;
                    }
                }
                else // Default to On Exact Match
                {
                    if (string.Equals(cleanMessage, kw, StringComparison.OrdinalIgnoreCase))
                    {
                        isMatch = true;
                        break;
                    }
                }
            }

            if (isMatch)
            {
                matchedBot = bot;
                break;
            }
        }

        if (matchedBot != null)
        {
            _logger.LogInformation("Matched Message Bot '{BotName}' for trigger keyword in message '{Message}'", matchedBot.Name, cleanMessage);

            if (string.Equals(matchedBot.OptionType, "PersonalAssistant", StringComparison.OrdinalIgnoreCase))
            {
                bool hasAssistant = !string.IsNullOrWhiteSpace(matchedBot.AssistantName) &&
                                    !matchedBot.AssistantName.Equals("select option", StringComparison.OrdinalIgnoreCase) &&
                                    !matchedBot.AssistantName.Equals("select assistant", StringComparison.OrdinalIgnoreCase);

                if (hasAssistant)
                {
                    string assistantName = matchedBot.AssistantName;
                    
                    // Start a new AI session
                    var newSession = new AiSession
                    {
                        PhoneNumber = normalizedPhone,
                        MessageBotId = matchedBot.Id,
                        AssistantName = assistantName,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    _dbContext.AiSessions.Add(newSession);
                    await _dbContext.SaveChangesAsync();

                    _logger.LogInformation("Created new AI session for phone {Phone} targeting assistant '{AssistantName}'", normalizedPhone, assistantName);
                    await ProcessAiAssistantRequestAsync(contact, normalizedPhone, cleanMessage, newSession);
                }
                else
                {
                    // Stop any active AI sessions for this phone number
                    var activeSessions = await _dbContext.AiSessions
                        .Where(s => s.PhoneNumber == normalizedPhone && s.IsActive)
                        .ToListAsync();
                    foreach (var s in activeSessions)
                    {
                        s.IsActive = false;
                        s.UpdatedAt = DateTime.UtcNow;
                    }
                    if (activeSessions.Count > 0)
                    {
                        await _dbContext.SaveChangesAsync();
                        _logger.LogInformation("Deactivated active AI sessions for phone {Phone} because personal assistant was unselected.", normalizedPhone);
                    }

                    // Treat as static reply fallback
                    var sendResult = await _messageBotExecutor.ExecuteReplyAsync(matchedBot, normalizedPhone);
                    await LogOutgoingMessageAsync(contact, sendResult.MessageId, matchedBot.ReplyText);
                }
            }
            else
            {
                // Predefined static/buttons/media reply
                var sendResult = await _messageBotExecutor.ExecuteReplyAsync(matchedBot, normalizedPhone);
                string textLog = matchedBot.ReplyText;

                if (string.Equals(matchedBot.OptionType, "ReplyButtons", StringComparison.OrdinalIgnoreCase))
                {
                    var btns = new List<string>();
                    if (!string.IsNullOrWhiteSpace(matchedBot.Button1)) btns.Add(matchedBot.Button1);
                    if (!string.IsNullOrWhiteSpace(matchedBot.Button2)) btns.Add(matchedBot.Button2);
                    if (!string.IsNullOrWhiteSpace(matchedBot.Button3)) btns.Add(matchedBot.Button3);
                    
                    if (btns.Count > 0)
                    {
                        textLog += $"\n[Buttons: {string.Join(", ", btns)}]";
                    }
                }
                else if (string.Equals(matchedBot.OptionType, "CtaUrl", StringComparison.OrdinalIgnoreCase))
                {
                    textLog += $"\n[Link: {matchedBot.CtaButtonName} - {matchedBot.CtaButtonLink}]";
                }

                await LogOutgoingMessageAsync(
                    contact, 
                    sendResult.MessageId, 
                    textLog, 
                    matchedBot.OptionType.Equals("Files", StringComparison.OrdinalIgnoreCase) ? matchedBot.FileUrl : null,
                    matchedBot.OptionType.Equals("Files", StringComparison.OrdinalIgnoreCase) ? matchedBot.FileType : null,
                    matchedBot.OptionType.Equals("Files", StringComparison.OrdinalIgnoreCase) ? matchedBot.FileName : null);
            }

            return true;
        }

        _logger.LogInformation("No active bot module triggered for phone {Phone}", normalizedPhone);
        return false;
    }

    private async Task ProcessAiAssistantRequestAsync(Contact contact, string normalizedPhone, string incomingMessage, AiSession session)
    {
        string assistantName = session.AssistantName;
        
        string systemPrompt = _configuration[$"PersonalAssistants:{assistantName}:Prompt"] ?? "You are a helpful assistant.";
        string footer = _configuration[$"PersonalAssistants:{assistantName}:Footer"] ?? "Send 'stop' to stop AI messages";
        
        string model = "llama3-8b-8192";
        string apiKey = string.Empty;

        // Resolve multi-tenant configurations if present
        var aiSetting = await _dbContext.ClientAiSettings
            .FirstOrDefaultAsync(s => s.IsEnabled);

        if (aiSetting != null)
        {
            model = aiSetting.Model;
            try
            {
                apiKey = _encryptionService.Decrypt(aiSetting.EncryptedApiKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decrypt API key from ClientAiSettings database table.");
            }
        }

        // Fallback to appsettings configuration if DB key is missing or not decrypted successfully
        if (string.IsNullOrEmpty(apiKey))
        {
            apiKey = _configuration["Groq:ApiKey"] ?? string.Empty;
            model = _configuration["Groq:Model"] ?? model;
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogError("Cannot run Personal Assistant completion because Groq API Key is not configured.");
            string errReply = "AI assistant configuration error. Please contact administration.";
            var errResult = await _whatsAppService.SendTextMessageAsync(normalizedPhone, errReply);
            await LogOutgoingMessageAsync(contact, errResult.MessageId, errReply);
            return;
        }

        try
        {
            string aiResponse = await _groqProvider.GenerateResponseAsync(systemPrompt, incomingMessage, model, apiKey);
            string replyText = string.IsNullOrEmpty(footer) ? aiResponse : $"{aiResponse}\n\n{footer}";

            var sendResult = await _whatsAppService.SendTextMessageAsync(normalizedPhone, replyText);
            await LogOutgoingMessageAsync(contact, sendResult.MessageId, replyText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to call Groq completions API for phone {Phone}", normalizedPhone);
            string errReply = "An error occurred while generating AI response. Please try again later.";
            var errResult = await _whatsAppService.SendTextMessageAsync(normalizedPhone, errReply);
            await LogOutgoingMessageAsync(contact, errResult.MessageId, errReply);
        }
    }

    private async Task LogOutgoingMessageAsync(
        Contact contact, 
        string? whatsAppMessageId, 
        string text, 
        string? mediaUrl = null, 
        string? mediaType = null, 
        string? mediaFileName = null)
    {
        var conversation = await _dbContext.ChatConversations.FirstOrDefaultAsync(c => c.ContactId == contact.Id);
        if (conversation == null)
        {
            conversation = new ChatConversation
            {
                ContactId = contact.Id,
                LastMessageText = text,
                LastMessageAt = DateTime.UtcNow,
                UnreadCount = 0,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _dbContext.ChatConversations.Add(conversation);
            await _dbContext.SaveChangesAsync();
        }
        else
        {
            conversation.LastMessageText = text;
            conversation.LastMessageAt = DateTime.UtcNow;
            _dbContext.ChatConversations.Entry(conversation).State = EntityState.Modified;
        }

        _dbContext.ChatMessages.Add(new ChatMessage
        {
            ConversationId = conversation.Id,
            ContactId = contact.Id,
            WhatsAppMessageId = whatsAppMessageId,
            Direction = ChatMessageDirection.Outgoing,
            Status = ChatMessageStatus.Sent,
            Text = text,
            MediaUrl = mediaUrl,
            MediaType = mediaType,
            MediaFileName = mediaFileName,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync();
    }
}
