using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services;

/// <summary>
/// Creates any conversation rows that write-time creation missed, so every contact appears in
/// the chat sidebar even before anyone has messaged them.
///
/// <para>
/// Conversations are created when a contact is created (<c>ContactService.CreateAsync</c>) and
/// when contacts are imported. This service exists for the cases those paths cannot cover: a
/// connection added *after* the contacts, contacts inserted by some future code path that
/// forgets, or a partial failure. It is a safety net, not the primary mechanism — if it is
/// creating rows on every run, something upstream is missing a call.
/// </para>
/// <para>
/// The work this does used to run on every chat-list read, as a full contact × connection
/// cross-product with a write at the end, several times a minute per open tab.
/// </para>
/// </summary>
public class ChatConversationReconcilerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ChatConversationReconcilerService> _logger;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _startupDelay;

    public ChatConversationReconcilerService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<ChatConversationReconcilerService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _interval = TimeSpan.FromMinutes(configuration.GetValue("ChatReconciler:IntervalMinutes", 60));
        _startupDelay = TimeSpan.FromSeconds(configuration.GetValue("ChatReconciler:StartupDelaySeconds", 45));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let migrations and seeding finish before adding load.
        try { await Task.Delay(_startupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReconcileAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Never let a bad run kill the loop — the next one may well succeed.
                _logger.LogError(ex, "Chat conversation reconciliation failed.");
            }

            try { await Task.Delay(_interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        // Its own scope per iteration: the scoped AppDbContext registered in Program.cs belongs
        // to a request, and a hosted service capturing one would hold it for the process
        // lifetime.
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var chatService = scope.ServiceProvider.GetRequiredService<IChatService>();

        var contactIds = await db.Contacts
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(c => !c.IsDeleted)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        if (contactIds.Count == 0) return;

        var before = await db.ChatConversations.IgnoreQueryFilters().CountAsync(cancellationToken);

        // Reuses the same method the write paths call, rather than a hand-written INSERT. An
        // earlier version of this used raw SQL and referenced "WabaPhoneNumbers" — the table is
        // actually named "PhoneNumbers", so it failed on every run. Going through EF means the
        // mapping is the single source of truth for table and column names.
        await chatService.EnsureConversationsForContactsAsync(contactIds);

        var after = await db.ChatConversations.IgnoreQueryFilters().CountAsync(cancellationToken);
        var created = after - before;

        if (created > 0)
        {
            // Warning, not Information: a healthy system creates these at contact-creation time,
            // so a non-zero count means a write path is missing its call.
            _logger.LogWarning(
                "Chat reconciler created {Count} conversation row(s) that write-time creation missed.",
                created);
        }
    }
}
