using System.Net.Http.Headers;
using API.Middleware;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Every response carries the id its log lines were written under, so a report ("I
/// got an error, the id was abc") reaches the right lines without grepping by
/// timestamp. The id is <c>HttpContext.TraceIdentifier</c>, which is also what
/// <c>ApiErrorResponse.TraceId</c> puts in an error body — one id, three places.
///
/// A caller may supply the header to join a chain of calls under one id, which means
/// it is attacker-controlled text on its way into a log file. So it is accepted only
/// when it is short and url-safe; a value with a newline in it could otherwise forge
/// log lines.
/// </summary>
[Collection(ApiRouteTableCollection.Name)]
public class CorrelationIdTests(ApiRouteTableFixture routeTable)
{
    /// <summary>
    /// Liveness: it runs no checks, so this exercises the middleware without touching
    /// a database or a mail provider.
    /// </summary>
    private const string Probe = "/health";

    private async Task<string?> CorrelationIdFor(string? supplied)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Probe);
        if (supplied is not null)
        {
            // TryAddWithoutValidation: some of these are deliberately not legal
            // header values, and the point is what the server does with them.
            request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, supplied);
        }

        using var response = await routeTable.Client.SendAsync(request);

        return response.Headers.TryGetValues(CorrelationIdMiddleware.HeaderName, out var values)
            ? string.Join(",", values)
            : null;
    }

    [Fact]
    public async Task A_response_always_carries_a_correlation_id()
    {
        var id = await CorrelationIdFor(supplied: null);

        Assert.False(string.IsNullOrWhiteSpace(id));
    }

    [Fact]
    public async Task Two_requests_get_different_ids()
    {
        var first = await CorrelationIdFor(supplied: null);
        var second = await CorrelationIdFor(supplied: null);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task A_caller_supplied_id_is_echoed_so_a_chain_of_calls_shares_one()
    {
        var id = await CorrelationIdFor("client-request-42");

        Assert.Equal("client-request-42", id);
    }

    /// <summary>
    /// Anything that is not plain url-safe text is replaced rather than logged. The
    /// newline case is the one that matters: it would let a caller write its own
    /// lines into the log file.
    /// </summary>
    [Theory]
    [InlineData("spaces are out")]
    [InlineData("semi;colon")]
    [InlineData("forged\nline")]
    [InlineData("<script>")]
    public async Task An_unsafe_supplied_id_is_replaced(string supplied)
    {
        var id = await CorrelationIdFor(supplied);

        Assert.False(string.IsNullOrWhiteSpace(id));
        Assert.NotEqual(supplied, id);
    }

    /// <summary>
    /// A bounded length, so nobody puts a megabyte in every log line.
    /// </summary>
    [Fact]
    public async Task An_overlong_supplied_id_is_replaced()
    {
        var supplied = new string('a', 200);

        var id = await CorrelationIdFor(supplied);

        Assert.NotEqual(supplied, id);
        Assert.True(id!.Length <= 64, $"Generated id was {id.Length} characters.");
    }
}
