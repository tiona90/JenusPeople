using Application.Holidays.Support;
using Application.Settings.Support;
using Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Persistence;

namespace Application.Reminders;

/// <summary>What one tick of the schedule left for the host to deal with.</summary>
public sealed class ReminderTickOutcome
{
    /// <summary>Reminders whose dispatch threw, for the host to report (<c>SystemErrorNotifier</c>).</summary>
    public List<(string ReminderId, Exception Exception)> Failures { get; } = [];

    /// <summary>The holiday provider could not be reached or had nothing; the host backs off before asking again.</summary>
    public bool HolidayCalendarUnavailable { get; set; }
}

/// <summary>
/// One tick of the reminder schedule: reads the reminders on Notification
/// Settings, asks <see cref="ReminderSchedule"/> which are due right now on the
/// org's clock and calendar, dispatches those, and records each in
/// <see cref="AppDbContext.ReminderRuns"/>. The hosted service
/// (<c>ReminderBackgroundService</c>) only owns the timer, the holiday-fetch
/// back-off and the error reporting, so this — the part with the rules in it —
/// runs against a real <see cref="AppDbContext"/> in tests.
///
/// The record is the point. "Already sent today" lived in a dictionary on the
/// hosted service, so every restart of the API re-sent whatever was due before
/// it: on the IIS host that is each deploy, each app-pool recycle and each time
/// the idle site is woken by its first visitor. A <see cref="ReminderRun"/> row
/// is written <em>before</em> the dispatch, so even a process that dies
/// mid-send does not repeat it when it comes back.
/// </summary>
public class ReminderRunner(
    AppDbContext context,
    ReminderDispatcher dispatcher,
    ILogger<ReminderRunner> logger,
    NagerHolidayClient? holidayClient = null)
{
    /// <param name="utcNow">The instant to evaluate the schedule at.</param>
    /// <param name="refreshHolidays">
    /// Whether to make sure this year's public holidays are cached before reading
    /// the calendar; the host passes false while backing off from a failed fetch.
    /// </param>
    public async Task<ReminderTickOutcome> TickAsync(DateTime utcNow, bool refreshHolidays, CancellationToken ct)
    {
        var outcome = new ReminderTickOutcome();

        var settings = await context.AppSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new AppSettings();
        var reminders = ReminderSerializer.FromJson(settings.RemindersJson);
        if (reminders.All(r => !r.Enabled)) return outcome;

        var localNow = WorkingWeek.LocalNow(settings, utcNow);
        var today = DateOnly.FromDateTime(localNow);
        var nowTime = TimeOnly.FromDateTime(localNow);

        // The weekly question looks back to this week's Monday, which may sit in
        // last year.
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        if (refreshHolidays)
            outcome.HolidayCalendarUnavailable = !await EnsureHolidaysCachedAsync(settings, [monday.Year, today.Year], ct);

        // One calendar lookup per tick, shared by every reminder; the weekly
        // question is only asked when a weekly reminder is switched on.
        var workingDay = await WorkingWeek.IsWorkingDayAsync(context, settings, today, ct);
        DateOnly? firstWorkingDayOfWeek = workingDay && reminders.Any(r => r.Enabled && r.Frequency == ReminderSchedule.Weekly)
            ? await WorkingWeek.FirstWorkingDayOfWeekAsync(context, settings, today, ct)
            : null;

        var runs = await context.ReminderRuns.ToDictionaryAsync(r => r.ReminderId, ct);

        foreach (var r in reminders)
        {
            runs.TryGetValue(r.Id, out var run);
            var cutoff = ReminderSchedule.CatchUpCutoff(r.Id, settings);
            var state = ReminderSchedule.Evaluate(r, nowTime, today, workingDay, firstWorkingDayOfWeek, run?.RanOn, cutoff);

            switch (state)
            {
                case ReminderDueState.Due:
                    // Recorded first: a crash inside the dispatch must not re-send on restart.
                    run = await RecordAsync(run, r.Id, today, utcNow, "dispatching", ct);
                    runs[r.Id] = run;
                    logger.LogInformation("Reminder '{Id}' is due (scheduled {Time} {Zone}, {Freq}); dispatching.",
                        r.Id, r.Time, settings.TimeZoneId, r.Frequency);
                    try
                    {
                        var sent = await dispatcher.DispatchAsync(r.Id, settings, ct);
                        await TryRecordAsync(run, sent ? "sent" : "nothing to send", ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Reminder '{Id}' failed.", r.Id);
                        await TryRecordAsync(run, $"failed: {ex.GetType().Name}", ct);
                        outcome.Failures.Add((r.Id, ex));
                    }
                    break;

                case ReminderDueState.TooLate:
                    // Recorded so the day reads as handled and this is logged once, not every minute.
                    runs[r.Id] = await RecordAsync(run, r.Id, today, utcNow, $"skipped: missed its {r.Time} slot, working day over at {cutoff:HH\\:mm}", ct);
                    logger.LogInformation("Reminder '{Id}' not sent: it missed its {Time} slot and the working day ended at {Cutoff} ({Zone}).",
                        r.Id, r.Time, cutoff, settings.TimeZoneId);
                    break;

                case ReminderDueState.NotWorkingDay:
                case ReminderDueState.NotFirstWorkingDayOfWeek:
                    logger.LogDebug("Reminder '{Id}' not sent: {State} ({Today}, {Zone}).", r.Id, state, today, settings.TimeZoneId);
                    break;
            }
        }

        return outcome;
    }

    private async Task<ReminderRun> RecordAsync(ReminderRun? run, string reminderId, DateOnly today, DateTime utcNow, string outcome, CancellationToken ct)
    {
        if (run is null)
        {
            run = new ReminderRun { ReminderId = reminderId };
            context.ReminderRuns.Add(run);
        }
        run.RanOn = today;
        run.RanAtUtc = utcNow;
        run.Outcome = Truncate(outcome);
        await context.SaveChangesAsync(ct);
        return run;
    }

    // The outcome is bookkeeping; a database that cannot take it must not turn a
    // sent reminder into a reported failure, or hide the failure that was.
    private async Task TryRecordAsync(ReminderRun run, string outcome, CancellationToken ct)
    {
        try
        {
            run.Outcome = Truncate(outcome);
            await context.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not record the outcome of reminder '{Id}'.", run.ReminderId);
        }
    }

    private static string Truncate(string s) =>
        s.Length <= ReminderRun.OutcomeMaxLength ? s : s[..ReminderRun.OutcomeMaxLength];

    // The calendar is the PublicHolidays cache, which only a page asking for
    // holidays used to fill, and saving a new holiday country empties it — so a
    // host nobody had browsed read a bank holiday as a working day and reminded
    // everybody. Returns false when the provider had nothing or could not be
    // reached; the tick then goes ahead on what is cached.
    private async Task<bool> EnsureHolidaysCachedAsync(AppSettings settings, int[] years, CancellationToken ct)
    {
        var code = settings.HolidayCountryCode?.Trim().ToUpperInvariant();
        if (holidayClient is null || string.IsNullOrEmpty(code)) return true;

        foreach (var year in years.Distinct())
        {
            try
            {
                if (await PublicHolidayCache.EnsureYearAsync(context, holidayClient, code, year, ct)) continue;
                logger.LogWarning("No {Year} public holidays returned for {Country}; reminders go by the cached calendar until the next attempt.", year, code);
                return false;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex,
                    "Could not load {Year} public holidays for {Country}; reminders go by the cached calendar until the next attempt.",
                    year, code);
                return false;
            }
        }
        return true;
    }
}
