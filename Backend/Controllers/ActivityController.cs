using System.Linq.Expressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.Activity;
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

    /// <summary>
    /// Sign-in events, which the Audit Events tab excludes.
    ///
    /// <para>
    /// Every sign-in already appears — with more detail — in the Login Successes and Login Errors
    /// tabs, which carry the email, user agent and failure reason. Leaving them in the audit
    /// stream as well meant routine logins outnumbered actual changes four to one, burying the
    /// thing the tab exists to show.
    /// </para>
    /// <para>
    /// <c>Auth.PasswordChanged</c> deliberately stays: it is a change to a credential, not a
    /// sign-in, and there is no other tab that records it.
    /// </para>
    /// <para>
    /// Applied by the list, the metric count and the filter options alike — a single definition
    /// so the three can never disagree about what the tab contains.
    /// </para>
    /// </summary>
    private static IQueryable<AuditLog> ExcludeSignInEvents(IQueryable<AuditLog> query) =>
        query.Where(a => a.Event != "Auth.LoginSucceeded" && a.Event != "Auth.LoginFailed");

    [HttpGet("metrics")]
    [RequiresPermission("ActivityLog.View")]
    public async Task<IActionResult> GetMetrics([FromQuery] string? filter)
    {
        var (since, until) = ResolveWindow(filter);

        var attempts = _dbContext.LoginAttempts.AsNoTracking();
        // Counted the same way the Audit Events tab lists them, so the card and the table's
        // "of N results" always agree.
        var audits = ExcludeSignInEvents(_dbContext.AuditLogs.AsNoTracking());

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

        // Distinct people who performed a change in this window.
        //
        // The null filter matters: UserId is null for background work (the scheduler, the
        // webhook), and Distinct() counts that null as one more "person" — which is part of why
        // this read as 11 against three real accounts. Accounts deleted since still count for
        // the period they acted in; that is the honest answer for an audit trail, and the card
        // is labelled "Users Acting" rather than "Active Users" so it cannot be misread as the
        // number of accounts that exist.
        var distinctActors = await audits
            .Where(a => a.UserId != null)
            .Select(a => a.UserId)
            .Distinct()
            .CountAsync();

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
            new { id = "active-users", title = "Users Acting", description = "People who made a change", iconName = "Users", value = distinctActors }
        };

        return Ok(new ApiResponse<object> { Success = true, Data = data });
    }

    [HttpGet("login-successes")]
    [RequiresPermission("ActivityLog.View")]
    public Task<IActionResult> GetLoginSuccesses(
        [FromQuery] string? search,
        [FromQuery] string? filter,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10) =>
        GetLoginAttemptsAsync(successful: true, search, filter, page, pageSize);

    [HttpGet("login-errors")]
    [RequiresPermission("ActivityLog.View")]
    public Task<IActionResult> GetLoginErrors(
        [FromQuery] string? search,
        [FromQuery] string? filter,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10) =>
        GetLoginAttemptsAsync(successful: false, search, filter, page, pageSize);

    /// <summary>
    /// Both sign-in tabs, filtered and paged in SQL.
    ///
    /// <para>
    /// One method for the two: they differed only by a boolean and one extra projected column,
    /// and keeping them separate meant every fix — the window, the search, and now the paging —
    /// had to be written twice and stay in step by hand.
    /// </para>
    /// <para>
    /// Previously a hard <c>.Take(500)</c> with no count, so the tabs could neither page nor say
    /// how many attempts there really were.
    /// </para>
    /// </summary>
    private async Task<IActionResult> GetLoginAttemptsAsync(
        bool successful,
        string? search,
        string? filter,
        int page,
        int pageSize)
    {
        // Clamped rather than trusted — pageSize arrives off the query string.
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 200 ? 10 : pageSize;

        var query = _dbContext.LoginAttempts.AsNoTracking().Where(a => a.Success == successful);
        query = ApplyWindow(query, filter);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(a => a.Email.ToLower().Contains(term) ||
                                     (a.IpAddress != null && a.IpAddress.Contains(term)));
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new LoginAttemptResponse
            {
                Id = a.Id.ToString(),
                Time = a.CreatedAt,
                Email = a.Email,
                IpAddress = a.IpAddress ?? "—",
                UserAgent = a.UserAgent ?? "—",
                Status = successful ? "Success" : "Failed",
                Reason = successful ? null : (a.FailureReason ?? "Unknown")
            })
            .ToListAsync();

        return Ok(new ApiResponse<PagedResponse<LoginAttemptResponse>>
        {
            Success = true,
            Data = new PagedResponse<LoginAttemptResponse>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            }
        });
    }

    /// <summary>
    /// The Audit Events table: filtered and paged in SQL.
    ///
    /// <para>
    /// This used to return a hard <c>.Take(500)</c> with no paging, which meant any filter only
    /// ever saw the most recent 500 rows — a date filter for last month found nothing once the
    /// table grew past that. Every parameter here narrows the whole table, and the count reported
    /// is the true count.
    /// </para>
    /// </summary>
    [HttpGet("audit-logs")]
    [RequiresPermission("ActivityLog.View")]
    public async Task<IActionResult> GetAuditLogs(
        [FromQuery] string? search,
        [FromQuery] string? filter,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? module,
        [FromQuery] string? action,
        [FromQuery] int? userId,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        // Clamped rather than trusted: pageSize comes straight off the query string, and an
        // unbounded value would let a caller pull the entire table in one request.
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 200 ? 10 : pageSize;

        var query = ExcludeSignInEvents(_dbContext.AuditLogs.AsNoTracking());

        // The relative pills (today/week/month) and an explicit date range are alternatives; an
        // explicit range wins because the user picked it directly.
        if (from.HasValue || to.HasValue)
        {
            if (from.HasValue)
                query = query.Where(a => a.CreatedAt >= from.Value.ToUniversalTime());
            if (to.HasValue)
                // Inclusive of the whole end day: a user picking 07 Aug means "through the 7th",
                // not "up to 00:00 on the 7th".
                query = query.Where(a => a.CreatedAt < to.Value.Date.AddDays(1).ToUniversalTime());
        }
        else
        {
            query = ApplyWindow(query, filter);
        }

        if (!string.IsNullOrWhiteSpace(module))
            query = query.Where(a => a.Module == module);

        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(a => a.Action == action);

        if (userId.HasValue)
            query = query.Where(a => a.UserId == userId.Value);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(a => a.Status == status);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(a => a.Event.ToLower().Contains(term) ||
                                     a.Category.ToLower().Contains(term) ||
                                     (a.Module != null && a.Module.ToLower().Contains(term)) ||
                                     (a.EntityName != null && a.EntityName.ToLower().Contains(term)) ||
                                     (a.UserName != null && a.UserName.ToLower().Contains(term)) ||
                                     (a.Description != null && a.Description.ToLower().Contains(term)));
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AuditLogResponse
            {
                Id = a.Id,
                EventNumber = a.EventNumber,
                Time = a.CreatedAt,
                Event = a.Event,
                Category = a.Category,
                Module = a.Module,
                Action = a.Action,
                Status = a.Status,
                User = a.UserName ?? "System",
                UserId = a.UserId,
                // Recorded since the audit trail shipped but never surfaced. Null for background
                // callers (scheduler, webhook), which genuinely have no originating address.
                IpAddress = a.IpAddress,
                UserAgent = a.UserAgent,
                Description = a.Description ?? string.Empty,
                EntityType = a.EntityType,
                EntityId = a.EntityId,
                EntityName = a.EntityName,
                ChangesJson = a.ChangesJson
            })
            .ToListAsync();

        return Ok(new ApiResponse<PagedResponse<AuditLogResponse>>
        {
            Success = true,
            Data = new PagedResponse<AuditLogResponse>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            }
        });
    }

    /// <summary>
    /// Distinct values present in the audit table, for the filter dropdowns.
    ///
    /// Queried rather than hardcoded so a module added later appears in the filter without a
    /// frontend change, and a module that has never written an entry is never offered.
    /// </summary>
    [HttpGet("audit-filters")]
    [RequiresPermission("ActivityLog.View")]
    public async Task<IActionResult> GetAuditFilterOptions()
    {
        // Scoped to the same rows the tab lists. Offering a value the table can never show —
        // "LoginSucceeded", say — is a filter that always returns nothing.
        var auditable = ExcludeSignInEvents(_dbContext.AuditLogs.AsNoTracking());

        var modules = await auditable
            .Where(a => a.Module != null)
            .Select(a => a.Module!)
            .Distinct().OrderBy(m => m).ToListAsync();

        var actions = await auditable
            .Where(a => a.Action != null && a.Action != "")
            .Select(a => a.Action!)
            .Distinct().OrderBy(a => a).ToListAsync();

        var statuses = await auditable
            .Where(a => a.Status != null)
            .Select(a => a.Status!)
            .Distinct().OrderBy(s => s).ToListAsync();

        var users = await auditable
            .Where(a => a.UserId != null && a.UserName != null)
            .Select(a => new AuditUserOption { Id = a.UserId!.Value, Name = a.UserName! })
            .Distinct().OrderBy(u => u.Name).ToListAsync();

        return Ok(new ApiResponse<AuditFilterOptionsResponse>
        {
            Success = true,
            Data = new AuditFilterOptionsResponse
            {
                Modules = modules,
                Actions = actions,
                Statuses = statuses,
                Users = users
            }
        });
    }
}
