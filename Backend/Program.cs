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
using WhatsAppCampaignApi.Services;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Validators;
using WhatsAppCampaignApi.Executors;

var builder = WebApplication.CreateBuilder(args);

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
    options.KnownNetworks.Clear();

    foreach (var address in trustedProxies) options.KnownProxies.Add(address);

    // Target-typed `new` because this list's element type differs between ASP.NET Core versions;
    // both spellings take (address, prefix length).
    foreach (var (prefix, length) in trustedNetworks) options.KnownNetworks.Add(new(prefix, length));
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
