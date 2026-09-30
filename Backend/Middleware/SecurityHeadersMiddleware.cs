namespace WhatsAppCampaignApi.Middleware;

/// <summary>
/// Baseline HTTP security headers on every response.
/// </summary>
/// <remarks>
/// This is a JSON API plus a handful of static uploads, so the policy is strict: nothing is
/// allowed to render as a page, frame, or run script from this origin. The few server-rendered
/// pages (the unsubscribe confirmation) are self-contained with inline styles only.
/// </remarks>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            // Never sniff a response into a different type: an upload served as image/png stays
            // an image even if its bytes look like HTML.
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["X-Permitted-Cross-Domain-Policies"] = "none";

            // API responses and uploads get the locked-down policy. Other paths are left alone so
            // a single-page app served from this host (with its own CSP) is not broken, and so the
            // development-only Swagger UI can run its script.
            if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/uploads"))
            {
                headers["Content-Security-Policy"] =
                    "default-src 'none'; img-src 'self' data:; style-src 'unsafe-inline'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
            }

            // API responses carry personal data; intermediaries must not keep copies.
            if (context.Request.Path.StartsWithSegments("/api") && !headers.ContainsKey("Cache-Control"))
            {
                headers["Cache-Control"] = "no-store";
            }

            return Task.CompletedTask;
        });

        return _next(context);
    }
}
