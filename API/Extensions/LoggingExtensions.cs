using System.Security.Claims;
using API.Middleware;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace API.Extensions;

/// <summary>
/// Serilog, replacing the default console logger.
///
/// Two sinks, because they answer different questions. The console carries a
/// human-readable line and is what ANCM captures when stdout logging is enabled
/// (DEPLOY.md §6). The file sink writes newline-delimited JSON under <c>Logs/</c>,
/// one file per day, keeping a fortnight — structured, so "every line for correlation
/// id abc" is a query rather than a grep.
///
/// The defaults below are set in code so a host with no Serilog configuration still
/// logs sensibly. A "Serilog" configuration section is read afterwards and wins, so
/// levels can be turned up on a deployed host by editing appsettings and recycling
/// the app pool.
/// </summary>
public static class LoggingExtensions
{
    private const string ConsoleTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {SourceContext}: {Message:lj}{NewLine}{Exception}";

    public static void UseSerilogLogging(this WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((context, services, logger) => logger
            .MinimumLevel.Information()
            // The framework's own chatter at Information is one line per request
            // times several; the request log below says the same thing once.
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
            // Without this the CorrelationId pushed by CorrelationIdMiddleware never
            // reaches a sink.
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "WorkTrack")
            .WriteTo.Console(outputTemplate: ConsoleTemplate)
            .WriteTo.File(
                new CompactJsonFormatter(),
                Path.Combine(context.HostingEnvironment.ContentRootPath, "Logs", "worktrack-.jsonl"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                // The app pool can run more than one worker process, and both write
                // here.
                shared: true)
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services));
    }

    /// <summary>
    /// One line per request — method, path, status, elapsed — instead of the several
    /// the framework emits. Add it after the correlation-id middleware so the line
    /// carries the id.
    /// </summary>
    public static void UseRequestLogging(this WebApplication app)
    {
        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate =
                "{RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0} ms";

            options.GetLevel = (httpContext, _, exception) =>
            {
                if (exception is not null || httpContext.Response.StatusCode >= 500)
                {
                    return LogEventLevel.Error;
                }

                if (httpContext.Response.StatusCode >= 400)
                {
                    return LogEventLevel.Warning;
                }

                // A probe every few seconds would otherwise be most of the log.
                return HealthCheckExtensions.IsHealthPath(httpContext.Request.Path)
                    ? LogEventLevel.Verbose
                    : LogEventLevel.Information;
            };

            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set(
                    CorrelationIdMiddleware.LogPropertyName,
                    httpContext.TraceIdentifier);

                // The user id, not the name: the name is the person's email address,
                // and a log file is a poor place to accumulate those. The id joins to
                // a user through the database when someone actually needs to know.
                var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!string.IsNullOrEmpty(userId))
                {
                    diagnosticContext.Set("UserId", userId);
                }
            };
        });
    }
}
