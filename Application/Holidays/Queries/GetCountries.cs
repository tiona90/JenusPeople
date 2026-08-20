using Application.Core;
using Application.Holidays.DTOs;
using Application.Holidays.Support;
using MediatR;

namespace Application.Holidays.Queries;

public class GetCountries
{
    public class Query : IRequest<Result<IReadOnlyList<CountryDto>>> { }

    public class Handler(NagerHolidayClient client) : IRequestHandler<Query, Result<IReadOnlyList<CountryDto>>>
    {
        public async Task<Result<IReadOnlyList<CountryDto>>> Handle(Query request, CancellationToken cancellationToken)
        {
            try
            {
                var countries = await client.GetAvailableCountriesAsync(cancellationToken);
                return Result<IReadOnlyList<CountryDto>>.Success(countries);
            }
            catch (HttpRequestException ex)
            {
                return Result<IReadOnlyList<CountryDto>>.Failure($"Could not load country list: {ex.Message}");
            }
        }
    }
}
