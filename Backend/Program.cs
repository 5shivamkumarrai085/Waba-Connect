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

var builder = WebApplication.CreateBuilder(args);

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
if (string.IsNullOrWhiteSpace(builder.Configuration["Email:Unsubscribe:SigningKey"]))
{
    var rootKey = builder.Configuration["Encryption:Key"];

    if (!string.IsNullOrWhiteSpace(rootKey))
    {
        var derived = System.Security.Cryptography.HKDF.DeriveKey(
            System.Security.Cryptography.HashAlgorithmName.SHA256,
            ikm: System.Text.Encoding.UTF8.GetBytes(rootKey),
            outputLength: 32,
            info: System.Text.Encoding.UTF8.GetBytes("WabaConnect.Email.Unsubscribe.v1"));

        builder.Configuration["Email:Unsubscribe:SigningKey"] = Convert.ToBase64String(derived);
    }
}

// 1. Serilog configuration
// The file sink writes compact JSON (CLEF) rather than plain text so the Setup > System Logs
// viewer can parse entries back into structured records (level, timestamp, properties,
// exception) instead of regex-scraping rendered strings. `shared: true` is required because
// CampaignSchedulerService writes concurrently with the request pipeline.
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName)
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
builder.Services.AddDbContextFactory<AppDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));

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

        // An anonymous webhook with no topic allowlist would accept any validly-signed SNS
        // notification from any AWS account. Only required for SES — SMTP inbound uses IMAP
        // polling and has no SNS dependency.
        if (options.Inbound.Enabled
            && options.DefaultProvider.Equals("AmazonSes", StringComparison.OrdinalIgnoreCase)
            && options.Ses.AllowedSnsTopicArns.Length == 0) return false;

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
if (builder.Configuration.GetValue("Email:Enabled", false))
{
    builder.Services.AddHostedService<JobQueueMaintenanceWorker>();

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

// Both email providers are registered against IEmailProvider, and EmailProviderFactory picks by
// ProviderName — the same arrangement as IAiProvider above. That is what makes the provider a
// per-connection setting rather than a deployment decision, and what lets a third provider be
// added later by registering it here and nowhere else.
builder.Services.AddSingleton<IMimeMessageBuilder, MimeMessageBuilder>();
builder.Services.AddScoped<IEmailProvider, SesEmailProvider>();
builder.Services.AddScoped<IEmailProvider, SmtpEmailProvider>();
builder.Services.AddScoped<IEmailProviderFactory, EmailProviderFactory>();
builder.Services.AddScoped<IEmailConnectionService, EmailConnectionService>();
builder.Services.AddScoped<IEmailTemplateService, EmailTemplateService>();
builder.Services.AddScoped<IEmailDomainService, EmailDomainService>();
builder.Services.AddScoped<IEmailEventProcessor, EmailEventProcessor>();

// SNS signature verification. Its own named client with a short timeout: fetching the signing
// certificate happens inline on a webhook request, and a slow AWS endpoint must not hold the
// request open while SNS waits for an acknowledgement.
builder.Services.AddScoped<ISnsMessageValidator, SnsMessageValidator>();
builder.Services.AddHttpClient(SnsMessageValidator.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});
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
var executionGate = builder.Configuration["Email:ExecutionGate:Provider"] ?? "none";
switch (executionGate.ToLowerInvariant())
{
    case "none":
        builder.Services.AddScoped<ICampaignExecutionGate, AutoApproveCampaignExecutionGate>();
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
    .AddCheck<DatabaseHealthCheck>("database", tags: new[] { "ready" });

// 4. Configure CORS
// AllowAnyOrigin stays valid because auth uses a bearer header rather than cookies —
// AllowAnyOrigin and AllowCredentials cannot be combined, so a cookie scheme would force
// an explicit origin list here.
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend",
        policy => policy
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader()
            // AllowAnyHeader covers REQUEST headers only. A cross-origin response exposes just
            // the CORS-safelisted headers to JavaScript unless named here — so without this the
            // file downloads could read no Content-Disposition and every export saved under a
            // generic client-side fallback name instead of the server's timestamped one.
            .WithExposedHeaders("Content-Disposition"));
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
    });

builder.Services.AddAuthorization();

// 5. Add Controllers and Validation
// ── Upload limits ───────────────────────────────────────────────────────────────────────────────
// Kestrel refuses a request body over 30 MB by default, and the multipart form reader caps a
// section at 128 MB. Both defaults sit below what a bulk-campaign CSV can legitimately be, and
// both reject the request before any controller runs -- so a large upload surfaced as a bare
// network error with nothing to explain it. Raised to the single limit the endpoint declares, so
// the transport, the form reader and the action attribute all agree.
builder.Services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = WhatsAppCampaignApi.Helpers.CsvUploadLimits.MaxBytes;
});

builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = WhatsAppCampaignApi.Helpers.CsvUploadLimits.MaxBytes;
    options.ValueLengthLimit = int.MaxValue;
    options.MemoryBufferThreshold = int.MaxValue;
});

builder.Services.AddControllers(options =>
{
    options.Filters.Add<SanitizeInputFilter>();
    // Gates the pre-auth controllers. Inert until Auth:EnforceOnLegacyEndpoints is turned on,
    // so no existing endpoint changes behaviour today.
    options.Filters.Add<LegacyAuthGateFilter>();
});

// Register FluentValidation.
// Note: this pipeline is synchronous, so validators must not contain MustAsync rules — one
// would throw at request time rather than validating. See ContactLookupValidatorCache.
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<CreateContactValidator>();
builder.Services.AddScoped<ContactLookupValidatorCache>();

// SignalR — real-time campaign progress updates.
// No Redis backplane now; add one here when horizontal scaling requires it:
// .AddStackExchangeRedis(connectionString)
// No code changes to CampaignHub or EventPublisherConsumer are needed when that happens.
builder.Services.AddSignalR(options =>
{
    // Keep-alive: detect dead connections faster than the default 15s interval.
    options.KeepAliveInterval = TimeSpan.FromSeconds(10);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
});

// 5. Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

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

app.UseMiddleware<ExceptionHandlingMiddleware>();

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
app.UseStaticFiles();
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
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
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
        problems.Add("Email:Unsubscribe:PublicBaseUrl is not set. It is the address recipients "
                   + "reach to unsubscribe, so it has to be a URL that resolves from outside this "
                   + "machine \u2014 for local development, http://localhost:5255.");
    }

    if (problems.Count == 0)
    {
        // The remaining rules are about the queue and inbound handling, and each is a relationship
        // between two values rather than a missing one.
        problems.Add("Check that Email:Queue:MaxBackoffSeconds is at least "
                   + "Email:Queue:BaseBackoffSeconds, that Email:Queue:PerPartitionCap does not "
                   + "exceed Email:Queue:ClaimBatchSize, and that SES inbound handling has at least "
                   + "one entry in Email:Ses:AllowedSnsTopicArns (not required for SMTP/IMAP).");
    }

    return "Email:Enabled is true but the email configuration is incomplete. "
         + string.Join(" ", problems);
}
