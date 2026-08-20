using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Holidays.DTOs;

namespace Application.Holidays.Support;

public class NagerHolidayClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<IReadOnlyList<CountryDto>> GetAvailableCountriesAsync(CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync("AvailableCountries", cancellationToken);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var list = await JsonSerializer.DeserializeAsync<List<NagerCountry>>(stream, JsonOptions, cancellationToken);
        return list?.Select(c => new CountryDto { CountryCode = c.CountryCode, Name = c.Name }).ToList() ?? [];
    }

    public async Task<IReadOnlyList<HolidayDto>> GetPublicHolidaysAsync(int year, string countryCode, CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync($"PublicHolidays/{year}/{countryCode}", cancellationToken);
        if (!response.IsSuccessStatusCode) return [];

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var list = await JsonSerializer.DeserializeAsync<List<NagerHoliday>>(stream, JsonOptions, cancellationToken);
        return list?.Select(h => new HolidayDto
        {
            Date = DateTime.SpecifyKind(h.Date, DateTimeKind.Utc),
            LocalName = h.LocalName ?? string.Empty,
            EnglishName = h.Name ?? string.Empty,
            CountryCode = h.CountryCode ?? countryCode,
        }).ToList() ?? [];
    }

    private record NagerCountry(string CountryCode, string Name);
    private record NagerHoliday(DateTime Date, string? LocalName, string? Name, string? CountryCode);
}
