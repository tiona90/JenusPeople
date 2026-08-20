using Serilog.Context;

namespace API.Middleware;

/// <summary>
/// Gives every request one id that appears in three places: on each log line the
/// request produces, in the error response body if it fails, and in the
/// X-Correlation-ID response header. That is what turns "a user saw a 500 at 14:32"
/// into the exact lines that produced it, without grepping by timestamp and hoping
/// the server was quiet.
///
/// The id is <c>HttpContext.TraceIdentifier</c>, which
/// <see cref="Models.ApiErrorResponse"/> and Serilog's request log already read —
/// overwriting it here is what makes them agree rather than each inventing its own.
///
/// A caller may supply the id so a chain of calls shares one, but only if it is
/// something safe to write into a log file (see <see cref="Sanitize"/>).
/// </summary>
public class CorrelationIdMiddleware(RequestDelegate next)
{
    /// <summary>Read from the request when present, always set on the response.</summary>
    public const string HeaderName = "X-Correlation-ID";

    /// <summary>The Serilog property name; the console and file sinks both print it.</summary>
    public const string LogPropertyName = "CorrelationId";

    private const int MaxLength = 64;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = Sanitize(context.Request.Headers[HeaderName])
            ?? Guid.NewGuid().ToString("n");

        context.TraceIdentifier = correlationId;

        // OnStarting rather than a direct assignment: middleware further down may
        // clear the response (the exception handler does), and the header has to
        // survive that. It is written once, just before the first byte goes out.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty(LogPropertyName, correlationId))
        {
            await next(context);
        }
    }

    /// <summary>
    /// The caller's id if it is one worth trusting: a bounded length and nothing but
    /// url-safe characters. Anything else — punctuation, whitespace, a newline, a
    /// second value joined by a comma — is discarded in favour of a generated id.
    /// A request header is attacker-controlled, and this one ends up in a log file
    /// that people read and tools parse; a newline in it forges log lines.
    /// </summary>
    private static string? Sanitize(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate) || candidate.Length > MaxLength)
        {
            return null;
        }

        foreach (var character in candidate)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_'))
            {
                return null;
            }
        }

        return candidate;
    }
}
