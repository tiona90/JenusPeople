using Application.Reminders;
using Application.Settings.Support;
using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace API.BackgroundServices;

// Drives the reminder schedule. Wakes once a minute, reads the persisted
// reminder settings, and for each enabled reminder whose configured time has
// passed today (and which hasn't run yet today) invokes the dispatcher.
//
// Design choices (confirmed with the product owner):
//   • Server local time — the stored TimeZoneId is a display-only string, so we
//     fire when the SERVER clock reaches the reminder's HH:mm.
//   • Weekly reminders fire on Monday only.
//   • Dedup is in-memory (a last-fired calendar date per reminder id). A restart
//     can re-send once if it happens within the same day after the fire time;
//     acceptable for this use case and avoids a DB migration.
public class ReminderBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<ReminderBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    // reminderId -> last calendar date (server local) it was dispatched.
    private readonly Dictionary<string, DateOnly> _lastRun = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ReminderBackgroundService started (tick interval: {Interval}).", TickInterval);
        using var timer = new PeriodicTimer(TickInterval);

        // Run once immediately so a reminder whose time already passed today is
        // caught up shortly after startup, then on every tick.
        do
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Reminder tick failed.");
            }
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var now = DateTime.Now; // server local time, by design
        var today = DateOnly.FromDateTime(now);
        var nowTime = TimeOnly.FromDateTime(now);

        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var settings = await context.AppSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new AppSettings();
        var reminders = ReminderSerializer.FromJson(settings.RemindersJson);

        ReminderDispatcher? dispatcher = null;

        foreach (var r in reminders)
        {
            if (!r.Enabled) continue;
            if (!TimeOnly.TryParse(r.Time, out var scheduled)) continue;
            if (r.Frequency == "weekly" && now.DayOfWeek != DayOfWeek.Monday) continue;
            if (nowTime < scheduled) continue; // not time yet today
            if (_lastRun.TryGetValue(r.Id, out var last) && last == today) continue; // already ran today

            _lastRun[r.Id] = today;
            logger.LogInformation("Reminder '{Id}' is due (scheduled {Time}, {Freq}); dispatching.", r.Id, r.Time, r.Frequency);

            dispatcher ??= scope.ServiceProvider.GetRequiredService<ReminderDispatcher>();
            await dispatcher.DispatchAsync(r.Id, settings, ct);
        }
    }
}
