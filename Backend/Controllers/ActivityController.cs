using System.Linq.Expressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.Entities;

namespace WhatsAppCampaignApi.Controllers;

/// <summary>
/// Backs the pre-existing /activity-logs page.
///
/// <para>
/// That page has been calling these four endpoints since it shipped, but none of them existed —
/// every request 404'd and the service swallowed it into an empty array, so the page rendered
/// as "no data" rather than as broken. The response shapes here match what the page already
/// expects (see LoginSuccessModel and AuditLogModel), so it starts working with no frontend
/// change at all.
/// </para>
/// </summary>
[ApiController]
[Route("api/Activity")]
[Authorize]
public class ActivityController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public ActivityController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Resolves the page's filter values (today | yesterday | week | month | all) to a
    /// half-open [since, until) window.
    ///
    /// <para>
    /// Two things this deliberately gets right. First, buckets are cut on the *caller's*
    /// midnight, not UTC's: for an IST user, UTC midnight is 05:30 local, so a UTC-cut "today"
    /// silently omitted everything they did before breakfast. Second, "yesterday" has an upper
    /// bound — without one it returned everything since yesterday, making it a superset of
    /// "today" rather than a distinct bucket.
    /// </para>
    /// </summary>
    private (DateTime? Since, DateTime? Until) ResolveWindow(string? filter)
    {
        // The browser sends its IANA zone; fall back to UTC when it doesn't or the id is
        // unknown to this host, which is no worse than the previous behaviour.
        var zone = TimeZoneInfo.Utc;
        var tzHeader = Request.Headers["X-Timezone"].ToString();
        if (!string.IsNullOrWhiteSpace(tzHeader))
        {
            try { zone = TimeZoneInfo.FindSystemTimeZoneById(tzHeader); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);
        var localMidnight = localNow.Date;

        DateTime ToUtc(DateTime local) =>
            TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone);

        return filter?.ToLowerInvariant() switch
        {
            "today" => (ToUtc(localMidnight), null),
            "yesterday" => (ToUtc(localMidnight.AddDays(-1)), ToUtc(localMidnight)),
            "week" => (ToUtc(localMidnight.AddDays(-7)), null),
            "month" => (ToUtc(localMidnight.AddMonths(-1)), null),
            _ => (null, null)
        };
    }

    /// <summary>
    /// Applies the resolved window to a query. Generic over an expression rather than a shared
    /// base type, because LoginAttempt and AuditLog are unrelated classes that merely happen to
    /// both carry a CreatedAt — inventing a base entity for two tables would be a bigger change
    /// than this needs to be.
    /// </summary>
    private IQueryable<T> ApplyWindow<T>(IQueryable<T> query, string? filter, Expression<Func<T, DateTime>> createdAt)
    {
        var (since, until) = ResolveWindow(filter);
        if (!since.HasValue && !until.HasValue) return query;

        var parameter = createdAt.Parameters[0];
        Expression? predicate = null;

        if (since.HasValue)
            predicate = Expression.GreaterThanOrEqual(createdAt.Body, Expression.Constant(since.Value));

        if (until.HasValue)
        {
            var upper = Expression.LessThan(createdAt.Body, Expression.Constant(until.Value));
            predicate = predicate is null ? upper : Expression.AndAlso(predicate, upper);
        }

        return query.Where(Expression.Lambda<Func<T, bool>>(predicate!, parameter));
    }

    private IQueryable<LoginAttempt> ApplyWindow(IQueryable<LoginAttempt> query, string? filter) =>
        ApplyWindow(query, filter, a => a.CreatedAt);

    private IQueryable<AuditLog> ApplyWindow(IQueryable<AuditLog> query, string? filter) =>
        ApplyWindow(query, filter, a => a.CreatedAt);

    [HttpGet("metrics")]
    [RequiresPermission("ActivityLog.View")]
    public async Task<IActionResult> GetMetrics([FromQuery] string? filter)
    {
        var (since, until) = ResolveWindow(filter);

        var attempts = _dbContext.LoginAttempts.AsNoTracking();
        var audits = _dbContext.AuditLogs.AsNoTracking();

        if (since.HasValue)
        {
            attempts = attempts.Where(a => a.CreatedAt >= since.Value);
            audits = audits.Where(a => a.CreatedAt >= since.Value);
        }

        if (until.HasValue)
        {
            attempts = attempts.Where(a => a.CreatedAt < until.Value);
            audits = audits.Where(a => a.CreatedAt < until.Value);
        }

        var successes = await attempts.CountAsync(a => a.Success);
        var failures = await attempts.CountAsync(a => !a.Success);
        var auditCount = await audits.CountAsync();
        var distinctActors = await audits.Select(a => a.UserId).Distinct().CountAsync();

        // Field names match what MetricCard destructures. They previously did not — the
        // controller emitted {id, label, value} while the card read {title, description,
        // iconName, value}, so every card rendered as a bare number under a fallback icon.
        //
        // Four cards, not three: the page's loading skeleton reserves four, so returning three
        // made the grid reflow the moment data arrived.
        var data = new[]
        {
            new { id = "login-successes", title = "Login Successes", description = "Successful sign-ins", iconName = "ShieldCheck", value = successes },
            new { id = "login-errors", title = "Login Errors", description = "Failed sign-in attempts", iconName = "ShieldAlert", value = failures },
            new { id = "audit-events", title = "Audit Events", description = "Recorded changes", iconName = "FileText", value = auditCount },
            new { id = "active-users", title = "Active Users", description = "Distinct people acting", iconName = "Users", value = distinctActors }
        };

        return Ok(new ApiResponse<object> { Success = true, Data = data });
    }

    [HttpGet("login-successes")]
    [RequiresPermission("ActivityLog.View")]
    public async Task<IActionResult> GetLoginSuccesses([FromQuery] string? search, [FromQuery] string? filter)
    {
        var query = _dbContext.LoginAttempts.AsNoTracking().Where(a => a.Success);
        query = ApplyWindow(query, filter);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(a => a.Email.ToLower().Contains(term) ||
                                     (a.IpAddress != null && a.IpAddress.Contains(term)));
        }

        var data = await query
            .OrderByDescending(a => a.CreatedAt)
            .Take(500)
            .Select(a => new
            {
                id = a.Id.ToString(),
                time = a.CreatedAt,
                email = a.Email,
                ipAddress = a.IpAddress ?? "—",
                userAgent = a.UserAgent ?? "—",
                status = "Success"
            })
            .ToListAsync();

        return Ok(new ApiResponse<object> { Success = true, Data = data });
    }

    [HttpGet("login-errors")]
    [RequiresPermission("ActivityLog.View")]
    public async Task<IActionResult> GetLoginErrors([FromQuery] string? search, [FromQuery] string? filter)
    {
        var query = _dbContext.LoginAttempts.AsNoTracking().Where(a => !a.Success);
        query = ApplyWindow(query, filter);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(a => a.Email.ToLower().Contains(term) ||
                                     (a.IpAddress != null && a.IpAddress.Contains(term)));
        }

        var data = await query
            .OrderByDescending(a => a.CreatedAt)
            .Take(500)
            .Select(a => new
            {
                id = a.Id.ToString(),
                time = a.CreatedAt,
                email = a.Email,
                ipAddress = a.IpAddress ?? "—",
                userAgent = a.UserAgent ?? "—",
                status = "Failed",
                reason = a.FailureReason ?? "Unknown"
            })
            .ToListAsync();

        return Ok(new ApiResponse<object> { Success = true, Data = data });
    }

    [HttpGet("audit-logs")]
    [RequiresPermission("ActivityLog.View")]
    public async Task<IActionResult> GetAuditLogs([FromQuery] string? search, [FromQuery] string? filter)
    {
        var query = _dbContext.AuditLogs.AsNoTracking().AsQueryable();
        query = ApplyWindow(query, filter);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(a => a.Event.ToLower().Contains(term) ||
                                     a.Category.ToLower().Contains(term) ||
                                     (a.UserName != null && a.UserName.ToLower().Contains(term)) ||
                                     (a.Description != null && a.Description.ToLower().Contains(term)));
        }

        var data = await query
            .OrderByDescending(a => a.CreatedAt)
            .Take(500)
            .Select(a => new
            {
                id = a.Id.ToString(),
                time = a.CreatedAt,
                @event = a.Event,
                category = a.Category,
                user = a.UserName ?? "System",
                // Recorded since the audit trail shipped but never surfaced. Null for background
                // callers (scheduler, webhook), which genuinely have no originating address.
                ipAddress = a.IpAddress,
                description = a.Description ?? string.Empty
            })
            .ToListAsync();

        return Ok(new ApiResponse<object> { Success = true, Data = data });
    }
}
