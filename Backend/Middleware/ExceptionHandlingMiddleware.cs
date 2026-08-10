using FluentValidation;
using System.Text.Json;
using WhatsAppCampaignApi.Models.DTOs.Common;

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
    private static bool IsExpectedClientError(Exception exception) => exception
        is ValidationException
        or KeyNotFoundException
        or ArgumentException
        or UnauthorizedAccessException
        or InvalidOperationException;

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
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
        }

        context.Response.ContentType = "application/json";
        var response = new ApiResponse { Success = false };

        switch (exception)
        {
            case ValidationException validationEx:
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                response.Message = "Validation failed";
                response.Errors = validationEx.Errors.Select(e => e.ErrorMessage).ToList();
                break;
                
            case KeyNotFoundException:
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                response.Message = exception.Message;
                break;
                
            case ArgumentException:
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                response.Message = exception.Message;
                break;
                
            case UnauthorizedAccessException:
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                response.Message = "Unauthorized";
                break;
                
            case InvalidOperationException:
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                response.Message = exception.Message;
                break;
                
            default:
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                response.Message = "An internal server error occurred.";
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
