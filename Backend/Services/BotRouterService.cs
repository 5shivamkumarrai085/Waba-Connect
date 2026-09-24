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
    private readonly IEnumerable<IAiProvider> _aiProviders;
    private readonly IEncryptionService _encryptionService;
    private readonly MessageBotExecutor _messageBotExecutor;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BotRouterService> _logger;

    private readonly IOmniSettingsService _settings;

    public BotRouterService(
        AppDbContext dbContext,
        IFlowExecutionService flowExecutionService,
        IWhatsAppService whatsAppService,
        IEnumerable<IAiProvider> aiProviders,
        IEncryptionService encryptionService,
        MessageBotExecutor messageBotExecutor,
        IConfiguration configuration,
        ILogger<BotRouterService> logger,
        IOmniSettingsService settings)
    {
        _dbContext = dbContext;
        _flowExecutionService = flowExecutionService;
        _whatsAppService = whatsAppService;
        _aiProviders = aiProviders;
        _encryptionService = encryptionService;
        _messageBotExecutor = messageBotExecutor;
        _configuration = configuration;
        _logger = logger;
        _settings = settings;
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

        // ── Stop Bot ────────────────────────────────────────────────────────────────────────────
        // Ahead of every other branch, because a customer who has said stop must not be answered
        // by any automation — message bot, template bot, flow or assistant alike.
        if (await HandleStopBotAsync(normalizedPhone, cleanMessage, connectionId))
        {
            return false;
        }

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
            // Words that end an assistant session. The configured list wins; the per-assistant
            // appsettings value, and then "stop", remain as the fallback so an install that has
            // never opened the settings page behaves exactly as it did before.
            var stopKeywords = (await _settings.GetListAsync("assistant.stopKeywords")).ToList();

            if (stopKeywords.Count == 0)
            {
                var configuredKeyword = activeSession != null
                    ? _configuration[$"PersonalAssistants:{activeSession.AssistantName}:StopKeyword"]
                    : null;

                stopKeywords.Add(configuredKeyword ?? "stop");
            }

            var stopCandidate = cleanMessage.Trim().Trim('.', '!', '?', ',', ';', ':').Trim();

            if (stopKeywords.Any(k => string.Equals(k.Trim(), stopCandidate, StringComparison.OrdinalIgnoreCase)))
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
            .Where(b => b.IsActive && (b.ConnectionId == null || b.ConnectionId == connectionId))
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
                    string assistantName = matchedBot.AssistantName ?? string.Empty;
                    
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

    /// <summary>
    /// Applies the Stop Bot settings to one inbound message.
    ///
    /// <para>
    /// Returns true when this message must not produce any automated reply — either because it
    /// <em>is</em> the stop word, or because the sender is still inside a stop window they opened
    /// earlier.
    /// </para>
    /// <para>
    /// The suppression is per sender and per connection, never global: silencing bots for everyone
    /// because one customer opted out would be a far larger action than the customer asked for.
    /// </para>
    /// <para>
    /// Matching is deliberately forgiving — trimmed, case-insensitive, and tolerant of the
    /// punctuation people actually type ("STOP.", "stop!"). It is still whole-message matching
    /// rather than substring: a sentence containing the word "stop" in passing ("stop by the shop
    /// tomorrow?") is a question for a human, not an opt-out.
    /// </para>
    /// </summary>
    private async Task<bool> HandleStopBotAsync(string normalizedPhone, string cleanMessage, int? connectionId)
    {
        try
        {
            var now = DateTime.UtcNow;

            var suppression = await _dbContext.BotSuppressions
                .Where(b => b.PhoneNumber == normalizedPhone
                            && (b.ConnectionId == null || b.ConnectionId == connectionId))
                .OrderByDescending(b => b.CreatedAt)
                .FirstOrDefaultAsync();

            var isSuppressed = suppression != null && (suppression.ResumeAt == null || suppression.ResumeAt > now);

            var keywords = await _settings.GetListAsync("stopBot.keywords");

            if (keywords.Count > 0)
            {
                var candidate = cleanMessage.Trim().Trim('.', '!', '?', ',', ';', ':').Trim();

                var matched = keywords.FirstOrDefault(k =>
                    !string.IsNullOrWhiteSpace(k) &&
                    string.Equals(k.Trim(), candidate, StringComparison.OrdinalIgnoreCase));

                if (matched != null)
                {
                    // A restart window of zero or less means "until someone intervenes", which is
                    // the honest reading of an unqualified stop.
                    var restartHours = await _settings.GetNumberAsync("stopBot.restartAfterHours", 0);
                    var resumeAt = restartHours > 0 ? now.AddHours(restartHours) : (DateTime?)null;

                    _dbContext.BotSuppressions.Add(new BotSuppression
                    {
                        PhoneNumber = normalizedPhone,
                        ConnectionId = connectionId,
                        MatchedKeyword = matched,
                        CreatedAt = now,
                        ResumeAt = resumeAt
                    });

                    // Anything the customer was mid-way through is over: leaving a flow or an
                    // assistant session active would have it resume the moment the window lapses,
                    // picking up a conversation the customer ended.
                    var openSessions = await _dbContext.AiSessions
                        .Where(a => a.PhoneNumber == normalizedPhone && a.IsActive)
                        .ToListAsync();

                    foreach (var session in openSessions)
                    {
                        session.IsActive = false;
                        session.UpdatedAt = now;
                    }

                    var openFlows = await _dbContext.ConversationStates
                        .Where(c => c.PhoneNumber == normalizedPhone && c.Status == "Active")
                        .ToListAsync();

                    foreach (var flow in openFlows)
                    {
                        flow.Status = "Completed";
                        flow.UpdatedAt = now;
                    }

                    await _dbContext.SaveChangesAsync();

                    _logger.LogInformation(
                        "Stop Bot: {Phone} sent \"{Keyword}\". Automated replies suppressed {Until}.",
                        normalizedPhone, matched,
                        resumeAt.HasValue ? $"until {resumeAt:u}" : "indefinitely");

                    return true;
                }
            }

            if (isSuppressed)
            {
                _logger.LogInformation(
                    "Stop Bot: ignoring message from {Phone} — suppressed until {Until}.",
                    normalizedPhone,
                    suppression!.ResumeAt.HasValue ? suppression.ResumeAt.Value.ToString("u") : "further notice");

                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            // Failing open: a settings or database problem must not silence every bot in the
            // product. The message routes as it would have before this feature existed.
            _logger.LogError(ex, "Stop Bot check failed for {Phone}; continuing with normal routing.", normalizedPhone);
            return false;
        }
    }

    /// <summary>
    /// The registered AI provider with this name.
    ///
    /// Falls back to the first registered provider rather than throwing: a missing registration is
    /// a deployment problem, and answering the customer with the other model beats not answering.
    /// </summary>
    private IAiProvider ResolveProvider(string providerName)
    {
        var match = _aiProviders.FirstOrDefault(p =>
            string.Equals(p.ProviderName, providerName, StringComparison.OrdinalIgnoreCase));

        if (match != null) return match;

        _logger.LogWarning("AI provider \"{Provider}\" is not registered; falling back.", providerName);
        return _aiProviders.First();
    }

    private async Task ProcessAiAssistantRequestAsync(Contact contact, string normalizedPhone, string incomingMessage, AiSession session, int? connectionId = null)
    {
        string assistantName = session.AssistantName;

        // ── AI Integration ──────────────────────────────────────────────────────────────────────
        // With OpenAI switched on in settings, the assistant runs on the configured model and the
        // key stored (encrypted) there. With it off, nothing about the previous behaviour changes:
        // Groq, with the key and model from appsettings. The provider is chosen by name from the
        // registered set rather than being newed up here.
        var useOpenAi = await _settings.GetFlagAsync("ai.openAiEnabled");

        string? apiKey;
        string? model;
        IAiProvider provider;

        if (useOpenAi)
        {
            apiKey = await _settings.GetValueAsync("ai.openAiSecretKey");
            model = await _settings.GetValueAsync("ai.chatModel");
            provider = ResolveProvider("OpenAI");
        }
        else
        {
            apiKey = _configuration[$"PersonalAssistants:{assistantName}:ApiKey"] ?? _configuration["Groq:ApiKey"];
            model = _configuration[$"PersonalAssistants:{assistantName}:Model"] ?? _configuration["Groq:Model"] ?? "llama-3.1-8b-instant";
            provider = ResolveProvider("Groq");
        }
        // Prompt resolution, most specific first:
        //   1. a database prompt matching the assistant's name,
        //   2. the database prompt flagged as default,
        //   3. the original appsettings values,
        //   4. a hardcoded fallback.
        // The seeder copies the appsettings prompt into the default row, so on an existing
        // install steps 1-2 return exactly what step 3 used to — behaviour is unchanged.
        string? systemPrompt = await _dbContext.AiPrompts
            .AsNoTracking()
            .Where(p => p.IsActive && p.Name.ToLower() == assistantName.ToLower())
            .Select(p => p.PromptText)
            .FirstOrDefaultAsync();

        systemPrompt ??= await _dbContext.AiPrompts
            .AsNoTracking()
            .Where(p => p.IsActive && p.IsDefault)
            .Select(p => p.PromptText)
            .FirstOrDefaultAsync();

        systemPrompt ??= _configuration[$"PersonalAssistants:{assistantName}:Prompt"]
            ?? _configuration[$"PersonalAssistants:{assistantName}:SystemPrompt"]
            ?? "You are OmniBot, a highly capable customer assistant for OmniConnect platform. Respond to the customer query in a polite, helpful, and concise manner.";
        // Assistant footer: the configured message wins, then the per-assistant appsettings value,
        // then the original default. A footer set to whitespace means "no footer" and is honoured
        // as such rather than falling through to the default.
        var configuredFooter = await _settings.GetValueAsync("assistant.footerMessage");

        string? footer = configuredFooter is not null
            ? configuredFooter.Trim()
            : _configuration[$"PersonalAssistants:{assistantName}:Footer"] ?? "Send 'stop' to stop AI messages";

        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogError(
                "Cannot run Personal Assistant completion: no API key configured for {Provider}.",
                provider.ProviderName);
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
            string aiResponse = await provider.GenerateResponseAsync(
                systemPrompt ?? "You are a helpful assistant.",
                incomingMessage,
                model ?? string.Empty,
                apiKey);

            string replyText = string.IsNullOrEmpty(footer) ? aiResponse : $"{aiResponse}\n\n{footer}";

            // Assistant message delay. An instant answer reads as a machine; a short pause reads as
            // someone typing. Awaited rather than blocked — this runs on a pooled thread serving
            // the webhook pipeline, and Thread.Sleep here would hold that thread against every
            // other inbound message.
            var delaySeconds = await _settings.GetNumberAsync("assistant.messageDelaySeconds", 0);
            if (delaySeconds > 0)
            {
                // Bounded: a mistyped value must not park a worker for hours.
                var delay = TimeSpan.FromSeconds(Math.Min(delaySeconds, 120));
                _logger.LogDebug("Holding assistant reply to {Phone} for {Delay}s.", normalizedPhone, delay.TotalSeconds);
                await Task.Delay(delay);
            }

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
            _logger.LogError(ex, "Failed to call the {Provider} completions API for phone {Phone}", provider.ProviderName, normalizedPhone);
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
