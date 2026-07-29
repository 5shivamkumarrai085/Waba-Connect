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

    public async Task<bool> RouteMessageAsync(string phoneNumber, string incomingMessage, Contact contact, int? connectionId = null)
    {
        string normalizedPhone = phoneNumber.Replace("+", "").Trim();
        string cleanMessage = incomingMessage.Trim();

        if (contact == null || !contact.IsActive || contact.IsDeleted)
        {
            _logger.LogInformation("Contact {Phone} is inactive or deleted. Ignoring incoming message for bot routing.", normalizedPhone);
            return false;
        }

        _logger.LogInformation("Routing incoming message from {Phone}: '{Message}'", normalizedPhone, cleanMessage);

        if (connectionId.HasValue)
        {
            var staleStates = await _dbContext.ConversationStates
                .Where(s => s.PhoneNumber == normalizedPhone && s.Status == "Active" && s.ConnectionId != connectionId.Value)
                .ToListAsync();

            if (staleStates.Count > 0)
            {
                _logger.LogInformation("Cleaning up {Count} stale active ConversationStates for phone {Phone} on other connections.", staleStates.Count, normalizedPhone);
                foreach (var state in staleStates)
                {
                    state.Status = "Completed";
                }
                await _dbContext.SaveChangesAsync();
            }
        }

        // 1. Stop Check
        var activeSession = await _dbContext.AiSessions
            .Include(s => s.MessageBot)
            .FirstOrDefaultAsync(s => s.PhoneNumber == normalizedPhone && s.IsActive && 
                (connectionId.HasValue ? s.ConnectionId == connectionId.Value : s.ConnectionId == null));

        var botFlowState = await _dbContext.ConversationStates
            .FirstOrDefaultAsync(s => s.PhoneNumber == normalizedPhone && s.Status == "Active" && 
                (connectionId.HasValue ? s.ConnectionId == connectionId.Value : s.ConnectionId == null));

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
                        .FirstOrDefaultAsync(s => s.PhoneNumber == normalizedPhone && 
                            (connectionId.HasValue ? s.ConnectionId == connectionId.Value : s.ConnectionId == null));
                    if (existingSession != null)
                    {
                        existingSession.IsActive = false;
                        existingSession.UpdatedAt = DateTime.UtcNow;
                    }
                    else
                    {
                        var firstBot = await _dbContext.MessageBots.OrderBy(x => x.Id).FirstOrDefaultAsync();
                        if (firstBot != null)
                        {
                            var newInactiveSession = new AiSession
                            {
                                PhoneNumber = normalizedPhone,
                                MessageBotId = firstBot.Id,
                                IsActive = false,
                                ConnectionId = connectionId,
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
                string? resolvedPhoneNumberId = null;
                if (connectionId.HasValue)
                {
                    var phone = await _dbContext.WabaPhoneNumbers.FirstOrDefaultAsync(p => p.ConnectionId == connectionId.Value);
                    resolvedPhoneNumberId = phone?.PhoneNumberId;
                }
                var sendResult = await _whatsAppService.SendTextMessageAsync(normalizedPhone, stopConfirmation, resolvedPhoneNumberId, connectionId);

                await LogOutgoingMessageAsync(contact, sendResult, stopConfirmation, connectionId: connectionId);
                return true;
            }
        }

        // 2. Existing AI Session Check
        if (activeSession != null)
        {
            var bot = activeSession.MessageBot;
            bool isAssistantValid = bot != null &&
                string.Equals(bot.OptionType, "PersonalAssistant", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(activeSession.AssistantName) &&
                !activeSession.AssistantName.Equals("select option", StringComparison.OrdinalIgnoreCase) &&
                !activeSession.AssistantName.Equals("select assistant", StringComparison.OrdinalIgnoreCase);

            if (isAssistantValid)
            {
                _logger.LogInformation("Active AI session exists for phone {Phone}. Routing directly to Groq assistant: '{AssistantName}'", normalizedPhone, activeSession.AssistantName);
                await ProcessAiAssistantRequestAsync(contact, normalizedPhone, cleanMessage, activeSession, connectionId);
                return true;
            }
            else
            {
                _logger.LogInformation("Deactivating AI session for phone {Phone} because Personal Assistant was unselected or disabled.", normalizedPhone);
                activeSession.IsActive = false;
                activeSession.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
                activeSession = null;
            }
        }

        // 3. Message Bot Keyword Matching
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
            if (botFlowState != null)
            {
                _dbContext.ConversationStates.Remove(botFlowState);
                await _dbContext.SaveChangesAsync();
            }

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
                        ConnectionId = connectionId,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    _dbContext.AiSessions.Add(newSession);
                    await _dbContext.SaveChangesAsync();

                    _logger.LogInformation("Created new AI session for phone {Phone} targeting assistant '{AssistantName}'", normalizedPhone, assistantName);
                    await ProcessAiAssistantRequestAsync(contact, normalizedPhone, cleanMessage, newSession, connectionId);
                }
                else
                {
                    // Stop any active AI sessions for this phone number
                    var activeSessions = await _dbContext.AiSessions
                        .Where(s => s.PhoneNumber == normalizedPhone && s.IsActive &&
                            (connectionId.HasValue ? s.ConnectionId == connectionId.Value : true))
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
                    var sendResult = await _messageBotExecutor.ExecuteReplyAsync(matchedBot, normalizedPhone, connectionId);
                    await LogOutgoingMessageAsync(contact, sendResult, matchedBot.ReplyText, connectionId: connectionId);
                }
            }
            else
            {
                // Deactivate any active AI sessions for this phone number
                var activeSessions = await _dbContext.AiSessions
                    .Where(s => s.PhoneNumber == normalizedPhone && s.IsActive &&
                        (connectionId.HasValue ? s.ConnectionId == connectionId.Value : true))
                    .ToListAsync();
                foreach (var s in activeSessions)
                {
                    s.IsActive = false;
                    s.UpdatedAt = DateTime.UtcNow;
                }
                if (activeSessions.Count > 0)
                {
                    await _dbContext.SaveChangesAsync();
                }

                // Predefined static/buttons/media reply
                var sendResult = await _messageBotExecutor.ExecuteReplyAsync(matchedBot, normalizedPhone, connectionId);
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
                    sendResult, 
                    textLog, 
                    matchedBot.OptionType.Equals("Files", StringComparison.OrdinalIgnoreCase) ? matchedBot.FileUrl : null,
                    matchedBot.OptionType.Equals("Files", StringComparison.OrdinalIgnoreCase) ? matchedBot.FileType : null,
                    matchedBot.OptionType.Equals("Files", StringComparison.OrdinalIgnoreCase) ? matchedBot.FileName : null,
                    connectionId);
            }

            return true;
        }

        // 4. Template Bot Fallback
        _logger.LogDebug("Evaluating Template Bot triggers for phone {Phone}", normalizedPhone);
        bool triggeredTemplate = await _whatsAppService.TryTriggerTemplateBotAsync(normalizedPhone, cleanMessage, contact, connectionId);
        if (triggeredTemplate)
        {
            if (botFlowState != null)
            {
                _dbContext.ConversationStates.Remove(botFlowState);
                await _dbContext.SaveChangesAsync();
            }
            _logger.LogInformation("Message matched and handled by Template Bot for phone {Phone}", normalizedPhone);
            return true;
        }

        // 5. Bot Flow Engine Fallback
        _logger.LogDebug("Evaluating Bot Flow triggers for phone {Phone}", normalizedPhone);
        bool triggeredBotFlow = await _flowExecutionService.ExecuteFlowStepAsync(normalizedPhone, cleanMessage, connectionId);
        if (triggeredBotFlow)
        {
            _logger.LogInformation("Message handled by Bot Flow engine for phone {Phone}", normalizedPhone);
            return true;
        }

        _logger.LogInformation("No active bot module triggered for phone {Phone}", normalizedPhone);
        return false;
    }

    private async Task ProcessAiAssistantRequestAsync(Contact contact, string normalizedPhone, string incomingMessage, AiSession session, int? connectionId = null)
    {
        string assistantName = session.AssistantName;
        
        string? apiKey = _configuration[$"PersonalAssistants:{assistantName}:ApiKey"] ?? _configuration["Groq:ApiKey"];
        string? model = _configuration[$"PersonalAssistants:{assistantName}:Model"] ?? _configuration["Groq:Model"] ?? "llama-3.1-8b-instant";
        string? systemPrompt = _configuration[$"PersonalAssistants:{assistantName}:Prompt"] 
            ?? _configuration[$"PersonalAssistants:{assistantName}:SystemPrompt"] 
            ?? "You are OmniBot, a highly capable customer assistant for OmniConnect platform. Respond to the customer query in a polite, helpful, and concise manner.";
        string? footer = _configuration[$"PersonalAssistants:{assistantName}:Footer"] ?? "Send 'stop' to stop AI messages";

        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogError("Cannot run Personal Assistant completion because Groq API Key is not configured.");
            string? resolvedPhoneNumberId = null;
            if (connectionId.HasValue)
            {
                var phone = await _dbContext.WabaPhoneNumbers.FirstOrDefaultAsync(p => p.ConnectionId == connectionId.Value);
                resolvedPhoneNumberId = phone?.PhoneNumberId;
            }

            string errReply = "AI assistant configuration error. Please contact administration.";
            var errResult = await _whatsAppService.SendTextMessageAsync(normalizedPhone, errReply, resolvedPhoneNumberId, connectionId);
            await LogOutgoingMessageAsync(contact, errResult, errReply, connectionId: connectionId);
            return;
        }

        try
        {
            string aiResponse = await _groqProvider.GenerateResponseAsync(systemPrompt ?? "You are a helpful assistant.", incomingMessage, model ?? "llama3-8b-8192", apiKey);
            string replyText = string.IsNullOrEmpty(footer) ? aiResponse : $"{aiResponse}\n\n{footer}";

            string? resolvedPhoneNumberId = null;
            if (connectionId.HasValue)
            {
                var phone = await _dbContext.WabaPhoneNumbers.FirstOrDefaultAsync(p => p.ConnectionId == connectionId.Value);
                resolvedPhoneNumberId = phone?.PhoneNumberId;
            }

            var sendResult = await _whatsAppService.SendTextMessageAsync(normalizedPhone, replyText, resolvedPhoneNumberId, connectionId);
            await LogOutgoingMessageAsync(contact, sendResult, replyText, connectionId: connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to call Groq completions API for phone {Phone}", normalizedPhone);
            string? resolvedPhoneNumberId = null;
            if (connectionId.HasValue)
            {
                var phone = await _dbContext.WabaPhoneNumbers.FirstOrDefaultAsync(p => p.ConnectionId == connectionId.Value);
                resolvedPhoneNumberId = phone?.PhoneNumberId;
            }

            string errReply = "An error occurred while generating AI response. Please try again later.";
            var errResult = await _whatsAppService.SendTextMessageAsync(normalizedPhone, errReply, resolvedPhoneNumberId, connectionId);
            await LogOutgoingMessageAsync(contact, errResult, errReply, connectionId: connectionId);
        }
    }

    private async Task LogOutgoingMessageAsync(
        Contact contact, 
        WhatsAppSendResult sendResult, 
        string text, 
        string? mediaUrl = null, 
        string? mediaType = null, 
        string? mediaFileName = null,
        int? connectionId = null)
    {
        var conversation = await _dbContext.ChatConversations
            .FirstOrDefaultAsync(c => c.ContactId == contact.Id && (connectionId == null || c.ConnectionId == connectionId));

        if (conversation == null)
        {
            var account = connectionId.HasValue 
                ? await _dbContext.WabaPhoneNumbers.FirstOrDefaultAsync(p => p.ConnectionId == connectionId.Value)
                : await _dbContext.WabaPhoneNumbers.FirstOrDefaultAsync();

            conversation = new ChatConversation
            {
                ContactId = contact.Id,
                ConnectionId = connectionId,
                WabaPhoneNumberId = account?.Id,
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
            if (conversation.ConnectionId == null && connectionId.HasValue)
            {
                conversation.ConnectionId = connectionId;
            }
            conversation.LastMessageText = text;
            conversation.LastMessageAt = DateTime.UtcNow;
            _dbContext.ChatConversations.Entry(conversation).State = EntityState.Modified;
        }

        _dbContext.ChatMessages.Add(new ChatMessage
        {
            ConversationId = conversation.Id,
            ContactId = contact.Id,
            ConnectionId = connectionId,
            WhatsAppMessageId = sendResult.Success ? sendResult.MessageId : null,
            Direction = ChatMessageDirection.Outgoing,
            Status = sendResult.Success ? ChatMessageStatus.Sent : ChatMessageStatus.Failed,
            ErrorMessage = sendResult.Success ? null : sendResult.ErrorMessage,
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
