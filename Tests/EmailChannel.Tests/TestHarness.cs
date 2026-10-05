using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Activity;
using WhatsAppCampaignApi.Models.Options;
using WhatsAppCampaignApi.Services;
using WhatsAppCampaignApi.Services.Email;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Services.Queue;
using WhatsAppCampaignApi.Services.Realtime;
using WhatsAppCampaignApi.Services.Storage;

namespace EmailChannel.Tests;

/// <summary>
/// Builds the service graph the tests run against, and provides direct SQL access for assertions.
///
/// <para>
/// The registrations here mirror Program.cs rather than substituting fakes. That is deliberate:
/// a suite wired differently from production verifies the wiring of the suite. The only
/// substitution is the audit service, which is a fire-and-forget side effect that no test asserts
/// on and which would otherwise leave rows behind in a shared table.
/// </para>
/// <para>
/// Raw SQL is used for assertions rather than EF, so a test observes what is actually in the
/// database instead of what the change tracker believes — including columns the mapping might be
/// getting wrong.
/// </para>
/// </summary>
public sealed class TestHarness : IAsyncDisposable
{
    /// <summary>
    /// Everything created by the suite carries this prefix, and cleanup keys off it. A test that
    /// fails midway still leaves the database recognisably clean-able.
    /// </summary>
    public const string Prefix = "zz-emailtest";

    private readonly ServiceProvider _provider;

    public string ConnectionString { get; }
    public IConfiguration Configuration { get; }

    /// <summary>A short run-unique suffix, so concurrent runs cannot collide on unique indexes.</summary>
    public string Tag { get; } = Guid.NewGuid().ToString("N")[..8];

    private TestHarness(ServiceProvider provider, IConfiguration configuration, string connectionString)
    {
        _provider = provider;
        Configuration = configuration;
        ConnectionString = connectionString;
    }

    public static TestHarness Create(string backendDirectory)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(backendDirectory)
            .AddJsonFile("appsettings.json")
            // Secrets are no longer committed: the same user-secrets store and environment
            // variables the API reads supply the connection string and keys here too.
            .AddUserSecrets(typeof(WhatsAppCampaignApi.Data.AppDbContext).Assembly, optional: true)
            .AddEnvironmentVariables()
            // The channel must be on for the pipeline to run at all, and the unsubscribe signer
            // refuses to issue tokens without a key. Supplied here rather than in appsettings so
            // no test value is ever committed, and so running the suite cannot change how the
            // application itself is configured.
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:Enabled"] = "true",
                ["Email:Unsubscribe:SigningKey"] = "integration-test-signing-key-not-a-real-secret",
                ["Email:Unsubscribe:PublicBaseUrl"] = "https://example.test",
                ["Email:Tracking:SigningSecret"] = "integration-test-tracking-secret-not-a-real-secret",
                ["Email:Tracking:BaseUrl"] = "https://example.test",
                ["Email:Tracking:Enabled"] = "true",
                ["Email:Dispatch:DefaultSendRatePerSecond"] = "50",
                // Tightened so the workers react quickly; production values would make the suite
                // spend most of its time asleep.
                ["Email:Queue:PollIntervalMs"] = "100",
                ["Email:Queue:IdleBackoffMs"] = "300"
            })
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("No DefaultConnection connection string was found.");

        // The field cipher is process-wide, initialised once at startup exactly as Program.cs does.
        SecretCipher.Initialize(configuration["Encryption:Key"]);

        var services = new ServiceCollection();

        // Warning and above: the suite's own output is the signal, and info-level logs from the
        // workers would bury it.
        services.AddLogging(b => b
            .AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = null; })
            .SetMinimumLevel(LogLevel.Warning));

        // Registered as IConfiguration explicitly. Build() returns IConfigurationRoot, and the
        // non-generic overload would register it only under that type — leaving EncryptionService,
        // which asks for IConfiguration, unresolvable.
        services.AddSingleton<IConfiguration>(configuration);
        // One data source shared by EF, the queue and the rate limiter, as in production.
        var dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();
        services.AddSingleton(dataSource);
        // Mirrors Program.cs in Development: a query that includes two collections without opting into
        // split queries throws, so the suite catches the cartesian-product queries that became 500s.
        services.AddDbContextFactory<AppDbContext>(o => o.UseNpgsql(dataSource)
            .ConfigureWarnings(w => w.Throw(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.MultipleCollectionIncludeWarning)));
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());
        services.AddOptions<EmailOptions>().Bind(configuration.GetSection(EmailOptions.SectionName));

        services.AddScoped<IEncryptionService, EncryptionService>();
        services.AddSingleton<RecordingAuditService>();
        services.AddScoped<IAuditService>(sp => sp.GetRequiredService<RecordingAuditService>());

        services.AddSingleton<IMimeMessageBuilder, MimeMessageBuilder>();
        services.AddScoped<IEmailProvider, SmtpEmailProvider>();
        services.AddScoped<IEmailProviderFactory, EmailProviderFactory>();
        services.AddScoped<IEmailConnectionService, EmailConnectionService>();
        services.AddScoped<IEmailTemplateService, EmailTemplateService>();
        services.AddScoped<IEmailSenderGate, EmailSenderGate>();
        services.AddScoped<IEmailSuppressionService, EmailSuppressionService>();
        services.AddScoped<IEmailSendRecorder, EmailSendRecorder>();
        services.AddScoped<IEmailCampaignDispatcher, EmailCampaignDispatcher>();
        services.AddScoped<ICampaignExecutionGate, AutoApproveCampaignExecutionGate>();
        services.AddSingleton<IUnsubscribeTokenService, UnsubscribeTokenService>();
        services.AddSingleton<IEmailRateLimiter, PostgresEmailRateLimiter>();
        services.AddSingleton<IJobQueue, PostgresJobQueue>();
        services.AddMemoryCache();

        // Campaign events: the store, the per-recipient processor and the in-process publisher.
        services.AddScoped<IEmailEventStore, EmailEventStore>();
        services.AddScoped<ICampaignEmailEventProcessor, CampaignEmailEventProcessor>();
        services.AddSingleton<InProcessEventPublisher>();
        services.AddSingleton<IEventPublisher>(sp => sp.GetRequiredService<InProcessEventPublisher>());
        services.AddSingleton<IEmailTrackingService, EmailTrackingService>();
        services.AddSingleton<SmtpConnectionPoolManager>();
        services.AddScoped<IInboundEmailThreader, InboundEmailThreader>();
        services.AddSingleton<IInboxNotifier, NoOpInboxNotifier>();

        // Attachments and uploaded CSVs resolve through the same storage abstraction as the API.
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(backendDirectory));
        services.AddOptions<StorageOptions>().Bind(configuration.GetSection(StorageOptions.SectionName));
        services.AddHttpClient(FileStorage.HttpClientName);
        services.AddSingleton<IFileStorage, FileStorage>();
        services.AddScoped<ICurrentUserService, SystemCurrentUser>();
        services.AddScoped<WhatsAppCampaignApi.Services.Security.IAccessScope, WhatsAppCampaignApi.Services.Security.AccessScope>();
        services.AddScoped<IOmniSettingsService, OmniSettingsService>();
        services.AddScoped<WhatsAppCampaignApi.Services.Segments.ISegmentService, WhatsAppCampaignApi.Services.Segments.SegmentService>();
        services.AddSingleton<DnsClient.ILookupClient>(_ => new DnsClient.LookupClient(new DnsClient.LookupClientOptions { Timeout = TimeSpan.FromSeconds(3), Retries = 1 }));
        services.AddScoped<IDeliverabilityService, DeliverabilityService>();
        services.AddHttpClient<IPublicEndpointProbe, PublicEndpointProbe>();
        services.AddScoped<WhatsAppCampaignApi.Services.Campaigns.IAbTestService, WhatsAppCampaignApi.Services.Campaigns.AbTestService>();
        services.AddScoped<WhatsAppCampaignApi.Services.Campaigns.IFollowUpService, WhatsAppCampaignApi.Services.Campaigns.FollowUpService>();
        services.AddScoped<WhatsAppCampaignApi.Services.Compliance.IComplianceGuard, WhatsAppCampaignApi.Services.Compliance.ComplianceGuard>();
        services.AddScoped<WhatsAppCampaignApi.Services.Compliance.IConsentService, WhatsAppCampaignApi.Services.Compliance.ConsentService>();

        // CampaignService takes both channels' collaborators. Nothing here exercises the WhatsApp
        // half — and that is itself part of what the suite checks, since an email campaign that
        // reached IWhatsAppService would throw rather than pass quietly.
        services.AddScoped(_ => UnavailableService.Create<IWhatsAppService>());
        services.AddScoped(_ => UnavailableService.Create<IChatService>());
        services.AddScoped(_ => UnavailableService.Create<WhatsAppCampaignApi.Services.WhatsApp.IWhatsAppCampaignDispatcher>());
        services.AddScoped<ICampaignService, CampaignService>();

        // Outbound webhooks. The suite's receiver listens on localhost, so this guard allows
        // private networks; Phase 19 checks the production guard separately.
        var webhookGuard = new WhatsAppCampaignApi.Services.Integrations.WebhookUrlGuard(allowHttp: true, allowPrivateNetworks: true);
        services.AddSingleton(webhookGuard);
        services.AddHttpClient(WhatsAppCampaignApi.Services.Integrations.WebhookSender.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, ConnectCallback = webhookGuard.ConnectAsync });
        services.AddScoped<WhatsAppCampaignApi.Services.Integrations.WebhookSender>();
        services.AddSingleton<WhatsAppCampaignApi.Services.Integrations.IWebhookEmitter, WhatsAppCampaignApi.Services.Integrations.WebhookEmitter>();
        services.AddScoped<WhatsAppCampaignApi.Services.Integrations.IWebhookSubscriptionService, WhatsAppCampaignApi.Services.Integrations.WebhookSubscriptionService>();

        return new TestHarness(services.BuildServiceProvider(), configuration, connectionString);
    }

    /// <summary>Resolves a service in its own scope, as a request or a worker iteration would.</summary>
    public async Task InScopeAsync(Func<IServiceProvider, Task> body)
    {
        using var scope = _provider.CreateScope();
        await body(scope.ServiceProvider);
    }

    public IServiceProvider RootServices => _provider;

    public IServiceScope CreateScope() => _provider.CreateScope();

    // ── Direct SQL, for assertions ───────────────────────────────────────────────────────────

    public async Task<string?> ScalarAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);

        var result = await command.ExecuteScalarAsync();
        return result == DBNull.Value ? null : result?.ToString();
    }

    public async Task<int> CountAsync(string sql, params (string Name, object Value)[] parameters) =>
        int.TryParse(await ScalarAsync(sql, parameters), out var count) ? count : -1;

    public async Task ExecAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Polls until a condition holds or the timeout expires.
    ///
    /// <para>
    /// Used instead of a fixed sleep when waiting on the background workers. A fixed sleep is
    /// either too short — producing a test that fails intermittently for no reason — or long
    /// enough to make the suite tedious to run.
    /// </para>
    /// </summary>
    public async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout, TimeSpan? pollInterval = null)
    {
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(400);
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return true;
            await Task.Delay(interval);
        }

        return false;
    }

    /// <summary>
    /// Removes everything the suite created.
    ///
    /// <para>
    /// Ordered so dependents go before principals wherever a cascade does not already cover it,
    /// and written to be safe to run twice — a previous crashed run must not make the next one
    /// fail during setup.
    /// </para>
    /// </summary>
    public async Task CleanupAsync()
    {
        // Job queue rows are keyed by the suite's own queue names and campaign partitions.
        await ExecAsync("""DELETE FROM "JobQueue" WHERE "QueueName" LIKE 'zz-%'""");

        await ExecAsync("""
            DELETE FROM "JobQueue"
             WHERE "PartitionKey" IN (
                 SELECT "Id"::text FROM "Campaigns" WHERE "Name" LIKE @prefix
             )
            """, ("prefix", $"{Prefix}%"));

        await ExecAsync("""
            DELETE FROM "JobQueue"
             WHERE "IdempotencyKey" IN (
                 SELECT 'expand:campaign:' || "Id"::text FROM "Campaigns" WHERE "Name" LIKE @prefix
             )
            """, ("prefix", $"{Prefix}%"));


        await ExecAsync("""
            DELETE FROM "ChatMessages" WHERE "ContactId" IN (
                SELECT "Id" FROM "Contacts" WHERE "Name" LIKE @prefix)
            """, ("prefix", $"{Prefix}%"));
        await ExecAsync("""
            DELETE FROM "ChatConversations" WHERE "ContactId" IN (
                SELECT "Id" FROM "Contacts" WHERE "Name" LIKE @prefix)
            """, ("prefix", $"{Prefix}%"));

        await ExecAsync("""DELETE FROM "Campaigns" WHERE "Name" LIKE @prefix""", ("prefix", $"{Prefix}%"));
        await ExecAsync("""DELETE FROM "EmailTemplates" WHERE "Name" LIKE @prefix""", ("prefix", $"{Prefix}%"));
        await ExecAsync("""DELETE FROM "EmailSuppressions" WHERE "EmailAddressNormalized" LIKE @suffix""",
            ("suffix", "%@example.test"));
        await ExecAsync("""DELETE FROM "Contacts" WHERE "Name" LIKE @prefix""", ("prefix", $"{Prefix}%"));

        // Cascades take the configuration, senders, domains and quota with the connection.
        await ExecAsync("""
            DELETE FROM "EmailConfigurations" WHERE "ConnectionId" IN (
                SELECT "Id" FROM "Connections" WHERE "Name" LIKE @prefix)
            """, ("prefix", $"{Prefix}%"));
        await ExecAsync("""DELETE FROM "Connections" WHERE "Name" LIKE @prefix""", ("prefix", $"{Prefix}%"));

        await ExecAsync("""DELETE FROM "AuditLogs" WHERE "EntityName" LIKE @prefix""", ("prefix", $"{Prefix}%"));

        // WhatsApp templates a phase inserted directly. Last, because campaigns reference them.
        // Only this run's ids: a global name sweep would also remove templates other runs, or a
        // person, created.
        foreach (var id in _createdTemplateIds)
        {
            await ExecAsync("""DELETE FROM "TemplateVariables" WHERE "TemplateId" = @id""", ("id", id));
            await ExecAsync("""DELETE FROM "Templates" WHERE "Id" = @id""", ("id", id));
        }
        _createdTemplateIds.Clear();
    }

    private readonly List<int> _createdTemplateIds = [];

    /// <summary>Registers a WhatsApp template a phase inserted, so cleanup removes it.</summary>
    public void TrackTemplate(params int[] ids) => _createdTemplateIds.AddRange(ids);

    /// <summary>Every audit event the code under test raised, in order (none reach the database).</summary>
    public RecordingAuditService Audit => _provider.GetRequiredService<RecordingAuditService>();

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();
}

/// <summary>
/// Records audit events in memory instead of writing them.
///
/// <para>
/// The only substituted service in the suite. Writing would scatter rows through a shared audit
/// table that the suite has no clean way to identify and remove; recording lets a phase assert
/// that an action was audited (which event, which record, which actor) without that.
/// </para>
/// </summary>
public sealed class RecordingAuditService : IAuditService
{
    public sealed record Entry(string Event, string? EntityType, string? EntityId, int? ActorUserId, string? ActorUserName, string? Description);

    private readonly System.Collections.Concurrent.ConcurrentQueue<Entry> _entries = new();

    public IReadOnlyList<Entry> Entries => _entries.ToArray();

    public bool Has(string eventName, string? entityId = null) =>
        _entries.Any(e => e.Event == eventName && (entityId is null || e.EntityId == entityId));

    public Task LogAsync(
        string eventName,
        string category,
        string? description = null,
        string? entityType = null,
        string? entityId = null,
        int? actorUserId = null,
        string? actorUserName = null,
        AuditMetadata? metadata = null)
    {
        _entries.Enqueue(new Entry(eventName, entityType, entityId, actorUserId, actorUserName, description));
        return Task.CompletedTask;
    }
}

/// <summary>No SignalR hub in the suite; inbox pushes are not asserted on.</summary>
internal sealed class NoOpInboxNotifier : IInboxNotifier
{
    public Task MessageReceivedAsync(int conversationId, int? connectionId, CancellationToken ct = default) => Task.CompletedTask;

    public Task MessageStatusChangedAsync(int conversationId, int messageId, string status, CancellationToken ct = default) => Task.CompletedTask;

    public Task ConversationUpdatedAsync(int conversationId, int? connectionId, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>The suite acts as the system: no signed-in user, no permissions.</summary>
internal sealed class SystemCurrentUser : ICurrentUserService
{
    public int? UserId => null;
    public string? Email => null;
    public string? UserName => "email-channel-tests";
    public bool IsAdministrator => false;
    public bool IsAuthenticated => false;
    public string? IpAddress => null;
    public string? UserAgent => null;
    public Task<bool> HasPermissionAsync(string permissionKey) => Task.FromResult(false);
    public Task<bool> HasAnyPermissionAsync(IEnumerable<string> permissionKeys) => Task.FromResult(false);
    public Task<List<string>> GetPermissionsAsync() => Task.FromResult(new List<string>());
}

/// <summary>Content root = the Backend directory, so storage paths resolve as they do for the API.</summary>
internal sealed class TestHostEnvironment(string contentRoot) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "WhatsAppCampaignApi";
    public string ContentRootPath { get; set; } = contentRoot;
    public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRoot);
}

/// <summary>
/// Stands in for a service the suite deliberately does not wire (WhatsApp, Chat). Resolvable, so
/// a class that merely depends on it can be built, but any call fails with a clear message rather
/// than a NullReferenceException — reaching it from an email path is itself a bug.
/// </summary>
internal class UnavailableService : System.Reflection.DispatchProxy
{
    private string _name = "service";

    public static T Create<T>() where T : class
    {
        var proxy = Create<T, UnavailableService>();
        ((UnavailableService)(object)proxy)._name = typeof(T).Name;
        return proxy;
    }

    protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args) =>
        throw new NotSupportedException($"{_name}.{targetMethod?.Name} is not available in the email test suite.");
}
