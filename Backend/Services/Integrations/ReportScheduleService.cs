using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.Reporting;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Services.Compliance;
using WhatsAppCampaignApi.Services.Email;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Services.Security;

namespace WhatsAppCampaignApi.Services.Integrations;

public sealed class SaveReportScheduleRequest
{
    public int ReportDefinitionId { get; set; }
    public string Frequency { get; set; } = "Weekly";
    public string TimeOfDay { get; set; } = "09:00";
    public int? DayOfWeek { get; set; }
    public int? DayOfMonth { get; set; }
    public string TimeZone { get; set; } = "UTC";
    public List<string> Recipients { get; set; } = [];
    public string Format { get; set; } = "xlsx";
    public int LookbackDays { get; set; } = 7;
    public int SenderIdentityId { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class ReportScheduleDto
{
    public int Id { get; set; }
    public int ReportDefinitionId { get; set; }
    public string ReportName { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;
    public string TimeOfDay { get; set; } = string.Empty;
    public int? DayOfWeek { get; set; }
    public int? DayOfMonth { get; set; }
    public string TimeZone { get; set; } = string.Empty;
    public List<string> Recipients { get; set; } = [];
    public string Format { get; set; } = string.Empty;
    public int LookbackDays { get; set; }
    public int SenderIdentityId { get; set; }
    public string? SenderAddress { get; set; }
    public int OwnerUserId { get; set; }
    public string? OwnerName { get; set; }
    public bool IsActive { get; set; }
    public DateTime NextRunAt { get; set; }
    public DateTime? LastRunAt { get; set; }
    public string? LastStatus { get; set; }
    public string? LastError { get; set; }
}

public sealed record ScheduleSenderOption(int Id, string EmailAddress, string DisplayName);

public interface IReportScheduleService
{
    /// <summary>Active senders on active email connections the caller may use.</summary>
    Task<List<ScheduleSenderOption>> GetSendersAsync(CancellationToken ct = default);

    Task<List<ReportScheduleDto>> ListAsync(CancellationToken ct = default);
    Task<ReportScheduleDto> CreateAsync(SaveReportScheduleRequest request, CancellationToken ct = default);
    Task<ReportScheduleDto> UpdateAsync(int id, SaveReportScheduleRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    /// <summary>Runs a schedule now, outside its timetable. Returns the updated row.</summary>
    Task<ReportScheduleDto> RunNowAsync(int id, CancellationToken ct = default);
}

public sealed class ReportScheduleService : IReportScheduleService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _audit;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAccessScope _scope;

    public ReportScheduleService(AppDbContext db, ICurrentUserService currentUser, IAuditService audit, IServiceScopeFactory scopeFactory, IAccessScope scope)
    {
        _scope = scope;
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _scopeFactory = scopeFactory;
    }

    public async Task<List<ScheduleSenderOption>> GetSendersAsync(CancellationToken ct = default)
    {
        var allowed = await _scope.GetAllowedConnectionIdsAsync(ct);
        var query = _db.EmailSenderIdentities.AsNoTracking().Where(s => s.IsActive && s.EmailConfiguration.IsActive);
        if (allowed is not null)
        {
            var ids = allowed.ToList();
            query = query.Where(s => s.EmailConfiguration.ConnectionId != null && ids.Contains(s.EmailConfiguration.ConnectionId.Value));
        }
        return await query.OrderByDescending(s => s.IsDefault).ThenBy(s => s.EmailAddress)
            .Select(s => new ScheduleSenderOption(s.Id, s.EmailAddress, s.DisplayName))
            .ToListAsync(ct);
    }

    public async Task<List<ReportScheduleDto>> ListAsync(CancellationToken ct = default)
    {
        var query = Visible();
        return await Project(query.OrderBy(s => s.NextRunAt)).ToListAsync(ct);
    }

    public async Task<ReportScheduleDto> CreateAsync(SaveReportScheduleRequest request, CancellationToken ct = default)
    {
        var schedule = new ReportSchedule { OwnerUserId = _currentUser.UserId ?? throw new ForbiddenException("Sign in to schedule reports.") };
        await ApplyAsync(schedule, request, ct);
        _db.ReportSchedules.Add(schedule);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Report.Scheduled", "Data",
            $"Scheduled report #{schedule.ReportDefinitionId} {schedule.Frequency.ToLowerInvariant()} to {schedule.Recipients.Split(',').Length} recipient(s).",
            "ReportSchedule", schedule.Id.ToString());
        return await GetAsync(schedule.Id, ct);
    }

    public async Task<ReportScheduleDto> UpdateAsync(int id, SaveReportScheduleRequest request, CancellationToken ct = default)
    {
        var schedule = await LoadAsync(id, ct);
        await ApplyAsync(schedule, request, ct);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Report.ScheduleUpdated", "Data", $"Updated report schedule #{id}.", "ReportSchedule", id.ToString());
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var schedule = await LoadAsync(id, ct);
        _db.ReportSchedules.Remove(schedule);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Report.ScheduleDeleted", "Data", $"Deleted report schedule #{id}.", "ReportSchedule", id.ToString());
    }

    public async Task<ReportScheduleDto> RunNowAsync(int id, CancellationToken ct = default)
    {
        await LoadAsync(id, ct);

        // The runner signs in as the schedule's owner through IHttpContextAccessor, whose setter
        // clears the context it replaces. Without suppressing flow that would be this request's.
        Task run;
        using (ExecutionContext.SuppressFlow())
        {
            run = Task.Run(() => ReportScheduleRunner.RunAsync(_scopeFactory, id, CancellationToken.None), CancellationToken.None);
        }
        await run;
        await _audit.LogAsync("Report.ScheduleRunNow", "Data", $"Ran report schedule #{id} on demand.", "ReportSchedule", id.ToString());
        return await GetAsync(id, ct);
    }

    private IQueryable<ReportSchedule> Visible() =>
        _currentUser.IsAdministrator ? _db.ReportSchedules : _db.ReportSchedules.Where(s => s.OwnerUserId == _currentUser.UserId);

    private async Task<ReportSchedule> LoadAsync(int id, CancellationToken ct) =>
        await Visible().FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new KeyNotFoundException("Report schedule not found.");

    private async Task<ReportScheduleDto> GetAsync(int id, CancellationToken ct) =>
        await Project(_db.ReportSchedules.Where(s => s.Id == id)).FirstAsync(ct);

    private IQueryable<ReportScheduleDto> Project(IQueryable<ReportSchedule> query) =>
        query.AsNoTracking().Select(s => new ReportScheduleDto
        {
            Id = s.Id,
            ReportDefinitionId = s.ReportDefinitionId,
            ReportName = s.ReportDefinition!.Name,
            Frequency = s.Frequency,
            TimeOfDay = s.TimeOfDay,
            DayOfWeek = s.DayOfWeek,
            DayOfMonth = s.DayOfMonth,
            TimeZone = s.TimeZone,
            Recipients = s.Recipients.Split(',', StringSplitOptions.None).ToList(),
            Format = s.Format,
            LookbackDays = s.LookbackDays,
            SenderIdentityId = s.SenderIdentityId,
            SenderAddress = _db.EmailSenderIdentities.Where(e => e.Id == s.SenderIdentityId).Select(e => e.EmailAddress).FirstOrDefault(),
            OwnerUserId = s.OwnerUserId,
            OwnerName = _db.AppUsers.Where(u => u.Id == s.OwnerUserId).Select(u => u.FirstName + " " + u.LastName).FirstOrDefault(),
            IsActive = s.IsActive,
            NextRunAt = s.NextRunAt,
            LastRunAt = s.LastRunAt,
            LastStatus = s.LastStatus,
            LastError = s.LastError
        });

    private async Task ApplyAsync(ReportSchedule schedule, SaveReportScheduleRequest request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        var reportVisible = await _db.ReportDefinitions.AnyAsync(r => r.Id == request.ReportDefinitionId && (r.IsShared || r.OwnerUserId == userId), ct);
        if (!reportVisible) throw new ArgumentException("Choose one of your saved reports.");

        var frequency = Catalogs.ReportScheduleCatalog.Frequencies
            .FirstOrDefault(f => f.Value.Equals(request.Frequency?.Trim(), StringComparison.OrdinalIgnoreCase))?.Value
            ?? throw new ArgumentException($"Frequency must be one of: {string.Join(", ", Catalogs.ReportScheduleCatalog.Frequencies.Select(f => f.Label.ToLowerInvariant()))}.");
        if (!TimeOnly.TryParseExact(request.TimeOfDay, "HH:mm", out _)) throw new ArgumentException("Give the time as HH:mm, e.g. 09:00.");
        if (frequency == "Weekly" && request.DayOfWeek is not (>= 0 and <= 6)) throw new ArgumentException("Choose the day of the week.");
        if (frequency == "Monthly" && (request.DayOfMonth is null || request.DayOfMonth < 1 || request.DayOfMonth > Catalogs.ReportScheduleCatalog.MaxDayOfMonth))
            throw new ArgumentException($"Choose a day of the month from 1 to {Catalogs.ReportScheduleCatalog.MaxDayOfMonth}.");
        if (string.IsNullOrWhiteSpace(request.TimeZone) || !TimeZoneInfo.TryFindSystemTimeZoneById(request.TimeZone.Trim(), out _)) throw new ArgumentException("Choose a valid time zone.");

        var requestedFormat = request.Format?.Trim().ToLowerInvariant();
        var format = Catalogs.ReportScheduleCatalog.Formats.FirstOrDefault(f => f.Value == requestedFormat)?.Value
            ?? throw new ArgumentException($"Format must be one of: {string.Join(", ", Catalogs.ReportScheduleCatalog.Formats.Select(f => f.Label))}.");
        var lookback = Catalogs.ReportScheduleCatalog.LookbackDays;
        if (request.LookbackDays < lookback.Min || request.LookbackDays > lookback.Max)
            throw new ArgumentException($"Each run can cover {lookback.Min} to {lookback.Max} days.");

        var recipients = (request.Recipients ?? []).Select(r => r.Trim()).Where(r => r.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (recipients.Count == 0 || recipients.Count > Catalogs.ReportScheduleCatalog.MaxRecipients)
            throw new ArgumentException($"Add 1 to {Catalogs.ReportScheduleCatalog.MaxRecipients} recipients.");
        var bad = recipients.FirstOrDefault(r => !System.Net.Mail.MailAddress.TryCreate(r, out var m) || m.Address != r);
        if (bad is not null) throw new ArgumentException($"\"{bad}\" is not a valid email address.");

        if (!(await GetSendersAsync(ct)).Any(s => s.Id == request.SenderIdentityId))
            throw new ArgumentException("Choose the email sender the report goes out from.");

        schedule.ReportDefinitionId = request.ReportDefinitionId;
        schedule.Frequency = frequency;
        schedule.TimeOfDay = request.TimeOfDay;
        schedule.DayOfWeek = frequency == "Weekly" ? request.DayOfWeek : null;
        schedule.DayOfMonth = frequency == "Monthly" ? request.DayOfMonth : null;
        schedule.TimeZone = request.TimeZone.Trim();
        schedule.Recipients = string.Join(',', recipients);
        schedule.Format = format;
        schedule.LookbackDays = request.LookbackDays;
        schedule.SenderIdentityId = request.SenderIdentityId;
        schedule.IsActive = request.IsActive;
        schedule.NextRunAt = ReportScheduleRunner.NextRun(schedule, DateTime.UtcNow);
    }
}

/// <summary>Runs schedules: works out when, builds the export as the owner, and emails it.</summary>
public static class ReportScheduleRunner
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    /// <summary>The first run strictly after <paramref name="afterUtc"/>, in the schedule's time zone.</summary>
    public static DateTime NextRun(ReportSchedule s, DateTime afterUtc)
    {
        var zone = ComplianceGuard.ResolveZone(s.TimeZone, TimeZoneInfo.Utc);
        var time = TimeOnly.ParseExact(s.TimeOfDay, "HH:mm");
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(afterUtc, DateTimeKind.Utc), zone);

        for (var day = DateOnly.FromDateTime(localNow); ; day = day.AddDays(1))
        {
            var matches = s.Frequency switch
            {
                "Daily" => true,
                "Weekly" => (int)day.DayOfWeek == (s.DayOfWeek ?? 1),
                _ => day.Day == (s.DayOfMonth ?? 1)
            };
            if (!matches) continue;

            var local = day.ToDateTime(time, DateTimeKind.Unspecified);
            // A time skipped by a daylight-saving jump runs an hour later rather than not at all.
            if (zone.IsInvalidTime(local)) local = local.AddHours(1);
            var utc = TimeZoneInfo.ConvertTimeToUtc(local, zone);
            if (utc > afterUtc) return utc;
        }
    }

    /// <summary>Builds and sends one schedule's report, recording the outcome. Never throws.</summary>
    public static async Task RunAsync(IServiceScopeFactory scopeFactory, int scheduleId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<AppDbContext>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ReportScheduleRunner));

        var schedule = await db.ReportSchedules.Include(s => s.ReportDefinition).FirstOrDefaultAsync(s => s.Id == scheduleId, ct);
        if (schedule is null) return;

        var accessor = services.GetRequiredService<IHttpContextAccessor>();
        var previousContext = accessor.HttpContext;
        try
        {
            var owner = await db.AppUsers.Include(u => u.Role).AsNoTracking().FirstOrDefaultAsync(u => u.Id == schedule.OwnerUserId, ct);
            if (owner is null || !owner.IsActive)
                throw new InvalidOperationException("The person who scheduled this report is no longer active.");
            if (schedule.ReportDefinition is null)
                throw new InvalidOperationException("The saved report was deleted.");

            // Run as the owner: the export sees exactly the connections they may see, no more.
            var isAdmin = owner.IsAdministrator || (owner.Role?.IsAdministrator ?? false);
            accessor.HttpContext = new DefaultHttpContext
            {
                RequestServices = services,
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(AuthClaims.UserId, owner.Id.ToString()),
                    new Claim(AuthClaims.Email, owner.Email),
                    new Claim(AuthClaims.Name, owner.FullName),
                    new Claim(AuthClaims.IsAdministrator, isAdmin ? "true" : "false")
                ], authenticationType: "ReportSchedule"))
            };

            var currentUser = services.GetRequiredService<ICurrentUserService>();
            if (!await currentUser.HasPermissionAsync("Reporting.Export"))
                throw new InvalidOperationException("The person who scheduled this report can no longer export reports.");

            var filters = JsonSerializer.Deserialize<ReportQueryRequest>(schedule.ReportDefinition.FiltersJson, Json) ?? new ReportQueryRequest();
            var columns = JsonSerializer.Deserialize<List<string>>(schedule.ReportDefinition.ColumnsJson, Json);
            var to = DateTime.UtcNow;
            filters.From = to.AddDays(-schedule.LookbackDays);
            filters.To = to;

            var export = await services.GetRequiredService<IReportExportService>().ExportAsync(filters, columns is { Count: > 0 } ? columns : null, schedule.Format);

            var sender = await db.EmailSenderIdentities.AsNoTracking().FirstOrDefaultAsync(s => s.Id == schedule.SenderIdentityId, ct)
                ?? throw new InvalidOperationException("The email sender for this schedule was removed.");
            var (provider, context) = await services.GetRequiredService<IEmailProviderFactory>().ResolveByConfigurationAsync(sender.EmailConfigurationId, ct);
            var domain = sender.EmailAddress.Contains('@') ? sender.EmailAddress.Split('@')[1] : "localhost";
            var name = schedule.ReportDefinition.Name;
            var period = $"{filters.From:dd MMM yyyy} – {to:dd MMM yyyy} (UTC)";

            var result = await provider.SendAsync(new EmailMessage
            {
                From = new EmailAddress(sender.EmailAddress, sender.DisplayName),
                To = schedule.Recipients.Split(',').Select(r => new EmailAddress(r)).ToList(),
                Subject = $"{name} — {period}",
                HtmlBody = $"<p>Your scheduled report <strong>{System.Net.WebUtility.HtmlEncode(name)}</strong> for {period} is attached.</p>"
                         + "<p style=\"color:#667085;font-size:12px\">Sent by Waba Connect. To change or stop it, open Reporting › Scheduled reports.</p>",
                Attachments = [new EmailAttachment(export.FileName, export.ContentType, export.Content)],
                MessageId = services.GetRequiredService<IMimeMessageBuilder>().NewMessageId(domain),
                Tags = new Dictionary<string, string> { ["purpose"] = "scheduled-report" },
            }, context, ct);

            if (!result.Success) throw new InvalidOperationException(result.ErrorMessage ?? "The email could not be sent.");

            schedule.LastStatus = "Sent";
            schedule.LastError = null;
            await services.GetRequiredService<IAuditService>().LogAsync("Report.ScheduledSent", "Data",
                $"Sent scheduled report \"{name}\" ({schedule.Format}) to {schedule.Recipients.Split(',').Length} recipient(s).",
                "ReportSchedule", schedule.Id.ToString());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Scheduled report {ScheduleId} failed.", scheduleId);
            schedule.LastStatus = "Failed";
            schedule.LastError = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
        }
        finally
        {
            accessor.HttpContext = previousContext;
        }

        schedule.LastRunAt = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
    }
}

/// <summary>Checks every minute for schedules that are due.</summary>
public sealed class ReportScheduleWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReportScheduleWorker> _logger;

    public ReportScheduleWorker(IServiceScopeFactory scopeFactory, ILogger<ReportScheduleWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                List<(int Id, DateTime Due)> due;
                using (var scope = _scopeFactory.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var now = DateTime.UtcNow;
                    due = (await db.ReportSchedules.AsNoTracking()
                        .Where(s => s.IsActive && s.NextRunAt <= now)
                        .OrderBy(s => s.NextRunAt).Take(10)
                        .Select(s => new { s.Id, s.NextRunAt }).ToListAsync(stoppingToken))
                        .Select(s => (s.Id, s.NextRunAt)).ToList();

                    foreach (var (id, dueAt) in due.ToList())
                    {
                        var row = await db.ReportSchedules.AsNoTracking().FirstAsync(s => s.Id == id, stoppingToken);
                        var next = ReportScheduleRunner.NextRun(row, DateTime.UtcNow);
                        // Claim: moving NextRunAt on only if nobody else has, so one instance runs it.
                        var claimed = await db.ReportSchedules.Where(s => s.Id == id && s.NextRunAt == dueAt)
                            .ExecuteUpdateAsync(u => u.SetProperty(s => s.NextRunAt, next), stoppingToken);
                        if (claimed == 0) due.Remove((id, dueAt));
                    }
                }

                foreach (var (id, _) in due)
                    await ReportScheduleRunner.RunAsync(_scopeFactory, id, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Report schedule cycle failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
        }
    }
}
