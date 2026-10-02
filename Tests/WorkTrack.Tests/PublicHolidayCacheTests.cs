using System.Net;
using System.Text;
using Application.Holidays.Support;
using Application.Settings.Support;
using Domain;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// The working-day calendar is the PublicHolidays cache, and the reminder
/// scheduler used to read it without ever filling it — only a page asking for
/// holidays did. A host nobody had browsed therefore read Cyprus Independence
/// Day (1 October 2026) as a working Thursday and sent every reminder on it.
/// </summary>
public class PublicHolidayCacheTests
{
    private const string CyprusOctober = """
        [
          { "date": "2026-10-01", "localName": "Ημέρα της Κυπριακής Ανεξαρτησίας", "name": "Cyprus Independence Day", "countryCode": "CY" },
          { "date": "2026-10-28", "localName": "Επέτειος του Όχι", "name": "Ohi Day", "countryCode": "CY" }
        ]
        """;

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static NagerHolidayClient Client(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://date.nager.at/api/v3/") });

    [Fact]
    public async Task An_empty_cache_is_filled_so_the_holiday_is_not_a_working_day()
    {
        await using var context = TestDb.Create();
        var settings = new AppSettings { HolidayCountryCode = "CY", WorkingDays = "mon-fri" };
        var holiday = new DateOnly(2026, 10, 1);
        Assert.True(await WorkingWeek.IsWorkingDayAsync(context, settings, holiday, CancellationToken.None));

        var handler = new StubHandler(HttpStatusCode.OK, CyprusOctober);
        Assert.True(await PublicHolidayCache.EnsureYearAsync(context, Client(handler), "cy", 2026, CancellationToken.None));

        Assert.False(await WorkingWeek.IsWorkingDayAsync(context, settings, holiday, CancellationToken.None));
        Assert.True(await WorkingWeek.IsWorkingDayAsync(context, settings, new DateOnly(2026, 10, 2), CancellationToken.None));
    }

    [Fact]
    public async Task A_cached_year_is_not_fetched_again()
    {
        await using var context = TestDb.Create();
        var handler = new StubHandler(HttpStatusCode.OK, CyprusOctober);

        await PublicHolidayCache.EnsureYearAsync(context, Client(handler), "CY", 2026, CancellationToken.None);
        await PublicHolidayCache.EnsureYearAsync(context, Client(handler), "CY", 2026, CancellationToken.None);

        Assert.Equal(1, handler.Calls);
        Assert.Equal(2, context.PublicHolidays.Count());
    }

    [Fact]
    public async Task A_provider_answering_nothing_reports_the_year_as_not_cached()
    {
        await using var context = TestDb.Create();
        var handler = new StubHandler(HttpStatusCode.NotFound, "");

        Assert.False(await PublicHolidayCache.EnsureYearAsync(context, Client(handler), "CY", 2026, CancellationToken.None));
        Assert.Empty(context.PublicHolidays);
    }
}
