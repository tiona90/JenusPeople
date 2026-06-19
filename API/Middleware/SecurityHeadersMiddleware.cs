namespace API.Middleware;

// Attaches modern security headers to every response. Uses OnStarting so the
// headers land even when downstream middleware calls Response.Clear() (e.g.
// GlobalExceptionMiddleware on its error path) — OnStarting fires just before
// the final flush, after any clears.
public class SecurityHeadersMiddleware(RequestDelegate next)
{
    // Strict policy for first-party content: JSON responses (CSP harmless) and
    // the branded HTML pages served by AccountController. Those pages use
    // inline <style> blocks but no <script>, hence `style-src 'unsafe-inline'`
    // and `script-src 'none'`.
    private const string StrictCsp =
        "default-src 'self'; " +
        "script-src 'none'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "font-src 'self'; " +
        "connect-src 'self'; " +
        "frame-ancestors 'none'; " +
        "base-uri 'none'; " +
        "form-action 'self'";

    // The React SPA (served same-origin from wwwroot) loads its own bundled
    // JS from this origin and relies on inline styles (MUI/Emotion inject
    // <style> tags at runtime). Images may come from Cloudinary / OAuth avatar
    // hosts over https. API calls and the SignalR socket are same-origin.
    private const string AppCsp =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: https:; " +
        "font-src 'self' data:; " +
        "connect-src 'self'; " +
        "frame-ancestors 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'";

    // Swagger UI loads its own JS/CSS bundles and uses inline initialiser
    // scripts, so a strict CSP would break it. Swagger is Development-only
    // (see Program.cs), so this relaxation only affects local dev surfaces.
    private const string SwaggerCsp =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "font-src 'self' data:; " +
        "connect-src 'self'; " +
        "frame-ancestors 'none'; " +
        "base-uri 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            var path = context.Request.Path.Value ?? string.Empty;
            var isSwagger = path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase);
            // API (JSON + branded account HTML pages) and the SignalR hub keep
            // the strict, script-free policy; everything else is the SPA shell.
            var isApi = path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/hubs", StringComparison.OrdinalIgnoreCase);

            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Content-Security-Policy"] = isSwagger ? SwaggerCsp : isApi ? StrictCsp : AppCsp;

            // Drop server fingerprinting headers if the host added them.
            headers.Remove("Server");
            headers.Remove("X-Powered-By");

            return Task.CompletedTask;
        });

        return next(context);
    }
}
