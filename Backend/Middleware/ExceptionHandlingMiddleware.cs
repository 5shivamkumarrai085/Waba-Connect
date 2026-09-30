using FluentValidation;
using System.Text.Json;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Services.Security;

namespace WhatsAppCampaignApi.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger, IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client went away (closed the tab, navigated, timed out). Not a server fault:
            // no error log, no audit row, and nobody is listening for a response body.
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 499;
            }
        }
        catch (Exception ex)
        {
            // Logged inside HandleExceptionAsync, once the exception has been classified.
            // Logging here meant every deliberate business rejection — "campaign name already
            // taken", "cannot delete a built-in role" — was written at Error with a full stack
            // trace, so the Errors tab was mostly the app working correctly.
            await HandleExceptionAsync(context, ex);
        }
    }

    /// <summary>
    /// True for exceptions this middleware deliberately maps to a 4xx. They are the API telling
    /// a caller no, not the server failing, so they are recorded as warnings without a stack
    /// trace.
    /// </summary>
    private static bool IsExpectedClientError(Exception exception) => exception switch
    {
        ValidationException or UnauthorizedAccessException or ForbiddenException => true,
        KeyNotFoundException or ArgumentException or InvalidOperationException => ThrownByThisApplication(exception),
        _ => IsUniqueViolation(exception)
    };

    /// <summary>
    /// Whether the exception came from this application's own code. Services throw
    /// InvalidOperationException, ArgumentException and KeyNotFoundException deliberately, with a
    /// message written for the operator; the framework and EF throw the same types with internal
    /// detail (entity names, SQL state, parameter names) that must not reach a client. Only the
    /// first kind is mapped to a 4xx with its message; the second is a 500.
    /// </summary>
    private static bool ThrownByThisApplication(Exception exception) =>
        exception.TargetSite?.DeclaringType?.Assembly == typeof(ExceptionHandlingMiddleware).Assembly;

    /// <summary>A unique-index violation: two requests created the same thing at once.</summary>
    private static bool IsUniqueViolation(Exception exception) =>
        exception is Microsoft.EntityFrameworkCore.DbUpdateException { InnerException: Npgsql.PostgresException { SqlState: "23505" } };

    /// <summary>
    /// Records a server fault as a Failed audit entry.
    ///
    /// <para>
    /// The audit service is resolved per request rather than injected: this middleware is
    /// constructed once for the application's lifetime, so holding a scoped service in a field
    /// would capture the first request's scope for every request after it.
    /// </para>
    /// <para>
    /// Wrapped whole. This runs on a path that is already handling a failure — if the audit
    /// write throws, the caller must still receive the response describing their original error,
    /// not a second one from the logging of the first.
    /// </para>
    /// </summary>
    private async Task TryAuditServerErrorAsync(HttpContext context, Exception exception)
    {
        try
        {
            var auditService = context.RequestServices.GetService<IAuditService>();
            if (auditService is null) return;

            // Discard whatever the failed operation left pending.
            //
            // A DbUpdateException leaves its entity tracked as Added, so the audit service's own
            // SaveChanges would retry that same doomed insert alongside the audit row and throw
            // again — silently losing the record of the very failure being reported. Verified:
            // before this, a 500 from an over-length column produced no audit entry at all.
            //
            // Safe here specifically because the request has already failed and nothing else
            // will use this context; the response is being written as we speak.
            var dbContext = context.RequestServices.GetService<Data.AppDbContext>();
            dbContext?.ChangeTracker.Clear();

            // Derives the module from the route's controller, so a new controller needs no
            // change here. Falls back when there is no route data (a fault thrown before
            // routing ran).
            //
            // Singularised because controllers are named in the plural by ASP.NET convention
            // while every service-written event uses the singular entity name. Without this the
            // module filter listed both "Contact" and "Contacts" as if they were different
            // things.
            var controller = context.GetRouteValue("controller")?.ToString();
            var module = Singularise(controller) ?? "Server";

            await auditService.LogAsync(
                $"{module}.Failed",
                "Security",
                $"{context.Request.Method} {context.Request.Path} failed — {exception.GetType().Name}. " +
                $"Trace {context.TraceIdentifier}.",
                entityType: "Request",
                entityId: context.TraceIdentifier);
        }
        catch (Exception auditFailure)
        {
            _logger.LogError(auditFailure, "Failed to record a server error in the audit trail.");
        }
    }

    /// <summary>
    /// Turns a plural controller name into the singular module name the audit trail uses.
    ///
    /// Deliberately narrow: "Campaigns" → "Campaign", "Activities" → "Activity". Names already
    /// singular ("Waba"), and those ending in a double s ("Address"), are left alone rather than
    /// mangled — an unrecognised shape is better than a wrong one.
    /// </summary>
    private static string? Singularise(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length <= 3) return name;
        if (name.EndsWith("ies", StringComparison.Ordinal)) return name[..^3] + "y";
        if (name.EndsWith("ss", StringComparison.Ordinal)) return name;
        return name.EndsWith("s", StringComparison.Ordinal) ? name[..^1] : name;
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        // Once the response has begun (a streamed export, say) its status and headers are gone;
        // writing an error body would only corrupt it. Log, and let the connection end.
        if (context.Response.HasStarted)
        {
            _logger.LogError(exception, "Unhandled exception after the response started on {Method} {Path}.",
                context.Request.Method, context.Request.Path);
            return;
        }

        if (IsExpectedClientError(exception))
        {
            _logger.LogWarning(
                "{ExceptionType} on {Method} {Path}: {Message}",
                exception.GetType().Name,
                context.Request.Method,
                context.Request.Path,
                exception.Message);
        }
        else
        {
            _logger.LogError(exception, "An unhandled exception occurred.");

            // Genuine faults go to the audit trail so a failed operation is findable from the
            // activity log's Status filter, not only from the server logs.
            //
            // Deliberately only in this branch: the 4xx cases above are the API correctly saying
            // no — "campaign name already taken", "role is built-in" — and auditing those would
            // fill the trail with the app working as designed.
            await TryAuditServerErrorAsync(context, exception);
        }

        context.Response.ContentType = "application/json";

        // The trace id ties what the user reports to the log entry, without exposing internals.
        context.Response.Headers["X-Trace-Id"] = context.TraceIdentifier;
        var response = new ApiResponse { Success = false };
        var fromThisApp = ThrownByThisApplication(exception);

        switch (exception)
        {
            case ValidationException validationEx:
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                response.Message = "Validation failed";
                response.Errors = validationEx.Errors.Select(e => e.ErrorMessage).ToList();
                break;

            case KeyNotFoundException when fromThisApp:
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                response.Message = exception.Message;
                break;

            case ArgumentException when fromThisApp:
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                response.Message = exception.Message;
                break;

            case Microsoft.EntityFrameworkCore.DbUpdateException when IsUniqueViolation(exception):
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                response.Message = "This record already exists or was just changed by someone else. Refresh and try again.";
                break;

            case ForbiddenException:
                // 403, not 401: the session is fine, the action is outside the caller's scope.
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                response.Message = exception.Message;
                break;

            case UnauthorizedAccessException:
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                response.Message = "Unauthorized";
                break;

            case InvalidOperationException when fromThisApp:
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                response.Message = exception.Message;
                break;

            default:
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                response.Message = $"An internal server error occurred. Reference: {context.TraceIdentifier}.";
                if (_env.IsDevelopment())
                {
                    response.Errors = [exception.Message, exception.StackTrace ?? ""];
                }
                break;
        }

        var result = JsonSerializer.Serialize(response, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await context.Response.WriteAsync(result);
    }
}
