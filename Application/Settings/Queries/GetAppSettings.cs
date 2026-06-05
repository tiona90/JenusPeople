using Application.Settings.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Settings.Queries;

public class GetAppSettings
{
    public class Query : IRequest<AppSettingsDto> { }

    public class Handler(AppDbContext context) : IRequestHandler<Query, AppSettingsDto>
    {
        public async Task<AppSettingsDto> Handle(Query request, CancellationToken cancellationToken)
        {
            var s = await context.AppSettings.FirstOrDefaultAsync(cancellationToken);
            // No row yet → return entity defaults (and the default reminder catalogue).
            return AppSettingsMapper.ToDto(s ?? new Domain.AppSettings());
        }
    }
}
