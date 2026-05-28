using Application.Core;
using Application.Holidays.DTOs;
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

            var cached = await context.PublicHolidays
                .AsNoTracking()
                .Where(h => h.CountryCode == code && h.Year == request.Year)
                .OrderBy(h => h.Date)
                .ToListAsync(cancellationToken);

            if (cached.Count > 0)
            {
                return Result<IReadOnlyList<HolidayDto>>.Success(cached.Select(ToDto).ToList());
            }

            try
            {
                var fetched = await client.GetPublicHolidaysAsync(request.Year, code, cancellationToken);
                if (fetched.Count == 0)
                    return Result<IReadOnlyList<HolidayDto>>.Success([]);

                var existingDates = await context.PublicHolidays
                    .Where(h => h.CountryCode == code)
                    .Select(h => h.Date)
                    .ToListAsync(cancellationToken);
                var existingSet = existingDates.Select(d => d.Date).ToHashSet();

                var entities = fetched
                    .GroupBy(h => h.Date.Date)
                    .Where(g => !existingSet.Contains(g.Key))
                    .Select(g => g.First())
                    .Select(h => new PublicHoliday
                    {
                        CountryCode = code,
                        Year = request.Year,
                        Date = h.Date.Date,
                        LocalName = h.LocalName,
                        EnglishName = h.EnglishName,
                        CachedAt = DateTime.UtcNow,
                    }).ToList();

                if (entities.Count > 0)
                {
                    context.PublicHolidays.AddRange(entities);
                    await context.SaveChangesAsync(cancellationToken);
                }

                var fresh = await context.PublicHolidays
                    .AsNoTracking()
                    .Where(h => h.CountryCode == code && h.Year == request.Year)
                    .OrderBy(h => h.Date)
                    .ToListAsync(cancellationToken);

                return Result<IReadOnlyList<HolidayDto>>.Success(fresh.Select(ToDto).ToList());
            }
            catch (HttpRequestException ex)
            {
                return Result<IReadOnlyList<HolidayDto>>.Failure($"Could not load public holidays: {ex.Message}");
            }
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
