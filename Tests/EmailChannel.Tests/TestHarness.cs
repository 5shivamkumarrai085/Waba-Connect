using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Activity;
using WhatsAppCampaignApi.Models.Options;
using WhatsAppCampaignApi.Services;
using WhatsAppCampaignApi.Services.Email;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Services.Queue;

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
            // The channel must be on for the pipeline to run at all, and the unsubscribe signer
            // refuses to issue tokens without a key. Supplied here rather than in appsettings so
            // no test value is ever committed, and so running the suite cannot change how the
            // application itself is configured.
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:Enabled"] = "true",
                ["Email:Unsubscribe:SigningKey"] = "integration-test-signing-key-not-a-real-secret",
                ["Email:Unsubscribe:PublicBaseUrl"] = "https://example.test",
                ["Email:Dispatch:DefaultSendRatePerSecond"] = "50",
                // Tightened so the workers react quickly; production values would make the suite
                // spend most of its time asleep.
                ["Email:Queue:PollIntervalMs"] = "100",
                ["Email:Queue:IdleBackoffMs"] = "300"
            })
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("No DefaultConnection connection string was found.");

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
        services.AddDbContextFactory<AppDbContext>(o => o.UseNpgsql(connectionString));
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());
        services.AddOptions<EmailOptions>().Bind(configuration.GetSection(EmailOptions.SectionName));

        services.AddScoped<IEncryptionService, EncryptionService>();
        services.AddScoped<IAuditService, NoOpAuditService>();

        services.AddSingleton<IMimeMessageBuilder, MimeMessageBuilder>();
        services.AddScoped<IEmailProvider, SesEmailProvider>();
        services.AddScoped<IEmailProvider, SmtpEmailProvider>();
        services.AddScoped<IEmailProviderFactory, EmailProviderFactory>();
        services.AddScoped<IEmailConnectionService, EmailConnectionService>();
        services.AddScoped<IEmailTemplateService, EmailTemplateService>();
        services.AddScoped<IEmailDomainService, EmailDomainService>();
        services.AddScoped<IEmailSuppressionService, EmailSuppressionService>();
        services.AddScoped<IEmailSendRecorder, EmailSendRecorder>();
        services.AddScoped<IEmailCampaignDispatcher, EmailCampaignDispatcher>();
        services.AddScoped<ICampaignExecutionGate, AutoApproveCampaignExecutionGate>();
        services.AddSingleton<IUnsubscribeTokenService, UnsubscribeTokenService>();
        services.AddSingleton<IEmailRateLimiter, PostgresEmailRateLimiter>();
        services.AddSingleton<IJobQueue, PostgresJobQueue>();
        services.AddScoped<IEmailEventProcessor, EmailEventProcessor>();
        services.AddScoped<ISnsMessageValidator, SnsMessageValidator>();
        services.AddHttpClient(SnsMessageValidator.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddMemoryCache();

        // CampaignService takes both channels' collaborators. Nothing here exercises the WhatsApp
        // half — and that is itself part of what the suite checks, since an email campaign that
        // reached IWhatsAppService would throw rather than pass quietly.
        services.AddScoped<IWhatsAppService>(_ => null!);
        services.AddScoped<IChatService>(_ => null!);
        services.AddScoped<ICampaignService, CampaignService>();

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

        await ExecAsync("""DELETE FROM "MessageActivityLogs" WHERE "Name" LIKE @prefix""", ("prefix", $"{Prefix}%"));

        // Delivery events survive their campaign on purpose — the FKs are SetNull, because the
        // event log is the record of what a provider told us and stays true after the campaign is
        // gone. So they are removed by their own test markers rather than by cascade. example.test
        // is a reserved test domain, so this cannot match real traffic.
        await ExecAsync("""
            DELETE FROM "EmailDeliveryEvents"
             WHERE "RecipientAddress" LIKE '%@example.test'
                OR "ProviderMessageId" LIKE 'ses-%'
                OR "ProviderMessageId" LIKE 'unknown-%'
            """);

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
    }

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();
}

/// <summary>
/// Swallows audit writes.
///
/// <para>
/// The only substituted service in the suite. Auditing is a fire-and-forget side effect that no
/// test asserts on, and letting it run would scatter rows through a shared audit table that the
/// suite has no clean way to identify and remove.
/// </para>
/// </summary>
internal sealed class NoOpAuditService : IAuditService
{
    public Task LogAsync(
        string eventName,
        string category,
        string? description = null,
        string? entityType = null,
        string? entityId = null,
        int? actorUserId = null,
        string? actorUserName = null,
        AuditMetadata? metadata = null) => Task.CompletedTask;
}
