using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Executors;
using WhatsAppCampaignApi.Models.DTOs.Activity;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Models.Options;
using WhatsAppCampaignApi.Services;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Services.Queue;
using WhatsAppCampaignApi.Services.Remittance;
using WhatsAppCampaignApi.Services.Remittance.Providers;
using WhatsAppCampaignApi.Services.WhatsApp;
using WhatsAppCampaignApi.Services.WhatsApp.Flows;

namespace Remittance.Tests;

/// <summary>
/// The Send Money service graph, wired as Program.cs wires it, against the real database. Only the
/// outside world is faked: WhatsApp sends are recorded instead of made, and exchange rates come
/// from <see cref="FakeExchangeRateProvider"/> so every pricing path can be steered.
/// </summary>
public sealed class RemittanceHarness : IAsyncDisposable
{
    public const string Prefix = "zz-remittest";

    /// <summary>Bot tests use Ofcom's drama range (+44 7700 900xxx), which never reaches a real phone.</summary>
    public const string BotPhonePrefix = "4477009007";
    public const string SourceCountry = "ZZ";
    public const string PaymentSecret = "zz-remittest-payment-secret-not-a-real-secret";
    public const string PayoutSecret = "zz-remittest-payout-secret-not-a-real-secret";
    public const string AppSecret = "zz-remittest-app-secret-not-a-real-secret";

    private readonly ServiceProvider _provider;

    public string ConnectionString { get; }
    public string Tag { get; } = Guid.NewGuid().ToString("N")[..8];
    public RecordingWhatsApp WhatsApp { get; }

    // Seeded fixture ids.
    public int ConnectionId { get; private set; }
    public string EndpointKey { get; private set; } = string.Empty;
    public string PublicKeyPem { get; private set; } = string.Empty;
    public int CorridorId { get; private set; }
    public int BankMethodId { get; private set; }
    public int InactiveMethodId { get; private set; }

    private RemittanceHarness(ServiceProvider provider, string connectionString, RecordingWhatsApp whatsApp)
    {
        _provider = provider;
        ConnectionString = connectionString;
        WhatsApp = whatsApp;
    }

    public static RemittanceHarness Create(string backendDirectory)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(backendDirectory)
            .AddJsonFile("appsettings.json")
            // The git-ignored developer secrets file first, then user-secrets: sandboxed terminals
            // often cannot read %APPDATA%, where user-secrets live.
            .AddJsonFile("appsettings.secrets.local.json", optional: true)
            .AddUserSecrets(typeof(AppDbContext).Assembly, optional: true)
            .AddEnvironmentVariables()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:PublicBaseUrl"] = "https://example.test",
                ["WhatsApp:AppSecret"] = AppSecret,
                ["Remittance:Enabled"] = "true",
                ["Remittance:ExchangeRates:Provider"] = FakeExchangeRateProvider.Name,
                ["Remittance:ExchangeRates:Fallbacks:0"] = FakeExchangeRateProvider.FallbackName,
                ["Remittance:ExchangeRates:CacheSeconds"] = "0",
                ["Remittance:ExchangeRates:MaxRateAgeHours"] = "26",
                ["Remittance:Otp:Provider"] = "Mock",
                ["Remittance:Otp:Length"] = "6",
                ["Remittance:Otp:MaxAttempts"] = "5",
                ["Remittance:Otp:ResendCooldownSeconds"] = "30",
                // The suite sends many codes to its few fictional numbers.
                ["Remittance:Otp:MaxPerDestinationPerHour"] = "1000",
                ["Remittance:Otp:MaxPerDestinationPerDay"] = "10000",
                ["Remittance:MaxLaunchesPerTenMinutes"] = "100",
                ["Remittance:Otp:Mock:ExposeCodeInFlow"] = "true",
                ["Remittance:Payments:Provider"] = "Mock",
                ["Remittance:Payments:Mock:WebhookSecret"] = PaymentSecret,
                ["Remittance:Payouts:Provider"] = "Mock",
                ["Remittance:Payouts:Mock:WebhookSecret"] = PayoutSecret,
                ["Remittance:Flows:AllowDraftSend"] = "true"
            })
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("No DefaultConnection connection string was found (user-secrets or Backend/appsettings.secrets.local.json).");
        SecretCipher.Initialize(configuration["Encryption:Key"]);

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = null; }).SetMinimumLevel(LogLevel.Error));
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IWebHostEnvironment>(new DevelopmentEnvironment(backendDirectory));
        services.AddSingleton<IHostEnvironment>(sp => sp.GetRequiredService<IWebHostEnvironment>());

        var dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();
        services.AddSingleton(dataSource);
        // Same strictness as Program.cs in Development: a query that loads two collections without
        // .AsSplitQuery() throws, so the suite catches it instead of the running app.
        var hotPath = new HotPathCache(new MemoryCache(new MemoryCacheOptions()));
        services.AddSingleton(hotPath);
        services.AddSingleton<HotPathCacheInvalidator>();
        services.AddDbContextFactory<AppDbContext>((sp, o) => o
            .AddInterceptors(sp.GetRequiredService<HotPathCacheInvalidator>())
            .UseNpgsql(dataSource)
            .ConfigureWarnings(w => w.Throw(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.MultipleCollectionIncludeWarning)));
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());
        services.AddMemoryCache();

        services.AddOptions<EmailOptions>().Bind(configuration.GetSection(EmailOptions.SectionName));
        services.AddOptions<RemittanceOptions>().Bind(configuration.GetSection(RemittanceOptions.SectionName));
        services.AddSingleton<IJobQueue, PostgresJobQueue>();
        services.AddScoped<IAuditService, NullAuditService>();

        var whatsApp = RecordingWhatsApp.Create();
        services.AddSingleton(whatsApp.AsService);
        services.AddScoped<IWhatsAppWebhookSignatureVerifier, WhatsAppWebhookSignatureVerifier>();

        // Exchange rates: the steerable fakes, plus the real ExchangeRate-API adapter for phase 6.
        services.AddSingleton<IExchangeRateProvider>(new FakeExchangeRateProvider(FakeExchangeRateProvider.Name));
        services.AddSingleton<IExchangeRateProvider>(new FakeExchangeRateProvider(FakeExchangeRateProvider.FallbackName));
        services.AddHttpClient<ExchangeRateApiProvider>();
        services.AddHttpClient<FrankfurterProvider>();

        services.AddScoped<IOtpProvider, MockOtpProvider>();
        services.AddScoped<IPaymentProvider, MockPaymentProvider>();
        services.AddScoped<IPayoutProvider, MockPayoutProvider>();
        services.AddScoped<IRemittanceProviderRegistry, RemittanceProviderRegistry>();
        services.AddScoped<IExchangeRateService, ExchangeRateService>();
        services.AddScoped<IOtpService, OtpService>();
        services.AddScoped<IQuoteService, QuoteService>();
        services.AddScoped<IBeneficiaryService, BeneficiaryService>();
        services.AddScoped<IRemittanceNotifier, RemittanceNotifier>();
        services.AddScoped<ITransferOrderService, TransferOrderService>();
        services.AddScoped<IOrderCreatedNotification, OrderCreatedNotification>();
        services.AddScoped<IRemittanceSessionService, RemittanceSessionService>();
        services.AddScoped<IRemittanceCallbackIngestion, RemittanceCallbackIngestion>();
        services.AddScoped<IRemittanceAdminService, RemittanceAdminService>();
        services.AddScoped<IFlowScreenHandler, SendMoneyFlowHandler>();
        services.AddScoped<IWhatsAppFlowEndpoint, WhatsAppFlowEndpoint>();
        services.AddScoped<IWhatsAppFlowCompletionHandler, WhatsAppFlowCompletionHandler>();
        services.AddScoped<IWhatsAppFlowLauncher, WhatsAppFlowLauncher>();

        // The bot engine (phase 7): the real flow engine and the executors its test flows use.
        services.AddScoped<IFlowLoaderService, FlowLoaderService>();
        services.AddScoped<IConversationStateService, ConversationStateService>();
        services.AddScoped<IFlowExecutionService, FlowExecutionService>();
        services.AddScoped<IBotSessionCleanup, BotSessionCleanup>();
        services.AddScoped<IBotFlowService, BotFlowService>();
        services.AddScoped<INodeExecutor, StartTriggerExecutor>();
        services.AddScoped<INodeExecutor, TextNodeExecutor>();
        services.AddScoped<INodeExecutor, ButtonNodeExecutor>();
        services.AddScoped<INodeExecutor, WhatsAppFlowNodeExecutor>();

        return new RemittanceHarness(services.BuildServiceProvider(), connectionString, whatsApp);
    }

    public AsyncServiceScope CreateScope() => _provider.CreateAsyncScope();

    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = CreateScope();
        return await work(scope.ServiceProvider);
    }

    public T Get<T>() where T : notnull => _provider.GetRequiredService<T>();

    // ── fixture ──────────────────────────────────────────────────────────────────────────────

    /// <summary>A connection with a Flow registration and key pair, a ZZ → QA corridor, fields, banks, fees, lists, payment methods.</summary>
    public async Task SeedAsync()
    {
        await using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var connection = new Connection { Name = $"{Prefix}-conn-{Tag}", Nickname = "ZZRT", IsActive = true };
        db.Connections.Add(connection);
        await db.SaveChangesAsync();
        ConnectionId = connection.Id;

        var pair = FlowCrypto.GenerateKeyPair();
        PublicKeyPem = pair.PublicKeyPem;
        EndpointKey = $"zzrt{Tag}";
        db.WhatsAppFlowEncryptionKeys.Add(new WhatsAppFlowEncryptionKey { ConnectionId = ConnectionId, PublicKeyPem = pair.PublicKeyPem, PrivateKeyPem = pair.PrivateKeyPem });
        db.WhatsAppFlowRegistrations.Add(new WhatsAppFlowRegistration
        {
            ConnectionId = ConnectionId, Purpose = WhatsAppFlowRegistration.SendMoneyPurpose, Name = "Send Money (test)",
            EndpointKey = EndpointKey, MetaFlowId = "zz-test-flow", Status = "DRAFT"
        });

        var corridor = new RemittanceCorridor
        {
            SourceCountryCode = SourceCountry, SourceCurrency = "MYR", DestinationCountryCode = "QA", DestinationCountryName = "zz Testland",
            DestinationCurrency = "IDR", DestinationDecimals = 0, MinSendAmount = 20m, MaxSendAmount = 20_000m, FxMarginBps = 150, SortOrder = -100
        };
        var bank = new RemittancePayoutMethod
        {
            Code = "BANK_TRANSFER", Name = "zz Bank Transfer", DeliveryEta = "Instant", PayoutProvider = MockPayoutProvider.Name, SortOrder = 0,
            FeeRule = new RemittanceFeeRule { FixedFee = 4m, PercentFee = 0.5m, MinFee = 5m, MaxFee = 50m, FirstTransferFee = 0m, PromoLabel = "PROMO" },
            Fields =
            {
                new RemittanceBeneficiaryField { Key = "account_number", Label = "Account number", InputType = RemittanceFieldInputType.Number, MinLength = 6, MaxLength = 12, IsAccountIdentifier = true, SortOrder = 0 },
                new RemittanceBeneficiaryField { Key = "branch_code", Label = "Branch code", InputType = RemittanceFieldInputType.Text, Pattern = "[A-Z]{4}[0-9]{2}", HelperText = "e.g. ABCD12", SortOrder = 1 },
                new RemittanceBeneficiaryField { Key = "nickname", Label = "Nickname", IsRequired = false, IsActive = false, SortOrder = 2 }
            },
            Institutions =
            {
                new RemittanceInstitution { Code = "BANKA", Name = "zz Bank A", SortOrder = 0 },
                new RemittanceInstitution { Code = "BANKB", Name = "zz Bank B", SortOrder = 1 },
                new RemittanceInstitution { Code = "OLD", Name = "zz Closed Bank", IsActive = false, SortOrder = 2 }
            }
        };
        var inactive = new RemittancePayoutMethod { Code = "OLD_METHOD", Name = "zz Retired method", PayoutProvider = MockPayoutProvider.Name, IsActive = false, SortOrder = 1 };
        corridor.PayoutMethods.Add(bank);
        corridor.PayoutMethods.Add(inactive);
        db.RemittanceCorridors.Add(corridor);

        db.RemittanceLookups.AddRange(
            new RemittanceLookup { Category = RemittanceLookup.SourceOfFunds, Code = "zz_rt_salary", Label = "zz Salary" },
            new RemittanceLookup { Category = RemittanceLookup.Purpose, Code = "zz_rt_family", Label = "zz Family support" },
            new RemittanceLookup { Category = RemittanceLookup.Relationship, Code = "zz_rt_friend", Label = "zz Friend" });

        db.RemittancePaymentMethods.AddRange(
            new RemittancePaymentMethod { SourceCountryCode = SourceCountry, Provider = MockPaymentProvider.Name, Code = "FPX", Label = "zz FPX Online", Badge = "INSTANT", PaymentWindowMinutes = 15 },
            new RemittancePaymentMethod { SourceCountryCode = SourceCountry, Provider = MockPaymentProvider.Name, Code = "COUNTER", Label = "zz Counter", PaymentWindowMinutes = 1440, SortOrder = 1 });

        await db.SaveChangesAsync();
        FakeExchangeRateProvider.Set(FakeExchangeRateProvider.Name, "MYR", "IDR", () => FakeExchangeRateProvider.Ok(FakeExchangeRateProvider.Name, 3640m));
        CorridorId = corridor.Id;
        BankMethodId = bank.Id;
        InactiveMethodId = inactive.Id;
    }

    /// <summary>A fresh test contact (fake, never-registered number) and a started session.</summary>
    public async Task<(int ContactId, Guid SessionId, string FlowToken)> NewSessionAsync()
    {
        await using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var phone = $"+6099{Random.Shared.Next(1_000_000, 9_999_999)}";
        var contact = new Contact { Name = $"{Prefix}-{Tag}", Phone = phone, IsActive = true, Source = "WhatsApp" };
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();

        var (session, token) = await scope.ServiceProvider.GetRequiredService<IRemittanceSessionService>()
            .StartAsync(contact.Id, phone.TrimStart('+'), ConnectionId, CancellationToken.None);
        return (contact.Id, session.Id, token);
    }

    /// <summary>Runs one plaintext data-exchange request through the real endpoint dispatch.</summary>
    public Task<(int Status, JsonObject? Body)> ExchangeAsync(string flowToken, string action, string? screen = null, JsonObject? data = null) =>
        InScopeAsync(sp =>
        {
            var request = new JsonObject { ["version"] = "3.0", ["action"] = action, ["flow_token"] = flowToken };
            if (screen is not null) request["screen"] = screen;
            if (data is not null) request["data"] = data;
            return sp.GetRequiredService<IWhatsAppFlowEndpoint>().HandlePlaintextAsync(EndpointKey, request, CancellationToken.None);
        });

    // ── SQL helpers ──────────────────────────────────────────────────────────────────────────

    public async Task<object?> ScalarAsync(string sql, params object[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        for (var i = 0; i < parameters.Length; i++) command.Parameters.AddWithValue($"p{i}", parameters[i]);
        var result = await command.ExecuteScalarAsync();
        return result is DBNull ? null : result;
    }

    public async Task<int> ExecAsync(string sql, params object[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        for (var i = 0; i < parameters.Length; i++) command.Parameters.AddWithValue($"p{i}", parameters[i]);
        var changed = await command.ExecuteNonQueryAsync();

        // Raw SQL bypasses the save interceptor, so a test that edits configuration this way
        // clears the hot-path cache itself, as an admin save would.
        var cache = _provider.GetRequiredService<HotPathCache>();
        foreach (var region in new[] { HotPathCache.BotFlows, HotPathCache.Bots, HotPathCache.Connections, HotPathCache.SendMoney }) cache.Invalidate(region);
        return changed;
    }

    public async Task CleanupAsync()
    {
        const string contacts = $"SELECT \"Id\" FROM \"Contacts\" WHERE \"Name\" LIKE '{Prefix}%'";
        const string sessions = $"SELECT \"Id\" FROM \"RemittanceSessions\" WHERE \"ContactId\" IN ({contacts})";
        const string orders = $"SELECT \"Id\" FROM \"TransferOrders\" WHERE \"ContactId\" IN ({contacts})";
        const string connections = $"SELECT \"Id\" FROM \"Connections\" WHERE \"Name\" LIKE '{Prefix}%'";

        string[] statements =
        [
            $"DELETE FROM \"JobQueue\" WHERE \"QueueName\" LIKE 'zz-%'",
            $"DELETE FROM \"ConversationStates\" WHERE \"PhoneNumber\" LIKE '{BotPhonePrefix}%'",
            $"DELETE FROM \"BotSuppressions\" WHERE \"PhoneNumber\" LIKE '{BotPhonePrefix}%'",
            $"DELETE FROM \"Messages\" WHERE \"PhoneNumber\" LIKE '{BotPhonePrefix}%'",
            $"DELETE FROM \"BotFlows\" WHERE \"Name\" LIKE '{Prefix}%'",
            $"DELETE FROM \"JobQueue\" WHERE \"IdempotencyKey\" LIKE '%zzrt%' OR \"IdempotencyKey\" IN (SELECT 'payout:' || \"Id\" FROM \"TransferOrders\" WHERE \"ContactId\" IN ({contacts}))",
            $"DELETE FROM \"TransferOrderEvents\" WHERE \"TransferOrderId\" IN ({orders})",
            $"DELETE FROM \"TransferOrders\" WHERE \"ContactId\" IN ({contacts})",
            $"DELETE FROM \"FxQuotes\" WHERE \"SessionId\" IN ({sessions})",
            $"DELETE FROM \"OtpChallenges\" WHERE \"SubjectKey\" IN (SELECT \"Id\"::text FROM \"RemittanceSessions\" WHERE \"ContactId\" IN ({contacts})) OR \"Purpose\" LIKE 'zz-%'",
            $"DELETE FROM \"RemittanceSessions\" WHERE \"ContactId\" IN ({contacts})",
            $"DELETE FROM \"RemittanceBeneficiaries\" WHERE \"ContactId\" IN ({contacts})",
            $"DELETE FROM \"ChatMessages\" WHERE \"ContactId\" IN ({contacts})",
            $"DELETE FROM \"ChatConversations\" WHERE \"ContactId\" IN ({contacts})",
            $"DELETE FROM \"Contacts\" WHERE \"Name\" LIKE '{Prefix}%'",
            $"DELETE FROM \"RemittanceCorridors\" WHERE \"SourceCountryCode\" = '{SourceCountry}'",
            $"DELETE FROM \"RemittancePaymentMethods\" WHERE \"SourceCountryCode\" = '{SourceCountry}'",
            "DELETE FROM \"RemittanceLookups\" WHERE \"Code\" LIKE 'zz_rt_%'",
            $"DELETE FROM \"WhatsAppFlowRegistrations\" WHERE \"ConnectionId\" IN ({connections})",
            $"DELETE FROM \"WhatsAppFlowEncryptionKeys\" WHERE \"ConnectionId\" IN ({connections})",
            $"DELETE FROM \"Connections\" WHERE \"Name\" LIKE '{Prefix}%'"
        ];
        foreach (var sql in statements) await ExecAsync(sql);
    }

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();
}

/// <summary>Exchange rates the tests control: a rate per pair, or a forced failure.</summary>
public sealed class FakeExchangeRateProvider(string name) : IExchangeRateProvider
{
    public const string Name = "FakeFx";
    public const string FallbackName = "FakeFxFallback";

    public static readonly ConcurrentDictionary<string, Func<ExchangeRateResult>> Behaviour = new();

    public string ProviderName => name;

    public Task<ExchangeRateResult> GetRateAsync(string from, string to, CancellationToken ct)
    {
        var key = $"{name}:{from}:{to}";
        return Task.FromResult(Behaviour.TryGetValue(key, out var behaviour)
            ? behaviour()
            : ExchangeRateResult.Unsupported(name, "not configured in the test"));
    }

    public static void Set(string provider, string from, string to, Func<ExchangeRateResult> behaviour) => Behaviour[$"{provider}:{from}:{to}"] = behaviour;
    public static ExchangeRateResult Ok(string provider, decimal rate, DateTime? asOf = null) => new(ExchangeRateOutcome.Ok, provider, rate, asOf ?? DateTime.UtcNow);
}

/// <summary>Records every WhatsApp send instead of making it, and reports it as sent.</summary>
public class RecordingWhatsApp : DispatchProxy
{
    public ConcurrentQueue<(string Method, string Phone, string Payload)> Sent { get; } = new();

    public static RecordingWhatsApp Create() => (RecordingWhatsApp)(object)Create<IWhatsAppService, RecordingWhatsApp>();

    public IWhatsAppService AsService => (IWhatsAppService)(object)this;

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        switch (method?.Name)
        {
            case nameof(IWhatsAppService.SendCustomPayloadAsync):
                Sent.Enqueue((method.Name, (string)args![0]!, System.Text.Json.JsonSerializer.Serialize(args[1])));
                return Task.FromResult(WhatsAppSendResult.Sent($"wamid.zzrt.{Guid.NewGuid():N}"));
            case nameof(IWhatsAppService.SendTextMessageAsync):
                Sent.Enqueue((method.Name, (string)args![0]!, (string)args[1]!));
                return Task.FromResult(WhatsAppSendResult.Sent($"wamid.zzrt.{Guid.NewGuid():N}"));
            default:
                throw new NotSupportedException($"IWhatsAppService.{method?.Name} is not used by Send Money.");
        }
    }
}

internal sealed class NullAuditService : IAuditService
{
    public Task LogAsync(string eventName, string category, string? description = null, string? entityType = null, string? entityId = null,
        int? actorUserId = null, string? actorUserName = null, AuditMetadata? metadata = null) => Task.CompletedTask;
}

internal sealed class DevelopmentEnvironment(string root) : IWebHostEnvironment
{
    public string WebRootPath { get; set; } = Path.Combine(root, "wwwroot");
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string ApplicationName { get; set; } = "WhatsAppCampaignApi";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string ContentRootPath { get; set; } = root;
    public string EnvironmentName { get; set; } = Environments.Development;
}
