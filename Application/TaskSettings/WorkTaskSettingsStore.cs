using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.TaskSettings;

/// <summary>
/// Reads the Task Settings row. A database without one (the in-memory test provider,
/// which never runs HasData) reads as the defaults — today's behaviour — rather than
/// failing every task write.
/// </summary>
public static class WorkTaskSettingsStore
{
    public static async Task<WorkTaskSettings> LoadAsync(AppDbContext context, CancellationToken cancellationToken) =>
        await context.WorkTaskSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken) ?? new WorkTaskSettings();
}
