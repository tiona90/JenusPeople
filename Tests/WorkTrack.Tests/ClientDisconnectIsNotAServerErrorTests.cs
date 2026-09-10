using API.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// A caller that hangs up mid-request used to be reported as an unhandled server
/// error: <see cref="OperationCanceledException"/> fell through
/// <c>GlobalExceptionMiddleware</c>'s status map to the 500 default, logging
/// "Unhandled exception" at Error and then trying to write a body to a socket
/// nobody was reading.
///
/// Found for real, not imagined: <c>GET /health/ready</c> ran a cold mail-provider
/// check that took 8.04s against a probe that gave up at 8s, and the log showed a
/// 500 for a request that had been served correctly. Noise like that is what buries
/// the errors worth reading.
///
/// The distinction these hold is the important half — only a cancellation the
/// *client* caused is benign. A cancellation from inside the server (an HttpClient
/// timeout in a handler, say) is a real failure and keeps its 500.
/// </summary>
public class ClientDisconnectIsNotAServerErrorTests
{
    private sealed class StubEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "WorkTrack.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    /// <summary>
    /// Runs the real middleware over a request whose handler throws
    /// <paramref name="thrown"/>, with the client's connection either already gone
    /// or still open.
    /// </summary>
    private static async Task<HttpContext> InvokeAsync(Exception thrown, bool clientGone)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/health/ready";
        context.Response.Body = new MemoryStream();

        using var aborted = new CancellationTokenSource();
        if (clientGone)
        {
            await aborted.CancelAsync();
        }

        context.RequestAborted = aborted.Token;

        var middleware = new GlobalExceptionMiddleware(
            _ => throw thrown,
            NullLogger<GlobalExceptionMiddleware>.Instance,
            new StubEnvironment(),
            new ConfigurationBuilder().Build());

        await middleware.InvokeAsync(context);
        return context;
    }

    public static TheoryData<Exception> Cancellations() => new(
        new OperationCanceledException(),
        new TaskCanceledException());

    [Theory]
    [MemberData(nameof(Cancellations))]
    public async Task A_caller_that_hangs_up_is_not_turned_into_a_server_error(Exception cancellation)
    {
        var context = await InvokeAsync(cancellation, clientGone: true);

        // Untouched: 200 is DefaultHttpContext's initial value, and nothing was
        // written. The point is that no 500 was manufactured for a request the
        // server had actually handled.
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
    }

    /// <summary>
    /// The half that must not regress. A cancellation the client did not cause is a
    /// server-side failure — swallowing it would hide exactly the outages this
    /// middleware exists to report.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cancellations))]
    public async Task A_cancellation_the_client_did_not_cause_is_still_a_500(Exception cancellation)
    {
        var context = await InvokeAsync(cancellation, clientGone: false);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.True(context.Response.Body.Length > 0);
    }

    // An ordinary fault is unaffected, whether or not the caller is still there.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_unrelated_exception_is_still_a_500(bool clientGone)
    {
        var context = await InvokeAsync(new InvalidTimeZoneException("boom"), clientGone);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
    }

    /// <summary>
    /// The mapped non-500 cases keep working with the client gone too — the
    /// disconnect check must not shadow them, since they are not cancellations.
    /// </summary>
    [Fact]
    public async Task A_mapped_exception_keeps_its_own_status_code()
    {
        var context = await InvokeAsync(new KeyNotFoundException(), clientGone: true);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }
}
