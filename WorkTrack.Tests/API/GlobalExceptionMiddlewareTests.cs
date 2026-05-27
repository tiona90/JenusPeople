using System.Text.Json;
using API.Middleware;
using API.Models;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace WorkTrack.Tests.API;

/// <summary>
/// Drives the middleware with an in-memory HttpContext and a RequestDelegate
/// that throws the target exception, then deserializes the response body and
/// asserts on the ApiErrorResponse shape. No web host, no controller pipeline.
/// </summary>
public class GlobalExceptionMiddlewareTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static async Task<(int StatusCode, ApiErrorResponse? Body)> RunAsync(
        Exception toThrow,
        string environmentName = "Production",
        string path = "/api/anything")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        context.TraceIdentifier = "trace-123";

        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(environmentName);

        var config = Substitute.For<IConfiguration>();

        RequestDelegate next = _ => throw toThrow;

        var middleware = new GlobalExceptionMiddleware(
            next,
            NullLogger<GlobalExceptionMiddleware>.Instance,
            env,
            config);

        await middleware.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await JsonSerializer.DeserializeAsync<ApiErrorResponse>(
            context.Response.Body, JsonOpts);

        return (context.Response.StatusCode, body);
    }

    // ── status code mapping ───────────────────────────────────────────────

    [Fact]
    public async Task UnauthorizedAccessException_Maps_To_403_With_Permission_Message()
    {
        var (status, body) = await RunAsync(new UnauthorizedAccessException("nope"));
        Assert.Equal(StatusCodes.Status403Forbidden, status);
        Assert.NotNull(body);
        Assert.Equal(403, body!.StatusCode);
        Assert.Equal("You do not have permission to perform this action.", body.Message);
    }

    [Fact]
    public async Task KeyNotFoundException_Maps_To_404_With_NotFound_Message()
    {
        var (status, body) = await RunAsync(new KeyNotFoundException("missing"));
        Assert.Equal(StatusCodes.Status404NotFound, status);
        Assert.NotNull(body);
        Assert.Equal(404, body!.StatusCode);
        Assert.Equal("The requested resource was not found.", body.Message);
    }

    [Fact]
    public async Task ArgumentException_Maps_To_400_With_InvalidData_Message()
    {
        var (status, body) = await RunAsync(new ArgumentException("bad arg"));
        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.NotNull(body);
        Assert.Equal(400, body!.StatusCode);
        Assert.Equal("The request contains invalid data.", body.Message);
    }

    [Fact]
    public async Task InvalidOperationException_Maps_To_409_Conflict()
    {
        var (status, body) = await RunAsync(new InvalidOperationException("conflict"));
        Assert.Equal(StatusCodes.Status409Conflict, status);
        Assert.NotNull(body);
        Assert.Equal(409, body!.StatusCode);
        Assert.Equal("The requested operation could not be completed.", body.Message);
    }

    [Fact]
    public async Task Generic_Exception_Maps_To_500()
    {
        var (status, body) = await RunAsync(new Exception("boom"));
        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.NotNull(body);
        Assert.Equal(500, body!.StatusCode);
        Assert.Equal("An unexpected server error occurred.", body.Message);
    }

    // ── special-cased exceptions get dedicated 400 messages ───────────────

    [Fact]
    public async Task ValidationException_Returns_400_With_Errors_Dictionary()
    {
        var failures = new List<ValidationFailure>
        {
            new("Email", "Email is required."),
            new("Email", "Must be a valid email."),
            new("Password", "Too short."),
        };
        var (status, body) = await RunAsync(new ValidationException(failures));

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.NotNull(body);
        Assert.Equal(400, body!.StatusCode);
        Assert.Equal("One or more validation errors occurred.", body.Message);
        Assert.NotNull(body.Errors);
        Assert.True(body.Errors!.ContainsKey("Email"));
        Assert.Equal(2, body.Errors["Email"].Length); // both Email failures grouped together
        Assert.True(body.Errors.ContainsKey("Password"));
    }

    [Fact]
    public async Task JsonException_Returns_400_With_Malformed_Payload_Message()
    {
        var (status, body) = await RunAsync(new JsonException("unterminated"));
        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.NotNull(body);
        Assert.Equal("Malformed JSON payload.", body!.Message);
    }

    [Fact]
    public async Task BadHttpRequestException_Returns_400_With_InvalidRequest_Message()
    {
        var (status, body) = await RunAsync(new BadHttpRequestException("bad"));
        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.NotNull(body);
        Assert.Equal("Invalid request.", body!.Message);
    }

    // ── envelope shape ────────────────────────────────────────────────────

    [Fact]
    public async Task Response_Includes_Path_And_TraceId_From_Context()
    {
        var (_, body) = await RunAsync(new KeyNotFoundException("x"), path: "/api/v1/widgets/42");
        Assert.NotNull(body);
        Assert.Equal("/api/v1/widgets/42", body!.Path);
        Assert.Equal("trace-123", body.TraceId);
    }

    [Fact]
    public async Task Response_Timestamp_Is_Recent()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var (_, body) = await RunAsync(new KeyNotFoundException("x"));
        var after = DateTime.UtcNow.AddSeconds(1);
        Assert.NotNull(body);
        Assert.InRange(body!.Timestamp, before, after);
    }

    // ── environment-dependent detail leakage ──────────────────────────────

    [Fact]
    public async Task Server_Error_In_Production_Hides_Exception_Details()
    {
        var (_, body) = await RunAsync(
            new Exception("internal database connection string with secrets"),
            environmentName: "Production");
        Assert.NotNull(body);
        Assert.Null(body!.Details);
    }

    [Fact]
    public async Task Server_Error_In_Development_Leaks_Exception_Message()
    {
        var (_, body) = await RunAsync(
            new Exception("debuggable detail"),
            environmentName: "Development");
        Assert.NotNull(body);
        Assert.NotNull(body!.Details);
        Assert.Contains("debuggable detail", body.Details!.ToString());
    }

    [Fact]
    public async Task NonServer_Error_Includes_Details_Even_In_Production()
    {
        // 4xx errors are user-actionable — the original message is safe to surface.
        var (_, body) = await RunAsync(
            new ArgumentException("missing required field 'name'"),
            environmentName: "Production");
        Assert.NotNull(body);
        Assert.NotNull(body!.Details);
        Assert.Contains("missing required field 'name'", body.Details!.ToString());
    }
}
