using Application.Reminders;
using Application.SystemErrors;

namespace API.BackgroundServices;

// Drives the reminder schedule: wakes once a minute and hands the tick to
// ReminderRunner, which reads the persisted reminder settings, works out which
// reminders are due (ReminderSchedule) and dispatches them.
//
// What lives here, and only here:
//   • The timer. Everything with a rule in it is in ReminderRunner, so it can be
//     tested against a real DbContext without a hosted service.
//   • The holiday-fetch back-off. The runner fills the year's public holidays
//     from date.nager.at before reading the calendar; when the provider cannot be
//     reached the tick goes ahead on what is cached and a fetch is retried after
//     HolidayRetryInterval rather than every minute.
//   • Error reporting. A reminder that throws is reported to the System
//     Administrators (SystemErrorNotifier) from a fresh scope — the DbContext
//     the dispatcher was using may be the thing that broke.
//
// Dedup is the ReminderRuns table, not memory: an API restart — a deploy, an
// app-pool recycle, IIS waking the idle site for its first visitor — used to
// re-send every reminder whose time had passed that day.
public class ReminderBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<ReminderBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan HolidayRetryInterval = TimeSpan.FromMinutes(15);

    // When a failed holiday fetch may next be retried.
    private DateTime _holidayRetryAfterUtc = DateTime.MinValue;

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
        ReminderTickOutcome outcome;
        using (var scope = scopeFactory.CreateScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<ReminderRunner>();
            var utcNow = DateTime.UtcNow;
            outcome = await runner.TickAsync(utcNow, refreshHolidays: utcNow >= _holidayRetryAfterUtc, ct);
            if (outcome.HolidayCalendarUnavailable) _holidayRetryAfterUtc = utcNow + HolidayRetryInterval;
        }

        foreach (var (id, ex) in outcome.Failures)
            await ReportAsync(id, ex, ct);
    }

    // Its own scope: the DbContext the dispatcher was using may be the thing
    // that broke, and the report must not depend on it.
    private async Task ReportAsync(string reminderId, Exception ex, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var notifier = scope.ServiceProvider.GetRequiredService<SystemErrorNotifier>();
        await notifier.NotifyAsync(new SystemErrorReport($"reminder '{reminderId}'", ex), ct);
    }
}
