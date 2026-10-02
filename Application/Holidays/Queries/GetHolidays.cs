using Application.Core;
using Application.Holidays.DTOs;
using Application.Holidays.Support;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Holidays.Queries;

public class GetHolidays
{
    public class Query : IRequest<Result<IReadOnlyList<HolidayDto>>>
    {
        public int Year { get; set; }
    }

    public class Handler(AppDbContext context, NagerHolidayClient client)
        : IRequestHandler<Query, Result<IReadOnlyList<HolidayDto>>>
    {
        public async Task<Result<IReadOnlyList<HolidayDto>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var settings = await context.AppSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
            var code = settings?.HolidayCountryCode?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(code))
                return Result<IReadOnlyList<HolidayDto>>.Success([]);

            try
            {
                await PublicHolidayCache.EnsureYearAsync(context, client, code, request.Year, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                return Result<IReadOnlyList<HolidayDto>>.Failure($"Could not load public holidays: {ex.Message}");
            }

            var holidays = await context.PublicHolidays
                .AsNoTracking()
                .Where(h => h.CountryCode == code && h.Year == request.Year)
                .OrderBy(h => h.Date)
                .ToListAsync(cancellationToken);

            return Result<IReadOnlyList<HolidayDto>>.Success(holidays.Select(ToDto).ToList());
        }

        private static HolidayDto ToDto(PublicHoliday h) => new()
        {
            Date = h.Date,
            LocalName = h.LocalName,
            EnglishName = h.EnglishName,
            CountryCode = h.CountryCode,
        };
    }
}
