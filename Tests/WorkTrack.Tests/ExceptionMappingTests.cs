using System.Text.Json;
using API.Middleware;
using Application.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// GlobalExceptionMiddleware used to read ArgumentException, InvalidOperationException
/// and KeyNotFoundException as the caller's fault — 400, 409 and 404 — and returned
/// the exception's message as the body's details, in Production too. Those types
/// are mostly what EF and the BCL throw for bugs, so a query that could not be
/// translated reached the client as a 409 with the query's shape in it, and never
/// reached the System Log. Now only RequestRefusedException is the caller's fault;
/// the rest are server errors with no details.
/// </summary>
public class ExceptionMappingTests
{
    private sealed class ProductionEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "WorkTrack.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static async Task<(int Status, string Body)> InvokeAsync(Exception thrown)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/timesheets/ts/entries";
        context.Response.Body = new MemoryStream();

        var middleware = new GlobalExceptionMiddleware(
            _ => throw thrown,
            NullLogger<GlobalExceptionMiddleware>.Instance,
            new ProductionEnvironment(),
            new ConfigurationBuilder().Build());

        await middleware.InvokeAsync(context);
        context.Response.Body.Position = 0;
        return (context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    [Fact]
    public async Task A_refusal_is_a_400_that_tells_the_caller_why()
    {
        var (status, body) = await InvokeAsync(new RequestRefusedException("Entries for future dates are not allowed."));

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Entries for future dates are not allowed.", json.RootElement.GetProperty("message").GetString());
    }

    public static TheoryData<Exception> FrameworkFaults() => new(
        new InvalidOperationException("The LINQ expression 'DbSet<Timesheet>()' could not be translated."),
        new KeyNotFoundException("The given key 'secret' was not present in the dictionary."),
        new ArgumentNullException("connectionString"));

    [Theory]
    [MemberData(nameof(FrameworkFaults))]
    public async Task A_framework_exception_is_a_server_error_with_no_internals(Exception fault)
    {
        var (status, body) = await InvokeAsync(fault);

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.DoesNotContain("LINQ", body);
        Assert.DoesNotContain("secret", body);
        Assert.DoesNotContain("connectionString", body);
    }
}
