using Application.Core;
using System.Text.Json;
using API.Extensions;
using Application.SystemErrors;
using FluentValidation;

namespace API.Middleware;

public class GlobalExceptionMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionMiddleware> logger,
    IHostEnvironment environment,
    IConfiguration configuration)
{
    private static readonly string[] OAuthCallbackPaths = ["/signin-github", "/signin-google"];

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            // A caller that gave up is not a server fault. Kestrel signals it by
            // cancelling RequestAborted, which surfaces as OperationCanceledException
            // from whatever was mid-flight — most often the response writer itself.
            // There is nobody left to send a status to, and reporting it as an
            // unhandled 500 buries real errors in the log: it was a readiness probe
            // with a shorter timeout than a cold mail-provider check that first
            // showed this up.
            //
            // Deliberately conditional on RequestAborted. A cancellation the client
            // did not cause — an HttpClient timeout inside a handler, say — is a
            // genuine server-side failure and keeps its 500.
            if (IsClientDisconnect(ex, context))
            {
                logger.LogInformation(
                    "Request {Path} was abandoned by the caller before it completed.",
                    context.Request.Path);
                return;
            }

            if (IsOAuthCallback(context.Request.Path))
            {
                await HandleOAuthFailureAsync(context, ex);
                return;
            }

            await HandleAsync(context, ex);
        }
    }

    /// <summary>
    /// Whether this exception is the caller hanging up rather than a failure to
    /// report. <see cref="TaskCanceledException"/> derives from
    /// <see cref="OperationCanceledException"/>, so both are covered.
    /// </summary>
    private static bool IsClientDisconnect(Exception ex, HttpContext context) =>
        ex is OperationCanceledException && context.RequestAborted.IsCancellationRequested;

    private async Task HandleAsync(HttpContext context, Exception ex)
    {
        switch (ex)
        {
            case ValidationException validationException:
                await WriteValidationFailureAsync(context, validationException);
                return;
            case BadHttpRequestException badHttpRequest:
                await WriteBadRequestAsync(context, badHttpRequest, "Invalid request.");
                return;
            case JsonException jsonException:
                await WriteBadRequestAsync(context, jsonException, "Malformed JSON payload.");
                return;
        }

        var statusCode = MapStatusCode(ex);
        var message = MapMessage(ex);

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(ex, "Unhandled exception for request {Path}", context.Request.Path);
        }
        else
        {
            logger.LogWarning(ex, "Handled exception for request {Path}", context.Request.Path);
        }

        if (context.Response.HasStarted)
        {
            if (statusCode >= StatusCodes.Status500InternalServerError)
                await ReportSystemErrorAsync(context, ex);
            throw ex;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        var details = statusCode >= StatusCodes.Status500InternalServerError && !environment.IsDevelopment()
            ? null
            : ex.Message;

        var response = ApiErrorResponseExtensions.Create(context, statusCode, message, details);
        await context.Response.WriteAsJsonAsync(response);

        // After the response, so a slow mail provider does not hold up the
        // caller's error page; the request scope (and its DbContext) is still
        // alive until this middleware returns.
        if (statusCode >= StatusCodes.Status500InternalServerError)
            await ReportSystemErrorAsync(context, ex);
    }

    /// <summary>
    /// A 500 is the system's fault, and the System Administrators are the people
    /// who keep the system running, so they are told — with the correlation id
    /// that finds the log lines. A 4xx is the caller's mistake and is not.
    ///
    /// The notifier runs in a scope of its own rather than the request's: it holds
    /// a DbContext, and the request's may be the very thing that just threw. It
    /// is optional, so a host that has not registered it (a test) still works,
    /// and it is given no cancellation token, because a caller who hangs up the
    /// moment the 500 lands must not take the report with them. The notifier
    /// never throws; the guard here is for resolution failing.
    /// </summary>
    private async Task ReportSystemErrorAsync(HttpContext context, Exception ex)
    {
        try
        {
            using var scope = context.RequestServices.GetRequiredService<IServiceScopeFactory>().CreateScope();
            var notifier = scope.ServiceProvider.GetService<SystemErrorNotifier>();
            if (notifier is null) return;
            var source = $"{context.Request.Method} {context.Request.Path}";
            await notifier.NotifyAsync(new SystemErrorReport(source, ex, context.TraceIdentifier), CancellationToken.None);
        }
        catch (Exception reportEx)
        {
            logger.LogError(reportEx, "Could not report the unhandled exception for {Path} to the System Administrators.", context.Request.Path);
        }
    }

    private async Task HandleOAuthFailureAsync(HttpContext context, Exception ex)
    {
        logger.LogWarning(ex, "OAuth callback failure for {Path}", context.Request.Path);

        if (context.Response.HasStarted)
        {
            return;
        }

        var clientBase = configuration["AppUrls:ClientBaseUrl"]?.TrimEnd('/') ?? "http://localhost:5174";
        var msg = Uri.EscapeDataString("Sign-in failed. Please try again.");
        context.Response.Redirect($"{clientBase}/?authStatus=error&authMessage={msg}#login");
    }

    private async Task WriteValidationFailureAsync(HttpContext context, ValidationException ex)
    {
        logger.LogWarning(ex, "Validation failed for request {Path}", context.Request.Path);

        if (context.Response.HasStarted)
        {
            throw ex;
        }

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = "application/json";

        var errors = ex.Errors
            .GroupBy(error => error.PropertyName)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.ErrorMessage).ToArray());

        var response = ApiErrorResponseExtensions.Create(
            context,
            StatusCodes.Status400BadRequest,
            "One or more validation errors occurred.",
            errors: errors);

        await context.Response.WriteAsJsonAsync(response);
    }

    private async Task WriteBadRequestAsync(HttpContext context, Exception ex, string message)
    {
        logger.LogWarning(ex, "{Message} for {Path}", message, context.Request.Path);

        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = "application/json";

        var response = ApiErrorResponseExtensions.Create(
            context,
            StatusCodes.Status400BadRequest,
            message,
            ex.Message);

        await context.Response.WriteAsJsonAsync(response);
    }

    private static bool IsOAuthCallback(PathString path) =>
        OAuthCallbackPaths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));

    /* Only an exception thrown *as* a refusal is the caller's fault.
       ArgumentException, InvalidOperationException and KeyNotFoundException used to
       map to 400/409/404, but they are mostly what EF and the BCL throw for bugs —
       "could not be translated", "Sequence contains no elements" — so those reached
       the client as a 4xx with their internals in the details and never reached the
       System Log. They are server errors now; a deliberate refusal throws
       RequestRefusedException, whose message is written for the user. */
    private static int MapStatusCode(Exception ex) => ex switch
    {
        RequestRefusedException => StatusCodes.Status400BadRequest,
        UnauthorizedAccessException => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status500InternalServerError,
    };

    private static string MapMessage(Exception ex) => ex switch
    {
        RequestRefusedException refused => refused.Message,
        UnauthorizedAccessException => "You do not have permission to perform this action.",
        _ => "An unexpected server error occurred.",
    };
}
