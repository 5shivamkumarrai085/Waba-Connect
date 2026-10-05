using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Data.Seed;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Middleware;
using WhatsAppCampaignApi.Models.Options;
using WhatsAppCampaignApi.Services;
using WhatsAppCampaignApi.Services.Email;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Services.Queue;
using WhatsAppCampaignApi.Validators;
using WhatsAppCampaignApi.Executors;
using WhatsAppCampaignApi.Hubs;

// Log output contains non-ASCII text (dashes, names); without this the Windows console shows mojibake.
Console.OutputEncoding = Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

// Secrets are not in appsettings.json. Load the developer's user-secrets in any non-Production
// environment (the host alone loads them only for "Development", and only via APPDATA), then fail
// with a diagnosis — environment, locations checked, fix — if any required secret is still missing.
StartupConfiguration.AddDeveloperSecrets(builder, args);
StartupConfiguration.RequireSecrets(builder.Configuration, builder.Environment);

// The unsubscribe signing key, derived when nobody configured one.
//
// This key signs the one-click unsubscribe tokens in outgoing mail. It has to be secret, and it
// has to be stable — regenerating it per process would invalidate every unsubscribe link already
// sitting in somebody's inbox — but nothing requires a human to pick it.
//
// So it is derived from the encryption key this application already manages, with HKDF and a
// purpose string. Domain separation means the derived key reveals nothing about the encryption
// key and cannot be substituted for it. One managed secret instead of two, and a deployment can
// no longer fail to boot over a value it never knew it had to set.
//
// An explicit Email:Unsubscribe:SigningKey still wins, for deployments that rotate the two
// independently.
StartupConfiguration.DeriveSecret(builder.Configuration, "Email:Unsubscribe:SigningKey", "WabaConnect.Email.Unsubscribe.v1");
StartupConfiguration.DeriveSecret(builder.Configuration, "Email:Tracking:SigningSecret", "WabaConnect.Email.Tracking.v1");

// One public base URL for every link that leaves the building: tracking pixels, click redirects,
// unsubscribe links and media URLs handed to Meta. The per-feature keys still win when set, so an
// existing deployment keeps working, but a new one configures App:PublicBaseUrl once.
StartupConfiguration.DefaultFrom(builder.Configuration, "Email:Tracking:BaseUrl", "App:PublicBaseUrl");
StartupConfiguration.DefaultFrom(builder.Configuration, "Email:Unsubscribe:PublicBaseUrl", "App:PublicBaseUrl");

// Refuse to start a production host on committed or placeholder secrets. A bank deployment that
// boots with the sample JWT key or admin password is worse than one that does not boot.
StartupConfiguration.ValidateProductionSecrets(builder.Configuration, builder.Environment);

// Field encryption key, needed before any credential is read — including by the EF converters
// that decrypt WABA tokens. Fails the boot when Encryption:Key is missing.
SecretCipher.Initialize(builder.Configuration["Encryption:Key"]);

// 1. Serilog configuration
// The file sink writes compact JSON (CLEF) rather than plain text so the Setup > System Logs
// viewer can parse entries back into structured records (level, timestamp, properties,
// exception) instead of regex-scraping rendered strings. `shared: true` is required because
// CampaignSchedulerService writes concurrently with the request pipeline.
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName)
    // Phone numbers and email addresses are masked in every sink (see PiiMaskingEnricher).
    .Enrich.With(new PiiMaskingEnricher())
    .WriteTo.Console()
    .WriteTo.File(
        new CompactJsonFormatter(),
        Path.Combine(AppContext.BaseDirectory, "Logs", "waba-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 31,
        fileSizeLimitBytes: 50L * 1024 * 1024,
        rollOnFileSizeLimit: true,
        shared: true)
    .CreateLogger();

builder.Host.UseSerilog();

// 2. Add Database Context (PostgreSQL/NeonDB)
// Registered as a factory (not AddDbContext) so services that need several independent queries running
// concurrently can create their own short-lived contexts on demand (DbContext itself isn't thread-safe).
// AddDbContext and AddDbContextFactory can't coexist for the same context (their DbContextOptions
// lifetimes conflict), so the regular scoped AppDbContext used everywhere else is derived from the factory.
//
// One NpgsqlDataSource for the whole process: EF, the job queue and the rate limiter share its
// connection pool instead of each building connections from a string. Pool size, timeouts and
// keep-alive come from the connection string (Maximum Pool Size, Timeout, Command Timeout,
// Keepalive), so they are tuned per deployment rather than in code.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection is not set. Provide it through an environment variable "
      + "(ConnectionStrings__DefaultConnection), user-secrets in development, or your secret store.");
}

var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(connectionString);
// Versioned so a session from a build on another queue contract shows up as foreign in
// pg_stat_activity (QueueHealthCheck); every build used to report the same name.
dataSourceBuilder.ConnectionStringBuilder.ApplicationName ??= $"WabaConnect/{WhatsAppCampaignApi.Services.Queue.QueueNames.ContractVersion}";
var npgsqlDataSource = dataSourceBuilder.Build();
builder.Services.AddSingleton(npgsqlDataSource);

builder.Services.AddDbContextFactory<AppDbContext>(options =>
{
    options.UseNpgsql(npgsqlDataSource, npgsql =>
    {
        // Retries transient failures (dropped connection, failover, serialization conflict) with
        // backoff. Safe here because nothing opens an explicit transaction outside an execution
        // strategy; one that does must wrap it in Database.CreateExecutionStrategy().
        npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
        npgsql.CommandTimeout(builder.Configuration.GetValue("Database:CommandTimeoutSeconds", 60));
    });

    // Query splitting is deliberately NOT global. It trades one wide result set for one round
    // trip per collection, which is a win on a local database and a loss on this one — the
    // database is a remote Neon instance, so every extra round trip costs ~250 ms of pure
    // network latency. Setting it globally made every list endpoint 2-3x slower.
    //
    // The three queries that genuinely include two collections at the same level opt in with
    // an explicit .AsSplitQuery(): CampaignService.GetByIdAsync, its scheduler equivalent, and
    // RoleService.GetByIdAsync.
    //
    // In Development a query that includes two collections without opting in throws rather
    // than silently returning a cartesian product, so a future one cannot slip through.
    if (builder.Environment.IsDevelopment())
    {
        options.ConfigureWarnings(w => w.Throw(RelationalEventId.MultipleCollectionIncludeWarning));
    }
});
builder.Services.AddScoped<AppDbContext>(sp =>
{
    var dbContext = sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext();
    // Only the request-scoped context feeds the audit trail. Contexts created directly from the
    // factory are short-lived read helpers; auditing their change tracker would attribute another
    // operation's work to whichever audit entry happened to be written next.
    dbContext.AuditChangeSink = sp.GetRequiredService<IAuditChangeBuffer>();
    return dbContext;
});

builder.Services.AddMemoryCache();

// Required for resolving the current user from the request. Note that two send paths
// (CampaignSchedulerService and the Meta webhook) have no HttpContext at all, so anything
// reading from this must tolerate a null context rather than throwing.
builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<IDashboardCacheService, DashboardCacheService>();
builder.Services.AddScoped<IReportingService, ReportingService>();
builder.Services.AddScoped<IReportQueryService, ReportQueryService>();
builder.Services.AddScoped<IOmniSettingsService, OmniSettingsService>();
builder.Services.AddScoped<IReportExportService, ReportExportService>();

// QuestPDF's Community licence, set once here rather than per export — the setting is a static
// process-wide flag, so setting it on every request call would be redundant work repeated on
// every single PDF export.
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

// 3. Add Services
builder.Services.AddScoped<IContactService, ContactService>();
builder.Services.AddScoped<IContactGroupService, ContactGroupService>();
builder.Services.AddScoped<ITemplateService, TemplateService>();
builder.Services.AddScoped<ICampaignService, CampaignService>();
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddScoped<IMessageBotService, MessageBotService>();
builder.Services.AddScoped<ITemplateBotService, TemplateBotService>();
builder.Services.AddScoped<IBotFlowService, BotFlowService>();
builder.Services.AddScoped<IFlowLoaderService, FlowLoaderService>();
builder.Services.AddScoped<IConversationStateService, ConversationStateService>();
builder.Services.AddScoped<IFlowExecutionService, FlowExecutionService>();

// Register Node Executors
builder.Services.AddScoped<INodeExecutor, StartTriggerExecutor>();
builder.Services.AddScoped<INodeExecutor, TextNodeExecutor>();
builder.Services.AddScoped<INodeExecutor, ListNodeExecutor>();
builder.Services.AddScoped<INodeExecutor, ButtonNodeExecutor>();
builder.Services.AddScoped<INodeExecutor, MediaNodeExecutor>();
builder.Services.AddScoped<INodeExecutor, LocationNodeExecutor>();
builder.Services.AddScoped<INodeExecutor, ContactCardNodeExecutor>();
builder.Services.AddScoped<INodeExecutor, AIAssistantExecutor>();
builder.Services.AddScoped<INodeExecutor, CallToActionExecutor>();

builder.Services.AddScoped<IEncryptionService, EncryptionService>();
builder.Services.AddHostedService<CredentialEncryptionMigrator>();

// File storage: the single gatekeeper for uploads and attachment reads (path confinement, content
// allow-list, SSRF-safe remote fetches). Redirects are not followed, so an allow-listed host cannot
// bounce a fetch to an internal address.
builder.Services.AddOptions<WhatsAppCampaignApi.Services.Storage.StorageOptions>()
    .Bind(builder.Configuration.GetSection(WhatsAppCampaignApi.Services.Storage.StorageOptions.SectionName));
builder.Services.AddSingleton<WhatsAppCampaignApi.Services.Storage.IFileStorage, WhatsAppCampaignApi.Services.Storage.FileStorage>();
builder.Services.AddHttpClient(WhatsAppCampaignApi.Services.Storage.FileStorage.HttpClientName)
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

// ---- Email channel configuration -------------------------------------------------------------
// Bound and validated once at startup, unlike the inline IConfiguration reads used elsewhere in
// this file. The email subsystem has knobs whose wrong values are silent rather than loud — a
// visibility timeout shorter than a send produces duplicate mail, a missing signing key produces
// unsubscribe links that no recipient can use — so they are worth failing the boot over.
builder.Services.AddOptions<EmailOptions>()
    .Bind(builder.Configuration.GetSection(EmailOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(options =>
    {
        // Everything below only matters once the channel is actually switched on, so a developer
        // machine with no email configuration still starts.
        if (!options.Enabled) return true;

        if (options.Queue.MaxBackoffSeconds < options.Queue.BaseBackoffSeconds) return false;

        // A per-partition cap above the batch size is not wrong, just inert — but it means
        // somebody intended fairness and did not get it.
        if (options.Queue.PerPartitionCap > options.Queue.ClaimBatchSize) return false;

        // Unsubscribe is a legal requirement for bulk mail, not a feature: without a signing key
        // and a reachable base URL there is no working opt-out, so refuse to start.
        //
        // SigningKey is derived above when unset, so reaching this means Encryption:Key is also
        // missing — which would break far more than email.
        if (string.IsNullOrWhiteSpace(options.Unsubscribe.SigningKey)) return false;
        if (string.IsNullOrWhiteSpace(options.Unsubscribe.PublicBaseUrl)) return false;

        return true;
    }, DescribeEmailConfigurationProblem(builder.Configuration))
    .ValidateOnStart();

// The queue transport. Singleton: it holds no per-request state, and it manages its own
// connections rather than borrowing the request-scoped DbContext — see PostgresJobQueue for why.
// Selected by Email:Queue:Transport, which is the seam an SQS implementation would plug into
// without either worker changing.
var queueTransport = builder.Configuration["Email:Queue:Transport"] ?? "Postgres";
switch (queueTransport.ToLowerInvariant())
{
    case "postgres":
        builder.Services.AddSingleton<IJobQueue, PostgresJobQueue>();
        break;
    default:
        // Fail at startup rather than at the first enqueue. A typo here would otherwise look
        // like a queue that accepts work and never runs it.
        throw new InvalidOperationException(
            $"Unsupported Email:Queue:Transport '{queueTransport}'. Supported values: Postgres.");
}

// Email background workers only run when the channel is switched on. A half-configured
// deployment should do nothing rather than accept campaigns it cannot send.
// Queue housekeeping serves every queue (WhatsApp too), so it always runs.
builder.Services.AddHostedService<JobQueueMaintenanceWorker>();

if (builder.Configuration.GetValue("Email:Enabled", false))
{

    // Two stages rather than one worker doing both: expansion is a database operation that
    // either works or does not, while dispatch is hundreds of independent network calls each
    // needing its own retry. Both are safe to run on every instance concurrently.
    builder.Services.AddHostedService<CampaignExpansionWorker>();
    builder.Services.AddHostedService<EmailDispatchWorker>();

    // IMAP polling for inbound replies. Only when explicitly enabled — a connection with no
    // IMAP settings configured simply has nothing to poll, but the worker itself must be running
    // so it picks up newly-configured mailboxes without a restart.
    if (builder.Configuration.GetValue("Email:Inbound:Enabled", false))
    {
        builder.Services.AddHostedService<ImapPollingWorker>();
    }
}

// Tenant mail: every connection sends through its own SMTP account (MailKit), resolved per
// connection by EmailProviderFactory. Providers are registered against IEmailProvider and picked
// by ProviderName, so another transport could be added here and nowhere else.
builder.Services.AddSingleton<IMimeMessageBuilder, MimeMessageBuilder>();
builder.Services.AddScoped<IEmailProvider, SmtpEmailProvider>();
builder.Services.AddScoped<IEmailProviderFactory, EmailProviderFactory>();
builder.Services.AddScoped<IEmailConnectionService, EmailConnectionService>();
builder.Services.AddScoped<IEmailTemplateService, EmailTemplateService>();
builder.Services.AddScoped<IEmailSenderGate, EmailSenderGate>();

// System mail (welcome emails, scheduled reports with no sender chosen): one platform SMTP
// account from the "Smtp" configuration section, exactly as in the OmniConnect AuthService.
// Inert without settings — IsEnabled is false and nothing is sent.
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.SectionName));
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<ISystemMailService, SystemMailService>();
builder.Services.AddScoped<IEmailSuppressionService, EmailSuppressionService>();
builder.Services.AddScoped<IEmailSendRecorder, EmailSendRecorder>();

// ── Normalized email event pipeline ───────────────────────────────────────────────────────
// These three are the backbone of the production-ready architecture:
// - IEmailEventStore: append-only event history with idempotency
// - ICampaignEmailEventProcessor: atomic counter increments, state machine, suppression, finalization
// - IEventPublisher / InProcessEventPublisher: decoupled real-time notification channel
builder.Services.AddScoped<IEmailEventStore, EmailEventStore>();
builder.Services.AddScoped<ICampaignEmailEventProcessor, CampaignEmailEventProcessor>();

// InProcessEventPublisher is Singleton because it holds the Channel<T> that bridges
// the dispatch workers (scoped) to the SignalR consumer (hosted service).
// Registered under both its concrete type (for the consumer) and the interface (for callers).
builder.Services.AddSingleton<InProcessEventPublisher>();
builder.Services.AddSingleton<IEventPublisher>(sp => sp.GetRequiredService<InProcessEventPublisher>());
builder.Services.AddHostedService<EventPublisherConsumer>();

// Pushes "something changed in this conversation" to open inboxes, replacing tight polling.
builder.Services.AddSingleton<WhatsAppCampaignApi.Services.Realtime.IDashboardNotifier, WhatsAppCampaignApi.Services.Realtime.DashboardNotifier>();
builder.Services.AddSingleton<WhatsAppCampaignApi.Services.Realtime.IInboxNotifier, WhatsAppCampaignApi.Services.Realtime.SignalRInboxNotifier>();

// SMTP connection pool manager — Singleton: pools must outlive request scopes
builder.Services.AddSingleton<SmtpConnectionPoolManager>();

// Open/click tracking token service
builder.Services.AddSingleton<IEmailTrackingService, EmailTrackingService>();

// One-off email from the inbox — reply, reply-all, forward. Sent inline rather than queued: the
// operator is watching, so the composer has to be able to tell them whether it went.
builder.Services.AddScoped<IEmailReplyService, EmailReplyService>();
builder.Services.AddScoped<IInboundEmailThreader, InboundEmailThreader>();
builder.Services.AddScoped<IEmailCampaignDispatcher, EmailCampaignDispatcher>();
builder.Services.AddSingleton<IUnsubscribeTokenService, UnsubscribeTokenService>();

// The rate limiter keeps its state in the database rather than in memory, so the configured
// send rate holds across every instance instead of being multiplied by however many are running.
builder.Services.AddSingleton<IEmailRateLimiter, PostgresEmailRateLimiter>();

// Who authorises a campaign to run. "none" is the built-in pass-through and this deployment's
// setting; a host application that owns maker-checker replaces this one registration and the
// campaign pipeline starts deferring to it, with no change to the workers or the queue.
// Campaigns:Approval:Required switches on the built-in maker-checker gate for every campaign, on
// both channels: a second user must approve before anything is sent.
var executionGate = builder.Configuration.GetValue("Campaigns:Approval:Required", false)
    ? WhatsAppCampaignApi.Services.Campaigns.InternalApprovalGate.Name
    : builder.Configuration["Email:ExecutionGate:Provider"] ?? "none";
switch (executionGate.ToLowerInvariant())
{
    case "none":
        builder.Services.AddScoped<ICampaignExecutionGate, AutoApproveCampaignExecutionGate>();
        break;
    case WhatsAppCampaignApi.Services.Campaigns.InternalApprovalGate.Name:
        builder.Services.AddScoped<ICampaignExecutionGate, WhatsAppCampaignApi.Services.Campaigns.InternalApprovalGate>();
        break;
    default:
        // Fails at startup rather than silently auto-approving. A deployment that believes it has
        // approval enforcement must not discover otherwise from its outbound mail.
        throw new InvalidOperationException(
            $"Email:ExecutionGate:Provider is set to '{executionGate}', but no gate is registered for it. "
          + "Register an ICampaignExecutionGate implementation with that GateName, or set the value to 'none'.");
}

// Webhook re-send. A short timeout because this is a best-effort side channel: a customer
// endpoint that hangs must not tie up a worker while Meta waits for its acknowledgement.
builder.Services.AddScoped<IWebhookForwarder, WebhookForwarder>();
builder.Services.AddHttpClient(WebhookForwarder.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

// Both AI providers are registered against IAiProvider. BotRouterService takes the whole set and
// picks by ProviderName, so which one answers a customer is a setting rather than a deployment.
builder.Services.AddHttpClient<IAiProvider, GroqProvider>();
builder.Services.AddHttpClient<IAiProvider, OpenAiProvider>();
builder.Services.AddScoped<MessageBotExecutor>();
builder.Services.AddScoped<IBotRouterService, BotRouterService>();

builder.Services.AddHttpClient<IWhatsAppService, WhatsAppCloudApiService>();

// Meta webhook: verified and stored by the controller, processed here. Always on — inbound
// WhatsApp does not depend on the email channel being enabled.
builder.Services.AddScoped<WhatsAppCampaignApi.Services.WhatsApp.IWhatsAppWebhookSignatureVerifier, WhatsAppCampaignApi.Services.WhatsApp.WhatsAppWebhookSignatureVerifier>();
builder.Services.AddHostedService<WhatsAppCampaignApi.Services.WhatsApp.WhatsAppWebhookWorker>();

// WhatsApp campaigns: durable queue delivery (expansion + multi-loop sender), in place of the
// in-memory Task.Run loop that did not survive a restart or a second instance.
builder.Services.AddOptions<WhatsAppCampaignApi.Services.WhatsApp.WhatsAppDispatchOptions>()
    .Bind(builder.Configuration.GetSection(WhatsAppCampaignApi.Services.WhatsApp.WhatsAppDispatchOptions.SectionName));
builder.Services.AddScoped<WhatsAppCampaignApi.Services.WhatsApp.IWhatsAppCampaignDispatcher, WhatsAppCampaignApi.Services.WhatsApp.WhatsAppCampaignDispatcher>();
builder.Services.AddHostedService<WhatsAppCampaignApi.Services.WhatsApp.WhatsAppCampaignExpansionWorker>();
builder.Services.AddHostedService<WhatsAppCampaignApi.Services.WhatsApp.WhatsAppCampaignSendWorker>();
builder.Services.AddHostedService<CampaignSchedulerService>();

// Safety net for chat conversation rows. They are created when a contact is created; this
// catches anything that path misses (notably contacts that predate a newly added connection).
builder.Services.AddHostedService<ChatConversationReconcilerService>();

// Refreshes templates from Meta on a schedule, so reading the template list never waits on an
// outbound HTTPS call.
builder.Services.AddHostedService<TemplateSyncBackgroundService>();

// Auto Clear Chat History. Reads its own setting each run, so the toggle takes effect at the next
// sweep rather than at the next restart.
builder.Services.AddHostedService<ChatHistoryCleanupService>();

// Authentication, RBAC and auditing
builder.Services.AddScoped<IPermissionResolver, PermissionResolver>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddSingleton<ISecurityStampCache, SecurityStampCache>();
builder.Services.AddScoped<IAuditChangeBuffer, AuditChangeBuffer>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<ISetupSeeder, SetupSeeder>();

// Setup module
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IRoleService, RoleService>();
builder.Services.AddScoped<ILogFileService, LogFileService>();

// Connection scoping — a different axis from RBAC (which WABA connection a user may see,
// rather than which features they may use). Kept as-is.
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<IPermissionManagementService, PermissionManagementService>();
builder.Services.AddScoped<WhatsAppCampaignApi.Services.Security.IAccessScope, WhatsAppCampaignApi.Services.Security.AccessScope>();
builder.Services.AddScoped<WhatsAppCampaignApi.Services.Compliance.IComplianceGuard, WhatsAppCampaignApi.Services.Compliance.ComplianceGuard>();
builder.Services.AddScoped<WhatsAppCampaignApi.Services.Compliance.IConsentService, WhatsAppCampaignApi.Services.Compliance.ConsentService>();
builder.Services.AddScoped<WhatsAppCampaignApi.Services.Segments.ISegmentService, WhatsAppCampaignApi.Services.Segments.SegmentService>();

// Deliverability pre-checks. DNS lookups use the host's resolver with a short timeout: a slow
// resolver must degrade the check to "could not read DNS", never hold up campaign creation.
builder.Services.AddSingleton<DnsClient.ILookupClient>(_ => new DnsClient.LookupClient(new DnsClient.LookupClientOptions
{
    Timeout = TimeSpan.FromSeconds(3),
    Retries = 1,
    UseCache = true
}));
builder.Services.AddScoped<IDeliverabilityService, DeliverabilityService>();
// Typed client: one pooled handler; the probe caches its verdict, so this costs one request per few minutes.
builder.Services.AddHttpClient<IPublicEndpointProbe, PublicEndpointProbe>();
builder.Services.AddScoped<WhatsAppCampaignApi.Services.Campaigns.IAbTestService, WhatsAppCampaignApi.Services.Campaigns.AbTestService>();
builder.Services.AddHostedService<WhatsAppCampaignApi.Services.Campaigns.AbTestWinnerWorker>();
builder.Services.AddScoped<WhatsAppCampaignApi.Services.Campaigns.IFollowUpService, WhatsAppCampaignApi.Services.Campaigns.FollowUpService>();
builder.Services.AddHostedService<WhatsAppCampaignApi.Services.Campaigns.FollowUpWorker>();
builder.Services.AddScoped<WhatsAppCampaignApi.Services.Chat.IConversationOperations, WhatsAppCampaignApi.Services.Chat.ConversationOperations>();
builder.Services.AddHostedService<WhatsAppCampaignApi.Services.Chat.ChatSlaWorker>();

// Outbound webhooks: signed (HMAC-SHA256), queued and retried, and never allowed to reach the
// server's own network: the guard runs when a URL is saved and again on every connection.
var webhookGuard = WhatsAppCampaignApi.Services.Integrations.WebhookUrlGuard.FromConfiguration(builder.Configuration, builder.Environment);
builder.Services.AddSingleton(webhookGuard);
builder.Services.AddHttpClient(WhatsAppCampaignApi.Services.Integrations.WebhookSender.HttpClientName, client =>
    {
        client.Timeout = TimeSpan.FromSeconds(10);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WabaConnect-Webhooks/1.0");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        ConnectCallback = webhookGuard.ConnectAsync,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    });
builder.Services.AddScoped<WhatsAppCampaignApi.Services.Integrations.WebhookSender>();
builder.Services.AddSingleton<WhatsAppCampaignApi.Services.Integrations.WebhookEmitter>();
builder.Services.AddSingleton<WhatsAppCampaignApi.Services.Integrations.IWebhookEmitter>(sp => sp.GetRequiredService<WhatsAppCampaignApi.Services.Integrations.WebhookEmitter>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<WhatsAppCampaignApi.Services.Integrations.WebhookEmitter>());
builder.Services.AddScoped<WhatsAppCampaignApi.Services.Integrations.IWebhookSubscriptionService, WhatsAppCampaignApi.Services.Integrations.WebhookSubscriptionService>();
builder.Services.AddHostedService<WhatsAppCampaignApi.Services.Integrations.WebhookDeliveryWorker>();

// Saved reports emailed on a timetable, each run under its owner's access.
builder.Services.AddScoped<WhatsAppCampaignApi.Services.Integrations.IReportScheduleService, WhatsAppCampaignApi.Services.Integrations.ReportScheduleService>();
builder.Services.AddHostedService<WhatsAppCampaignApi.Services.Integrations.ReportScheduleWorker>();
builder.Services.AddScoped<IConnectionService, ConnectionService>();
builder.Services.AddScoped<IWabaRepository, WabaRepository>();
builder.Services.AddScoped<IBusinessRepository, BusinessRepository>();
builder.Services.AddScoped<IPhoneRepository, PhoneRepository>();
builder.Services.AddScoped<IHealthLogRepository, HealthLogRepository>();
builder.Services.AddScoped<IMetaGraphService, MetaGraphService>();
builder.Services.AddScoped<IWebhookService, WebhookService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IHealthService, HealthService>();

// 3b. Forwarded headers — who the client actually is behind a proxy.
//
// Without this, everything downstream sees the load balancer's address: the audit trail records
// the proxy for every user, and the rate limiter partitions the entire internet into one bucket.
//
// The trust list comes from configuration and is empty by default. When it is empty the
// middleware is NOT added at all, and the connection's own address is what gets recorded.
//
// That last part is load-bearing and not obvious. ForwardedHeadersMiddleware only validates the
// peer when at least one proxy or network is configured — with both lists empty it skips the
// check entirely and believes every X-Forwarded-For it is sent. So "configured nothing" would
// mean "trust everyone", the exact inversion of what it looks like. Verified by sending a
// spoofed header: it was accepted and written to the sign-in record. Not registering the
// middleware is the only way an unconfigured deployment stays safe.
var forwardedHeaders = builder.Configuration.GetSection("ForwardedHeaders");
var trustedProxies = new List<IPAddress>();
var trustedNetworks = new List<(IPAddress Prefix, int Length)>();

foreach (var proxy in forwardedHeaders.GetSection("KnownProxies").Get<string[]>() ?? Array.Empty<string>())
{
    if (IPAddress.TryParse(proxy, out var address)) trustedProxies.Add(address);
    else Log.Warning("Ignoring unparseable ForwardedHeaders:KnownProxies entry {Value}.", proxy);
}

foreach (var network in forwardedHeaders.GetSection("KnownNetworks").Get<string[]>() ?? Array.Empty<string>())
{
    // "10.0.0.0/8" — the CIDR form an operator has to hand from their VPC. Parsed rather than
    // split by hand so that a network written with host bits set ("10.1.2.3/8") is rejected
    // loudly instead of silently trusting a range nobody intended.
    if (System.Net.IPNetwork.TryParse(network, out var parsed)) trustedNetworks.Add((parsed.BaseAddress, parsed.PrefixLength));
    else Log.Warning("Ignoring unparseable ForwardedHeaders:KnownNetworks entry {Value}.", network);
}

var trustForwardedHeaders = trustedProxies.Count > 0 || trustedNetworks.Count > 0;

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    // How many hops in from the right of the chain to walk. One proxy in front of the app is the
    // normal case; a CDN plus a load balancer is two. Walking further than the number of hops you
    // actually control is how a spoofed entry gets believed.
    options.ForwardLimit = forwardedHeaders.GetValue<int?>("ForwardLimit") ?? 1;

    // Defaults trust the loopback proxy. Cleared so the trust list is exactly what was configured
    // and nothing that merely happens to work on a developer machine.
    options.KnownProxies.Clear();
    options.KnownIPNetworks.Clear();

    foreach (var address in trustedProxies) options.KnownProxies.Add(address);

    // Target-typed `new` because this list's element type differs between ASP.NET Core versions;
    // both spellings take (address, prefix length).
    foreach (var (prefix, length) in trustedNetworks) options.KnownIPNetworks.Add(new(prefix, length));
});

// 3c. Rate limiting.
//
// Partitioned by user id when there is one and by client IP otherwise, so one noisy tenant cannot
// exhaust everyone else's allowance and an unauthenticated flood is still bounded. Sign-in gets
// its own tighter bucket: it is the endpoint worth guessing at, and it is cheap to defend because
// no legitimate person signs in twenty times a minute.
//
// QueueLimit is 0 on purpose. Queueing turns a rate limit into latency — requests pile up holding
// server resources and the client sees a hang instead of an answer. Refusing immediately with a
// 429 and Retry-After tells the caller what to do.
var rateLimiting = builder.Configuration.GetSection("RateLimiting");
var rateLimitingEnabled = rateLimiting.GetValue<bool?>("Enabled") ?? true;

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    var permitLimit = rateLimiting.GetValue<int?>("PermitLimit") ?? 300;
    var windowSeconds = rateLimiting.GetValue<int?>("WindowSeconds") ?? 60;
    var queueLimit = rateLimiting.GetValue<int?>("QueueLimit") ?? 0;
    var authPermitLimit = rateLimiting.GetValue<int?>("AuthPermitLimit") ?? 20;
    var authWindowSeconds = rateLimiting.GetValue<int?>("AuthWindowSeconds") ?? 60;

    static string PartitionKey(HttpContext context)
    {
        var userId = context.User?.FindFirst(AuthClaims.UserId)?.Value;
        return !string.IsNullOrWhiteSpace(userId)
            ? $"user:{userId}"
            : $"ip:{ClientIpResolver.Resolve(context) ?? "unknown"}";
    }

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        // The health endpoint is how a load balancer decides whether this node is alive. Rate
        // limiting it would make a busy node look dead and take it out of rotation.
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            return RateLimitPartition.GetNoLimiter("health");
        }

        // Machine traffic that arrives from a handful of shared addresses: Meta's webhook
        // deliveries, and open/click tracking hits, which Gmail and Outlook fetch through their
        // image proxies. A per-IP limit would throttle inbound WhatsApp and silently drop opens
        // at exactly the volume a bank campaign produces. Both are cheap and idempotent, and the
        // webhook is signature-verified.
        if (context.Request.Path.StartsWithSegments("/api/webhook") || context.Request.Path.StartsWithSegments("/api/t"))
        {
            return RateLimitPartition.GetNoLimiter("machine");
        }

        return RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromSeconds(windowSeconds),
                QueueLimit = queueLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            });
    });

    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = authPermitLimit,
                Window = TimeSpan.FromSeconds(authWindowSeconds),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }));

    // Tell the caller when to come back rather than leaving them to guess and retry immediately,
    // which is what turns a rate limit into a hot loop.
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            """{"success":false,"message":"Too many requests. Please slow down and try again shortly."}""",
            cancellationToken);
    };
});

// 3d. Response compression.
//
// The list endpoints return sizeable JSON and the exports return CSV, both of which compress by
// roughly an order of magnitude. Left off for HTTPS by default in ASP.NET Core because of the
// BREACH attack; enabled here because this API returns no secret in a response body that also
// reflects attacker-controlled input, and the bandwidth on report exports is the larger concern.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
    {
        "application/json",
        "text/csv"
    });
});

// 3e. Health checks — how a load balancer decides to drain this node.
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: new[] { "ready" })
    .AddCheck<QueueHealthCheck>("job-queue", tags: new[] { "ready" })
    // Not "ready": an unreachable tracking address must alert, not pull the node out of rotation.
    .AddCheck<PublicEndpointHealthCheck>("tracking-address");

// 4. Configure CORS
// An explicit origin allow-list from Cors:AllowedOrigins. A wildcard is not acceptable for a
// banking deployment, and it also broke the SignalR negotiate request: the browser client sends
// credentials, and a credentialed request is refused when the answer is "*".
//
// With no list configured, Development still accepts any localhost origin (the Vite dev server
// runs on a different port from the API); any other environment then accepts same-origin calls
// only, which is correct when the SPA is served from the API's own host.
var allowedOrigins = (builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>())
    .Where(o => !string.IsNullOrWhiteSpace(o))
    .Select(o => o.Trim().TrimEnd('/'))
    .ToArray();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins);
        }
        else if (builder.Environment.IsDevelopment())
        {
            policy.SetIsOriginAllowed(origin =>
                Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                && (uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)));
        }
        else
        {
            policy.SetIsOriginAllowed(_ => false);
        }

        policy
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials()
            // AllowAnyHeader covers REQUEST headers only. A cross-origin response exposes just
            // the CORS-safelisted headers to JavaScript unless named here — so without this the
            // file downloads could read no Content-Disposition and every export saved under a
            // generic client-side fallback name instead of the server's timestamped one.
            .WithExposedHeaders("Content-Disposition", "X-Total-Count", "X-Next-Cursor")
            .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
    });
});

// 4b. Authentication (JWT bearer)
var jwtKey = builder.Configuration["Auth:Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException(
        "Auth:Jwt:Key must be configured with at least 32 bytes for HMAC-SHA256 signing.");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Without this the handler rewrites short JWT claim names through its inbound map —
        // "sub" becomes the ClaimTypes.NameIdentifier URI — and CurrentUserService, which looks
        // up the exact names it wrote, silently resolves every user as null.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Auth:Jwt:Issuer"],
            ValidAudience = builder.Configuration["Auth:Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            // Default is 5 minutes of leeway, which lets a just-expired token keep working.
            ClockSkew = TimeSpan.Zero,
            // Stop the inbound-claim mapper rewriting "sub" to the long ClaimTypes.NameIdentifier
            // URI, so CurrentUserService can read back the exact claim names it wrote.
            NameClaimType = AuthClaims.Name,
            RoleClaimType = AuthClaims.RoleName
        };

        options.Events = new JwtBearerEvents
        {
            // Every request re-checks that the session is still valid: the account is active and
            // its security stamp matches the token's. A password change, deactivation or role
            // change therefore ends existing sessions immediately, not when their tokens expire.
            OnTokenValidated = async context =>
            {
                var principal = context.Principal;
                if (!int.TryParse(principal?.FindFirst(AuthClaims.UserId)?.Value, out var userId))
                {
                    context.Fail("The token has no user id.");
                    return;
                }

                var stamps = context.HttpContext.RequestServices.GetRequiredService<ISecurityStampCache>();
                var (active, stamp) = await stamps.GetAsync(userId, context.HttpContext.RequestAborted);
                var tokenStamp = principal!.FindFirst(AuthClaims.SecurityStamp)?.Value;

                if (!active || stamp is null || !string.Equals(stamp, tokenStamp, StringComparison.Ordinal))
                {
                    context.Fail("The session has ended.");
                }
            },

            // Browsers cannot set an Authorization header on a WebSocket or EventSource request,
            // so the SignalR client sends the token as ?access_token=. Accepted on hub paths only;
            // everywhere else a token in a URL would end up in proxy and access logs.
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// 5. Add Controllers and Validation
// ── Upload limits ───────────────────────────────────────────────────────────────────────────────
// Kestrel refuses a request body over 30 MB by default, and the multipart form reader caps a
// section at 128 MB. Both defaults sit below what a bulk-campaign CSV can legitimately be, and
// both reject the request before any controller runs -- so a large upload surfaced as a bare
// network error with nothing to explain it. Raised to the single limit the endpoint declares, so
// the transport, the form reader and the action attribute all agree.
//
// The large limit is granted per endpoint ([RequestSizeLimit] on the CSV and media uploads), not
// globally: a 256 MB ceiling on every route let any authenticated caller tie up memory and
// bandwidth with an oversized body to an endpoint that expects a few kilobytes.
builder.Services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = builder.Configuration.GetValue<long>("Limits:MaxRequestBodyBytes", 30L * 1024 * 1024);
    options.Limits.MaxRequestHeadersTotalSize = 64 * 1024;
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
    options.AddServerHeader = false;
});

builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = WhatsAppCampaignApi.Helpers.CsvUploadLimits.MaxBytes;
    options.ValueLengthLimit = 4 * 1024 * 1024;

    // Uploads above 64 KB are buffered to a temp file rather than held in memory; the previous
    // int.MaxValue kept every multipart upload entirely in RAM.
    options.MemoryBufferThreshold = 64 * 1024;
});

// HSTS for production: browsers refuse plain HTTP to this host for a year once seen.
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
});

builder.Services.AddControllers(options =>
{
    options.Filters.Add<SanitizeInputFilter>();
    // Gates the pre-auth controllers. Inert until Auth:EnforceOnLegacyEndpoints is turned on,
    // so no existing endpoint changes behaviour today.
    options.Filters.Add<LegacyAuthGateFilter>();
    // A user flagged to change their password may do only that until they have.
    options.Filters.Add<MustChangePasswordFilter>();
});

// Register FluentValidation.
// Note: this pipeline is synchronous, so validators must not contain MustAsync rules — one
// would throw at request time rather than validating. See ContactLookupValidatorCache.
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<CreateContactValidator>();
builder.Services.AddScoped<ContactLookupValidatorCache>();

// SignalR — real-time campaign progress and inbox updates.
// With more than one API instance, set SignalR:Redis to a Redis connection string so a message
// published on one node reaches clients connected to another. Single-node deployments leave it
// empty and need nothing else.
var signalR = builder.Services.AddSignalR(options =>
{
    // Keep-alive: detect dead connections faster than the default 15s interval.
    options.KeepAliveInterval = TimeSpan.FromSeconds(10);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
    // Clients only ever send small method calls (JoinCampaign); cap what one may send.
    options.MaximumReceiveMessageSize = 32 * 1024;
});

var signalRRedis = builder.Configuration["SignalR:Redis"];
if (!string.IsNullOrWhiteSpace(signalRRedis))
{
    signalR.AddStackExchangeRedis(signalRRedis, redis =>
    {
        redis.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("wabaconnect");
    });
}

// 5. Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// A wrong Encryption:Key would otherwise start cleanly and then fail every send that needs a
// stored credential. Checked once, before any worker takes work.
await EncryptionKeyGuard.VerifyAsync(app.Services);

// Configure the HTTP request pipeline.
//
// Request logging sits OUTSIDE the exception handler, which is the opposite of how it was
// wired. Inside, every exception passed through it on the way out, so a KeyNotFoundException
// that the handler below turns into a clean 404 was logged as "responded 500" with a full stack
// trace — the client got 404 while the log claimed a server fault.
//
// Outside, the handler has already written the real status by the time this records it, and no
// exception reaches it.
//
// Routine per-request lines were 526 of 709 entries in a day's log file — 74% of it — which
// buried the handful of records anyone opens the viewer to find. A healthy request now logs at
// Debug (below the Information floor, so it never reaches a sink); anything that failed, was
// refused, or was slow keeps its line.
// First in the pipeline, and specifically before request logging: this is what replaces the
// connection's address with the client's, and anything that reads an address before it runs —
// the request log included — would record the proxy instead.
//
// Only registered when a proxy is actually configured. See the options block above: an empty
// trust list makes this middleware believe every forwarded header, so leaving it out is what
// makes an unconfigured deployment safe.
if (trustForwardedHeaders)
{
    app.UseForwardedHeaders();
    Log.Information(
        "Trusting X-Forwarded-For from {ProxyCount} known prox(ies) and {NetworkCount} known network(s).",
        trustedProxies.Count, trustedNetworks.Count);
}
else
{
    Log.Information(
        "No ForwardedHeaders:KnownProxies/KnownNetworks configured — X-Forwarded-For is ignored " +
        "and the connection address is recorded. Configure the proxy in each deployment that has one.");
}

app.UseSerilogRequestLogging(options =>
{
    options.GetLevel = (httpContext, elapsedMs, exception) =>
    {
        // Only reachable if something escaped the handler entirely.
        if (exception is not null) return LogEventLevel.Error;

        var status = httpContext.Response.StatusCode;
        if (status >= 500) return LogEventLevel.Error;

        // 4xx is the client being told no — a 403 from the permission filter is a real security
        // signal and stays visible.
        if (status >= 400) return LogEventLevel.Warning;

        // A healthy request that took over a second is worth keeping.
        return elapsedMs > 1000 ? LogEventLevel.Warning : LogEventLevel.Debug;
    };
});

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Before CORS and the rest: nothing further down should spend work on a request that is about to
// be refused. Compression wraps the response stream, so it has to be in place before anything
// writes one.
app.UseResponseCompression();

app.UseCors("AllowFrontend");

// Customer CSV uploads used to be saved under wwwroot/uploads/csv, where anyone with a link could
// download them. New uploads go to private storage; older files there are refused outright.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/uploads/csv", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    await next();
});

app.UseStaticFiles(new StaticFileOptions
{
    // Uploaded files are data, not pages: anything that is not an image is delivered as a
    // download, so a document can never be rendered in the browser in our origin's context.
    OnPrepareResponse = ctx =>
    {
        if (!ctx.Context.Request.Path.StartsWithSegments("/uploads")) return;

        var contentType = ctx.Context.Response.ContentType ?? string.Empty;
        if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers.ContentDisposition = "attachment";
        }

        ctx.Context.Response.Headers.CacheControl = "private, max-age=3600";
    }
});
// Order matters: authentication populates HttpContext.User, which authorization then reads.
app.UseAuthentication();
app.UseAuthorization();

// After authentication on purpose: the limiter partitions by user id when there is one, and
// before UseAuthentication runs HttpContext.User is empty, so every signed-in request would fall
// back to sharing a bucket by IP — putting a whole office behind one NAT into one allowance.
if (rateLimitingEnabled)
{
    app.UseRateLimiter();
}

// Unauthenticated by design: a load balancer has no credentials, and a health endpoint that needs
// a token cannot do its job. The body is deliberately thin — status, per-check state and latency,
// nothing about why a check failed, since anyone can read it.
//   /health/live  — the process is up (no dependencies): restart it if this fails.
//   /health/ready — the database answers and the queue is not backed up: route traffic to it.
//   /health       — everything, for dashboards.
static Task WriteHealthAsync(HttpContext context, Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport report)
{
    context.Response.ContentType = "application/json";
    return context.Response.WriteAsync(JsonSerializer.Serialize(new
    {
        status = report.Status.ToString(),
        totalDurationMs = (int)report.TotalDuration.TotalMilliseconds,
        checks = report.Entries.Select(entry => new
        {
            name = entry.Key,
            status = entry.Value.Status.ToString(),
            durationMs = (int)entry.Value.Duration.TotalMilliseconds
        })
    }));
}

app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { ResponseWriter = WriteHealthAsync });
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = WriteHealthAsync
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteHealthAsync
});

app.MapControllers();

// Real-time campaign event updates via SignalR.
// Clients connect at /hubs/campaign and call JoinCampaign(campaignId) to subscribe.
app.MapHub<CampaignHub>("/hubs/campaign");

app.Lifetime.ApplicationStarted.Register(() =>
{
    var addresses = app.Urls;
    foreach (var address in addresses)
    {
        Log.Information("Now listening on: {Address}", address);
    }

    // Pending-migration guard. Deliberately NOT Database.Migrate(): the connection string
    // points at a shared Neon instance, so concurrent developer startups would race, and
    // AppDbContext.OnConfiguring suppresses PendingModelChangesWarning — meaning an auto-apply
    // could push a surprise. Warn loudly instead and let the CLI stay the single entry point.
    _ = Task.Run(async () =>
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();

            if (pending.Count > 0)
            {
                Log.Fatal(
                    "{Count} PENDING MIGRATION(S) NOT APPLIED: {Migrations}. Run: dotnet ef database update",
                    pending.Count, string.Join(", ", pending));
            }

            // Seed the permission catalogue, default roles and the first super admin. Runs at
            // boot rather than lazily on first request, because the lazy pattern races under
            // concurrent cold-start traffic and can double-insert. The seeder is additive and
            // swallows its own errors, so a missing table degrades to a log line.
            var seeder = scope.ServiceProvider.GetRequiredService<ISetupSeeder>();
            await seeder.SeedAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not check for pending migrations.");
        }
    });
});

app.Run();

/// <summary>
/// Names the specific email setting that is missing, for the startup validation message.
/// </summary>
/// <remarks>
/// The message used to recite every rule the validator checks, which left whoever hit it reading
/// five conditions to work out which one applied to them. Evaluated once, at startup, against the
/// same configuration the options were bound from.
/// </remarks>
static string DescribeEmailConfigurationProblem(IConfiguration configuration)
{
    var problems = new List<string>();

    if (string.IsNullOrWhiteSpace(configuration["Email:Unsubscribe:SigningKey"])
        && string.IsNullOrWhiteSpace(configuration["Encryption:Key"]))
    {
        problems.Add("Email:Unsubscribe:SigningKey is not set and cannot be derived, because "
                   + "Encryption:Key is also empty. Set one of them.");
    }

    if (string.IsNullOrWhiteSpace(configuration["Email:Unsubscribe:PublicBaseUrl"]))
    {
        problems.Add("Email:Unsubscribe:PublicBaseUrl is not set. Set App:PublicBaseUrl (it is used for "
                   + "unsubscribe, tracking and media links) to a URL that resolves from outside this "
                   + "machine \u2014 appsettings.Development.json sets http://localhost:5155 for local runs.");
    }

    if (problems.Count == 0)
    {
        // The remaining rules are about the queue and inbound handling, and each is a relationship
        // between two values rather than a missing one.
        problems.Add("Check that Email:Queue:MaxBackoffSeconds is at least "
                   + "Email:Queue:BaseBackoffSeconds, and that Email:Queue:PerPartitionCap does not "
                   + "exceed Email:Queue:ClaimBatchSize.");
    }

    return "Email:Enabled is true but the email configuration is incomplete. "
         + string.Join(" ", problems);
}
