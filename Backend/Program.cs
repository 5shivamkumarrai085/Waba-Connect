using System.Text;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
builder.Services.AddHttpClient<IAiProvider, GroqProvider>();
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

app.UseCors("AllowFrontend");
app.UseStaticFiles();
// Order matters: authentication populates HttpContext.User, which authorization then reads.
app.UseAuthentication();
app.UseAuthorization();
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
