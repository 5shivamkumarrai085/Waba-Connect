using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.EntityFrameworkCore;
using Serilog;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Middleware;
using WhatsAppCampaignApi.Services;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Validators;

var builder = WebApplication.CreateBuilder(args);

// 1. Serilog configuration
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// 2. Add Database Context (PostgreSQL/NeonDB)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// 3. Add Services
builder.Services.AddScoped<IContactService, ContactService>();
builder.Services.AddScoped<IContactGroupService, ContactGroupService>();
builder.Services.AddScoped<ITemplateService, TemplateService>();
builder.Services.AddScoped<ICampaignService, CampaignService>();
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddScoped<IMessageBotService, MessageBotService>();
builder.Services.AddScoped<ITemplateBotService, TemplateBotService>();
builder.Services.AddScoped<IBotFlowService, BotFlowService>();
builder.Services.AddHttpClient<IWhatsAppService, WhatsAppCloudApiService>();
builder.Services.AddHostedService<CampaignSchedulerService>();

// WABA Integration Services
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
