using Application.Holidays.Support;
using Application.Reminders;
using Application.Settings.Support;
using Application.SystemErrors;
using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace API.BackgroundServices;

// Drives the reminder schedule. Wakes once a minute, reads the persisted
// reminder settings, and for each enabled reminder that is due (ReminderSchedule)
// and hasn't run yet today invokes the dispatcher.
//
// Design choices:
//   • The org's clock, not the server's. A reminder's HH:mm is read in
//     AppSettings.TimeZoneId (WorkingWeek.LocalNow), the same zone the attendance
//     rules judge lateness in. It used to be DateTime.Now, which is right only
//     while the server happens to sit in the org's zone — true of the developer
//     box (GTB Standard Time) and of nothing that can be relied on.
//   • Working days only, for every reminder. The Working Week on Organization
//     settings (weekday preset plus public holidays) decides whether anything
//     goes out today; a Saturday or a bank holiday sends nothing. Weekly
//     reminders fire on the first working day of the week, so a Monday holiday
//     moves them to Tuesday rather than skipping the week (they used to fire on
//     Monday, holiday or not, and never at all for a week with no Monday in it).
//   • The holidays are fetched here, not assumed. The calendar is the
//     PublicHolidays cache, which only a page asking for holidays used to fill,
//     and saving a new holiday country empties it — so a host nobody had browsed
//     read a bank holiday (Cyprus Independence Day, 1 October 2026) as a
//     working day and reminded everybody. Each tick makes sure the year's
//     holidays are cached first (PublicHolidayCache — one indexed lookup once
//     they are, since the table can be emptied under it at any time). If the provider cannot be reached
//     the tick goes ahead on what is cached, as before, and a fetch is retried
//     after HolidayRetryInterval rather than every minute.
//   • Dedup is in-memory (a last-fired org-local date per reminder id). A restart
//     can re-send once if it happens within the same day after the fire time;
//     acceptable for this use case and avoids a DB migration.
//   • A reminder that throws is logged, reported to the System Administrators
//     (SystemErrorNotifier) and does not stop the reminders after it in the same
//     tick. It is still marked as run for the day: retrying a broken reminder
//     every minute would send the same error email and, worse, could send half
//     a digest sixty times.
public class ReminderBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<ReminderBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan HolidayRetryInterval = TimeSpan.FromMinutes(15);

    // reminderId -> last calendar date (org local) it was dispatched.
    private readonly Dictionary<string, DateOnly> _lastRun = new();

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
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var settings = await context.AppSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new AppSettings();
        var reminders = ReminderSerializer.FromJson(settings.RemindersJson);
        if (reminders.All(r => !r.Enabled)) return;

        var localNow = WorkingWeek.LocalNow(settings, DateTime.UtcNow);
        var today = DateOnly.FromDateTime(localNow);
        var nowTime = TimeOnly.FromDateTime(localNow);

        // The weekly question looks back to this week's Monday, which may sit in
        // last year.
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        await EnsureHolidaysCachedAsync(scope, context, settings, [monday.Year, today.Year], ct);

        // One calendar lookup per tick, shared by every reminder; the weekly
        // question is only asked when a weekly reminder is switched on.
        var workingDay = await WorkingWeek.IsWorkingDayAsync(context, settings, today, ct);
        DateOnly? firstWorkingDayOfWeek = workingDay && reminders.Any(r => r.Enabled && r.Frequency == ReminderSchedule.Weekly)
            ? await WorkingWeek.FirstWorkingDayOfWeekAsync(context, settings, today, ct)
            : null;

        ReminderDispatcher? dispatcher = null;

        foreach (var r in reminders)
        {
            DateOnly? lastRun = _lastRun.TryGetValue(r.Id, out var last) ? last : null;
            var state = ReminderSchedule.Evaluate(r, nowTime, today, workingDay, firstWorkingDayOfWeek, lastRun);
            if (state != ReminderDueState.Due)
            {
                if (state is ReminderDueState.NotWorkingDay or ReminderDueState.NotFirstWorkingDayOfWeek)
                    logger.LogDebug("Reminder '{Id}' not sent: {State} ({Today}, {Zone}).", r.Id, state, today, settings.TimeZoneId);
                continue;
            }

            _lastRun[r.Id] = today;
            logger.LogInformation("Reminder '{Id}' is due (scheduled {Time} {Zone}, {Freq}); dispatching.", r.Id, r.Time, settings.TimeZoneId, r.Frequency);

            dispatcher ??= scope.ServiceProvider.GetRequiredService<ReminderDispatcher>();
            try
            {
                await dispatcher.DispatchAsync(r.Id, settings, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Reminder '{Id}' failed.", r.Id);
                await ReportAsync(r.Id, ex, ct);
            }
        }
    }

    private async Task EnsureHolidaysCachedAsync(
        IServiceScope scope, AppDbContext context, AppSettings settings, int[] years, CancellationToken ct)
    {
        var code = settings.HolidayCountryCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code)) return;

        if (DateTime.UtcNow < _holidayRetryAfterUtc) return;

        var client = scope.ServiceProvider.GetRequiredService<NagerHolidayClient>();
        foreach (var year in years.Distinct())
        {
            try
            {
                if (await PublicHolidayCache.EnsureYearAsync(context, client, code, year, ct)) continue;
                _holidayRetryAfterUtc = DateTime.UtcNow + HolidayRetryInterval;
                logger.LogWarning("No {Year} public holidays returned for {Country}; retrying in {Interval}.", year, code, HolidayRetryInterval);
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                _holidayRetryAfterUtc = DateTime.UtcNow + HolidayRetryInterval;
                logger.LogWarning(ex,
                    "Could not load {Year} public holidays for {Country}; reminders go by the cached calendar until the next attempt.",
                    year, code);
                return;
            }
        }
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
