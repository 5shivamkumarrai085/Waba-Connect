using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Services.Realtime;
using WhatsAppCampaignApi.Services.Security;

namespace WhatsAppCampaignApi.Services.Chat;

/// <summary>
/// The operational layer of the inbox: who owns a conversation, where it stands, and the SLA
/// clocks — plus automatic routing of new conversations to agents.
/// </summary>
public interface IConversationOperations
{
    /// <summary>A customer wrote: reopen if needed, start the SLA clock, route if unassigned.</summary>
    Task OnInboundAsync(int conversationId, CancellationToken ct = default);

    /// <summary>An agent replied: stops the first-response clock and marks the conversation as waiting on the customer.</summary>
    Task OnAgentReplyAsync(int conversationId, CancellationToken ct = default);

    Task AssignAsync(int conversationId, int? userId, CancellationToken ct = default);
    Task SetStatusAsync(int conversationId, ConversationStatus status, CancellationToken ct = default);

    /// <summary>Agents who may take conversations on this connection (for the assign menu).</summary>
    Task<IReadOnlyList<AssignableAgent>> GetAssignableAgentsAsync(int? connectionId, CancellationToken ct = default);
}

public sealed record AssignableAgent(int Id, string Name, string Email, int OpenConversations);

/// <inheritdoc />
public sealed class ConversationOperations : IConversationOperations
{
    public const string StrategyKey = "chatRouting.strategy";
    public const string AgentsKey = "chatRouting.agents";
    public const string FirstResponseKey = "sla.firstResponseMinutes";
    public const string ResolutionKey = "sla.resolutionHours";
    public const string AutoCloseKey = "autoClose.hours";
    public const string AutoCloseMessageKey = "autoClose.message";

    private readonly AppDbContext _db;
    private readonly IOmniSettingsService _settings;
    private readonly IPermissionResolver _permissions;
    private readonly IAccessScope _scope;
    private readonly ICurrentUserService _currentUser;
    private readonly IInboxNotifier _notifier;
    private readonly IAuditService _audit;
    private readonly WhatsAppCampaignApi.Services.Integrations.IWebhookEmitter? _webhooks;

    public ConversationOperations(
        AppDbContext db, IOmniSettingsService settings, IPermissionResolver permissions, IAccessScope scope,
        ICurrentUserService currentUser, IInboxNotifier notifier, IAuditService audit,
        WhatsAppCampaignApi.Services.Integrations.IWebhookEmitter? webhooks = null)
    {
        _webhooks = webhooks;
        _db = db;
        _settings = settings;
        _permissions = permissions;
        _scope = scope;
        _currentUser = currentUser;
        _notifier = notifier;
        _audit = audit;
    }

    public async Task OnInboundAsync(int conversationId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var conversation = await _db.ChatConversations.FirstOrDefaultAsync(c => c.Id == conversationId, ct);
        if (conversation is null) return;

        conversation.LastInboundAt = now;

        // A new question on a finished conversation reopens it, and the SLA clocks restart. A
        // conversation already open and waiting on a first answer keeps its original deadline.
        var reopening = conversation.Status is ConversationStatus.Resolved or ConversationStatus.Closed;
        var awaitingAnswer = conversation.FirstResponseDueAt is not null && conversation.FirstRespondedAt is null;

        if (reopening || !awaitingAnswer)
        {
            var firstResponseMinutes = await _settings.GetNumberAsync(FirstResponseKey, 0);
            var resolutionHours = await _settings.GetNumberAsync(ResolutionKey, 0);
            conversation.FirstResponseDueAt = firstResponseMinutes > 0 ? now.AddMinutes(firstResponseMinutes) : null;
            conversation.FirstRespondedAt = null;
            conversation.SlaBreachedAt = null;
            if (reopening || conversation.ResolveDueAt is null)
                conversation.ResolveDueAt = resolutionHours > 0 ? now.AddHours(resolutionHours) : null;
            conversation.ResolvedAt = null;
        }

        conversation.Status = ConversationStatus.Open;

        if (conversation.AssignedUserId is null)
        {
            var agent = await PickAgentAsync(conversation.ConnectionId, ct);
            if (agent is { } agentId)
            {
                conversation.AssignedUserId = agentId;
                conversation.AssignedAt = now;
            }
        }

        await _db.SaveChangesAsync(ct);
        await _notifier.ConversationUpdatedAsync(conversation.Id, conversation.ConnectionId, ct);
        await EmitAsync("conversation.message_received", conversation, ct, ("reopened", reopening));
    }

    private async Task EmitAsync(string type, WhatsAppCampaignApi.Models.Entities.ChatConversation conversation, CancellationToken ct, params (string Key, object? Value)[] extra)
    {
        if (_webhooks is null) return;
        var data = new Dictionary<string, object?>
        {
            ["conversationId"] = conversation.Id,
            ["contactId"] = conversation.ContactId,
            ["channel"] = conversation.Channel.ToString(),
            ["status"] = conversation.Status.ToString(),
            ["assignedUserId"] = conversation.AssignedUserId
        };
        foreach (var (k, v) in extra) data[k] = v;
        await _webhooks.EmitAsync(type, conversation.ConnectionId, data, null, ct);
    }

    public async Task OnAgentReplyAsync(int conversationId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        await _db.ChatConversations
            .Where(c => c.Id == conversationId && c.FirstRespondedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.FirstRespondedAt, now), ct);

        // Answered: now it waits on the customer. A resolved or closed one is left as it is.
        await _db.ChatConversations
            .Where(c => c.Id == conversationId && c.Status == ConversationStatus.Open)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.Status, ConversationStatus.Pending), ct);
    }

    public async Task AssignAsync(int conversationId, int? userId, CancellationToken ct = default)
    {
        var conversation = await _db.ChatConversations.FirstOrDefaultAsync(c => c.Id == conversationId, ct)
            ?? throw new KeyNotFoundException("Conversation not found.");

        string? assigneeName = null;
        if (userId is { } id)
        {
            var eligible = await GetAssignableAgentsAsync(conversation.ConnectionId, ct);
            var agent = eligible.FirstOrDefault(a => a.Id == id)
                ?? throw new ArgumentException("That user cannot take conversations on this connection.");
            assigneeName = agent.Name;
        }

        conversation.AssignedUserId = userId;
        conversation.AssignedAt = userId is null ? null : DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync("Chat.Assigned", "Data",
            userId is null ? $"Conversation #{conversationId} was unassigned." : $"Conversation #{conversationId} was assigned to {assigneeName}.",
            "ChatConversation", conversationId.ToString());
        await _notifier.ConversationUpdatedAsync(conversation.Id, conversation.ConnectionId, ct);
        await EmitAsync("conversation.assigned", conversation, ct);
    }

    public async Task SetStatusAsync(int conversationId, ConversationStatus status, CancellationToken ct = default)
    {
        var conversation = await _db.ChatConversations.FirstOrDefaultAsync(c => c.Id == conversationId, ct)
            ?? throw new KeyNotFoundException("Conversation not found.");

        var now = DateTime.UtcNow;
        conversation.Status = status;
        if (status is ConversationStatus.Resolved or ConversationStatus.Closed)
        {
            conversation.ResolvedAt ??= now;
        }
        else
        {
            conversation.ResolvedAt = null;
        }
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync("Chat.StatusChanged", "Data", $"Conversation #{conversationId} marked {status}.",
            "ChatConversation", conversationId.ToString());
        await _notifier.ConversationUpdatedAsync(conversation.Id, conversation.ConnectionId, ct);
        await EmitAsync("conversation.status_changed", conversation, ct);
    }

    public async Task<IReadOnlyList<AssignableAgent>> GetAssignableAgentsAsync(int? connectionId, CancellationToken ct = default)
    {
        var configured = (await _settings.GetListAsync(AgentsKey))
            .Select(v => int.TryParse(v, out var id) ? id : (int?)null)
            .Where(id => id is not null).Select(id => id!.Value).ToHashSet();

        var users = await _db.AppUsers.AsNoTracking()
            .Where(u => u.IsActive && !u.IsDeleted)
            .Select(u => new
            {
                u.Id,
                Name = (u.FirstName + " " + (u.LastName ?? "")).Trim(),
                u.Email,
                u.IsAdministrator,
                Open = _db.ChatConversations.Count(c => c.AssignedUserId == u.Id
                    && (c.Status == ConversationStatus.Open || c.Status == ConversationStatus.Pending))
            })
            .ToListAsync(ct);

        var result = new List<AssignableAgent>();
        foreach (var user in users)
        {
            if (configured.Count > 0 && !configured.Contains(user.Id)) continue;

            // Must be able to use Chat at all, and (under connection scoping) this connection.
            if (!user.IsAdministrator && !(await _permissions.GetEffectivePermissionsAsync(user.Id)).Contains("Chat.View")) continue;
            if (connectionId is { } conn
                && await _scope.GetAllowedConnectionIdsForUserAsync(user.Id, user.IsAdministrator, ct) is { } allowed
                && !allowed.Contains(conn)) continue;

            result.Add(new AssignableAgent(user.Id, user.Name, user.Email, user.Open));
        }

        return result.OrderBy(a => a.Name).ToList();
    }

    /// <summary>Round-robin (longest since last assignment) or least-busy (fewest open conversations).</summary>
    private async Task<int?> PickAgentAsync(int? connectionId, CancellationToken ct)
    {
        var strategy = (await _settings.GetValueAsync(StrategyKey))?.Trim().ToLowerInvariant();
        if (strategy is null or "" or "off") return null;

        var agents = await GetAssignableAgentsAsync(connectionId, ct);
        if (agents.Count == 0) return null;

        if (strategy == "leastbusy")
            return agents.OrderBy(a => a.OpenConversations).ThenBy(a => a.Id).First().Id;

        var ids = agents.Select(a => a.Id).ToArray();
        var lastAssigned = await _db.ChatConversations.AsNoTracking()
            .Where(c => c.AssignedUserId != null && ids.Contains(c.AssignedUserId.Value) && c.AssignedAt != null)
            .GroupBy(c => c.AssignedUserId!.Value)
            .Select(g => new { UserId = g.Key, Last = g.Max(c => c.AssignedAt) })
            .ToDictionaryAsync(x => x.UserId, x => x.Last, ct);

        return agents
            .OrderBy(a => lastAssigned.TryGetValue(a.Id, out var last) ? last : DateTime.MinValue)
            .ThenBy(a => a.Id)
            .First().Id;
    }
}

/// <summary>Flags SLA breaches and auto-closes conversations that have gone quiet.</summary>
public sealed class ChatSlaWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ChatSlaWorker> _logger;

    public ChatSlaWorker(IServiceScopeFactory scopeFactory, ILogger<ChatSlaWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var settings = scope.ServiceProvider.GetRequiredService<IOmniSettingsService>();
                var notifier = scope.ServiceProvider.GetRequiredService<IInboxNotifier>();
                var now = DateTime.UtcNow;

                // First-response breaches: flagged once, and pushed to open inboxes.
                var breached = await db.ChatConversations.AsNoTracking()
                    .Where(c => c.Status == ConversationStatus.Open && c.FirstRespondedAt == null
                             && c.FirstResponseDueAt != null && c.FirstResponseDueAt < now && c.SlaBreachedAt == null)
                    .OrderBy(c => c.FirstResponseDueAt)
                    .Select(c => new { c.Id, c.ConnectionId })
                    .Take(500)
                    .ToListAsync(stoppingToken);
                if (breached.Count > 0)
                {
                    var ids = breached.Select(b => b.Id).ToArray();
                    await db.ChatConversations.Where(c => ids.Contains(c.Id) && c.SlaBreachedAt == null)
                        .ExecuteUpdateAsync(u => u.SetProperty(c => c.SlaBreachedAt, now), stoppingToken);
                    foreach (var b in breached) await notifier.ConversationUpdatedAsync(b.Id, b.ConnectionId, stoppingToken);
                    _logger.LogInformation("{Count} conversation(s) breached their first-response SLA.", breached.Count);
                }

                // Auto-close: no word from the customer for N hours.
                var hours = await settings.GetNumberAsync(ConversationOperations.AutoCloseKey, 0);
                if (hours > 0)
                {
                    var cutoff = now.AddHours(-hours);
                    var stale = await db.ChatConversations
                        .Where(c => (c.Status == ConversationStatus.Open || c.Status == ConversationStatus.Pending)
                                 && c.LastInboundAt != null && c.LastInboundAt < cutoff
                                 && (c.LastMessageAt == null || c.LastMessageAt < cutoff))
                        .Include(c => c.Contact)
                        .OrderBy(c => c.LastInboundAt)
                        .Take(200)
                        .ToListAsync(stoppingToken);

                    if (stale.Count > 0)
                    {
                        var message = await settings.GetValueAsync(ConversationOperations.AutoCloseMessageKey);
                        var whatsApp = scope.ServiceProvider.GetRequiredService<IWhatsAppService>();
                        foreach (var conversation in stale)
                        {
                            conversation.Status = ConversationStatus.Closed;
                            conversation.ResolvedAt ??= now;

                            // A closing note only inside WhatsApp's 24-hour service window, where free text is allowed.
                            if (!string.IsNullOrWhiteSpace(message) && conversation.Channel == MessageChannel.WhatsApp
                                && conversation.LastInboundAt > now.AddHours(-24) && conversation.Contact is { } contact)
                            {
                                try { await whatsApp.SendTextMessageAsync(contact.Phone, message, null, conversation.ConnectionId); }
                                catch (Exception ex) { _logger.LogDebug(ex, "Closing message for conversation {Id} not sent.", conversation.Id); }
                            }
                        }
                        await db.SaveChangesAsync(stoppingToken);
                        foreach (var c in stale) await notifier.ConversationUpdatedAsync(c.Id, c.ConnectionId, stoppingToken);
                        _logger.LogInformation("Auto-closed {Count} quiet conversation(s).", stale.Count);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "SLA sweep failed; retrying.");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
