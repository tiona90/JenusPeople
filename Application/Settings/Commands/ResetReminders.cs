using Application.Core;
using Application.Settings.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Settings.Commands;

// Danger-zone action: restore every reminder to its factory default.
public class ResetReminders
{
    public class Command : IRequest<Result<AppSettingsDto>> { }

    public class Handler(AppDbContext context) : IRequestHandler<Command, Result<AppSettingsDto>>
    {
        public async Task<Result<AppSettingsDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var settings = await context.AppSettings.FirstOrDefaultAsync(cancellationToken);
            if (settings is null)
            {
                settings = new Domain.AppSettings();
                context.AppSettings.Add(settings);
            }

            // Empty JSON => GetAppSettings/mapper return the default catalogue.
            settings.RemindersJson = string.Empty;
            await context.SaveChangesAsync(cancellationToken);

            return Result<AppSettingsDto>.Success(AppSettingsMapper.ToDto(settings));
        }
    }
}
