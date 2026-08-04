using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.EntityFrameworkCore;
using Serilog;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Middleware;
using WhatsAppCampaignApi.Services;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Validators;
using WhatsAppCampaignApi.Executors;

var builder = WebApplication.CreateBuilder(args);

// 1. Serilog configuration
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// 2. Add Database Context (PostgreSQL/NeonDB)
// Registered as a factory (not AddDbContext) so services that need several independent queries running
// concurrently can create their own short-lived contexts on demand (DbContext itself isn't thread-safe).
// AddDbContext and AddDbContextFactory can't coexist for the same context (their DbContextOptions
// lifetimes conflict), so the regular scoped AppDbContext used everywhere else is derived from the factory.
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddScoped<AppDbContext>(sp =>
    sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());

builder.Services.AddMemoryCache();
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
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend",
        policy => policy
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader());
});

// 5. Add Controllers and Validation
builder.Services.AddControllers(options =>
{
    options.Filters.Add<SanitizeInputFilter>();
});

// Register FluentValidation
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<CreateContactValidator>();

// 5. Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseCors("AllowFrontend");
app.UseStaticFiles();
app.UseAuthorization();
app.MapControllers();

app.Lifetime.ApplicationStarted.Register(() =>
{
    var addresses = app.Urls;
    foreach (var address in addresses)
    {
        Log.Information("Now listening on: {Address}", address);
    }
});

app.Run();
