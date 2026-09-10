using System.Text.Json;
using API.Extensions;
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

    private static int MapStatusCode(Exception ex) => ex switch
    {
        UnauthorizedAccessException => StatusCodes.Status403Forbidden,
        KeyNotFoundException => StatusCodes.Status404NotFound,
        ArgumentException => StatusCodes.Status400BadRequest,
        InvalidOperationException => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status500InternalServerError,
    };

    private static string MapMessage(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "You do not have permission to perform this action.",
        KeyNotFoundException => "The requested resource was not found.",
        ArgumentException => "The request contains invalid data.",
        InvalidOperationException => "The requested operation could not be completed.",
        _ => "An unexpected server error occurred.",
    };
}
