using System.Net;
using Application.Attendance.Support;
using Application.Holidays.Support;
using Application.Settings.Support;
using Domain;
using Domain.Interfaces;
using Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Persistence;

namespace Application.Reminders;

// Builds and sends the content for a single reminder type. Pure dispatch — the
// scheduling (which reminders are due, dedup) lives in the API hosted service
// (ReminderBackgroundService); this class only knows how to produce one
// reminder's recipients + message when asked.
//
// Currently implements the reminders backed by real data:
//   • pending-approvals — managers with leave/timesheets awaiting review in their department (never System Administrators)
//   • late-submissions  — employees sitting on an un-submitted (Draft) timesheet
//   • low-balance       — employees whose remaining leave is below the threshold
//   • birthday-reminder — admins/managers told of upcoming employee birthdays
//   • check-in          — employees who have not yet checked in today (never System Administrators)
//   • check-out         — employees still checked in (no check-out) today (never System Administrators)
//   • daily-attendance-report — HR Administrators told, each working morning and
//                         over their departments alone, who was late, absent,
//                         still checked in, over hours, behind on a timesheet or
//                         on leave the previous working day (never System
//                         Administrators: they run the system, not attendance,
//                         and are told about system errors instead —
//                         SystemErrorNotifier)
// Other ids are accepted but logged as not-implemented rather than failing.
//
// Nothing is sent on a non-working day — a weekend under the Working Week, or a
// public holiday for the holiday country — whichever reminder it is and however
// it was asked for. The scheduler already refuses to dispatch on such a day;
// DispatchAsync asks again because the on-demand run-reminder endpoint bypasses
// the schedule, and the question used to be repeated only by check-in,
// check-out and the daily report, so a manual run of the pending-approvals or
// late-submissions reminder still went out on a bank holiday.
public class ReminderDispatcher(
    AppDbContext context,
    IEmailService emailService,
    ILogger<ReminderDispatcher> logger,
    NagerHolidayClient? holidayClient = null)
{
    // Matches the client copy ("fewer than 5 days remaining").
    private const int LowBalanceThreshold = 5;

    // How far ahead the birthday reminder looks (inclusive of today).
    private const int BirthdayLookaheadDays = 7;

    public const string PendingApprovals = "pending-approvals";
    public const string LateSubmissions = "late-submissions";
    public const string LowBalance = "low-balance";
    public const string BirthdayReminder = "birthday-reminder";
    public const string CheckInReminder = "check-in";
    public const string CheckOutReminder = "check-out";
    public const string DailyAttendanceReport = "daily-attendance-report";

    // Convenience overload (used by the on-demand test endpoint): loads settings,
    // and fills this year's public holidays first — the scheduler does that on
    // every tick, but this path never passes through it, and an empty cache
    // reads a bank holiday as an ordinary weekday (PublicHolidayCache).
    public async Task<bool> DispatchAsync(string reminderId, CancellationToken cancellationToken)
    {
        var settings = await context.AppSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken)
                       ?? new AppSettings();
        await EnsureHolidaysCachedAsync(settings, cancellationToken);
        return await DispatchAsync(reminderId, settings, cancellationToken);
    }

    /// <summary>
    /// Sends one reminder. Returns false, sending nothing, when today is not a
    /// working day for the org (weekend or public holiday) or the id has no
    /// dispatcher.
    /// </summary>
    public async Task<bool> DispatchAsync(string reminderId, AppSettings settings, CancellationToken cancellationToken)
    {
        if (!await IsWorkingDayTodayAsync(settings, cancellationToken))
        {
            logger.LogInformation("{Id}: today ({Today}) is not a working day (weekend or public holiday); nothing sent.",
                reminderId, TodayLocal(settings));
            return false;
        }

        switch (reminderId)
        {
            case PendingApprovals:
                await PendingApprovalsAsync(settings, cancellationToken);
                break;
            case LateSubmissions:
                await LateSubmissionsAsync(settings, cancellationToken);
                break;
            case LowBalance:
                await LowBalanceAsync(settings, cancellationToken);
                break;
            case BirthdayReminder:
                await BirthdayRemindersAsync(settings, cancellationToken);
                break;
            case CheckInReminder:
                await CheckInReminderAsync(settings, cancellationToken);
                break;
            case CheckOutReminder:
                await CheckOutReminderAsync(settings, cancellationToken);
                break;
            case DailyAttendanceReport:
                await DailyAttendanceReportAsync(settings, cancellationToken);
                break;
            default:
                logger.LogInformation("Reminder '{Id}' has no dispatcher implementation; skipping.", reminderId);
                return false;
        }
        return true;
    }

    private async Task EnsureHolidaysCachedAsync(AppSettings settings, CancellationToken ct)
    {
        var code = settings.HolidayCountryCode;
        if (holidayClient is null || string.IsNullOrWhiteSpace(code)) return;
        try
        {
            await PublicHolidayCache.EnsureYearAsync(context, holidayClient, code, TodayLocal(settings).Year, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not load public holidays for {Country}; going by the cached calendar.", code);
        }
    }

    // ── pending-approvals ────────────────────────────────────────────────────
    private async Task PendingApprovalsAsync(AppSettings settings, CancellationToken ct)
    {
        // Managers only ever decide the manager stage, so their digest counts
        // Pending alone — a row sitting with HR is no longer theirs to action.
        var pendingLeave = await context.AnnualLeaves
            .Where(l => l.Status == AnnualLeaveStatus.Pending)
            .Select(l => l.DepartmentId)
            .ToListAsync(ct);

        var awaitingHrLeave = await context.AnnualLeaves
            .Where(l => l.Status == AnnualLeaveStatus.AwaitingHrApproval)
            .Select(l => l.DepartmentId)
            .ToListAsync(ct);

        var pendingTimesheets = await context.Timesheets
            .Where(t => t.Status == TimesheetStatus.Submitted || t.Status == TimesheetStatus.Resubmitted)
            .Select(t => (int?)t.DepartmentId)
            .ToListAsync(ct);

        var totalLeave = pendingLeave.Count;
        var totalTimesheets = pendingTimesheets.Count;

        if (totalLeave == 0 && totalTimesheets == 0 && awaitingHrLeave.Count == 0)
        {
            logger.LogInformation("pending-approvals: nothing awaiting review; no notifications sent.");
            return;
        }

        var managers = await GetManagersWithDepartmentAsync(ct);

        var sent = 0;
        if (settings.EmailNotificationsEnabled)
        {
            // Managers only, each seeing their own department's queue. System Administrators used
            // to get an organisation-wide copy as well, but the people who action
            // a submission are the managers, so that copy was a daily email about
            // queues that were not the System Administrator's to clear. A System Administrator holds no
            // department (the validators refuse one), so the join below cannot
            // pick one up as a manager either.
            foreach (var mgr in managers)
            {
                var deptLeave = pendingLeave.Count(d => d == mgr.DepartmentId);
                var deptTimesheets = pendingTimesheets.Count(d => d == mgr.DepartmentId);
                if (deptLeave == 0 && deptTimesheets == 0) continue;
                if (await SendPendingSummaryAsync(mgr.Email, mgr.DisplayName, deptLeave, deptTimesheets, "your department", ct))
                    sent++;
            }

            // HR Administrators get their own digest of what is awaiting HR in
            // their assigned departments, plus every department-less row (an
            // administrator's own, or a legacy row nobody else can reach).
            if (awaitingHrLeave.Count > 0)
            {
                var hrAdmins = await GetActiveHrAdministratorsAsync(ct);
                foreach (var hr in hrAdmins)
                {
                    var hrLeave = awaitingHrLeave.Count(d => d is null || hr.DepartmentIds.Contains(d.Value));
                    if (hrLeave == 0) continue;
                    if (await SendPendingSummaryAsync(hr.Email, hr.DisplayName, hrLeave, 0, "your departments", ct))
                        sent++;
                }
            }
        }
        else
        {
            logger.LogInformation("pending-approvals: email notifications disabled; skipping emails.");
        }


        logger.LogInformation("pending-approvals: dispatched. Emails sent: {Sent}.", sent);
    }

    /// <summary>
    /// Every active HR Administrator with an email, and the departments assigned
    /// to them — unlike <see cref="GetHrAdministratorsAsync"/>, one with no rows is
    /// still included, since a department-less leave reaches every HR
    /// Administrator regardless of their assigned departments.
    /// </summary>
    private async Task<List<ScopedContact>> GetActiveHrAdministratorsAsync(CancellationToken ct)
    {
        var hrRoleId = await context.Roles.Where(r => r.Name == AppRoles.HrAdministrator).Select(r => r.Id).FirstOrDefaultAsync(ct);
        if (hrRoleId is null) return [];

        var hr = await (
            from ur in context.UserRoles
            where ur.RoleId == hrRoleId
            join u in context.Users on ur.UserId equals u.Id
            where u.IsActive && u.Email != null && u.Email != ""
            select new { u.Id, u.Email, u.DisplayName }
        ).Distinct().ToListAsync(ct);
        if (hr.Count == 0) return [];

        var ids = hr.Select(h => h.Id).ToList();
        var rows = await context.UserDepartments
            .Where(ud => ids.Contains(ud.UserId))
            .Select(ud => new { ud.UserId, ud.DepartmentId })
            .ToListAsync(ct);

        return hr
            .Select(h => new ScopedContact(h.Id, h.Email!, h.DisplayName,
                rows.Where(r => r.UserId == h.Id).Select(r => r.DepartmentId).Distinct().ToList()))
            .ToList();
    }

    private Task<bool> SendPendingSummaryAsync(string email, string? name, int leaveCount, int timesheetCount, string scope, CancellationToken ct)
    {
        var greeting = WebUtility.HtmlEncode(name ?? email);
        var html = $"""
<p>Hello {greeting},</p>
<p>You have items awaiting your review across {WebUtility.HtmlEncode(scope)}:</p>
<ul>
  <li><strong>{leaveCount}</strong> leave request(s) pending approval</li>
  <li><strong>{timesheetCount}</strong> timesheet(s) submitted for review</li>
</ul>
<p>Please log in to Jenus People to review and action them.</p>
""";
        var text = $"Hello {name ?? email},\n\nAwaiting your review across {scope}:\n- {leaveCount} leave request(s) pending approval\n- {timesheetCount} timesheet(s) submitted for review\n\nPlease log in to Jenus People to review them.";
        return SendEmailAsync(email, "Jenus People: items awaiting your approval", html, text, ct);
    }

    // ── late-submissions ─────────────────────────────────────────────────────
    private async Task LateSubmissionsAsync(AppSettings settings, CancellationToken ct)
    {
        var drafts = await context.Timesheets
            .Where(t => t.Status == TimesheetStatus.Draft && t.Employee != null && t.Employee.User != null
                        && t.Employee.User.Email != null && t.Employee.User.Email != "")
            .Select(t => new
            {
                Email = t.Employee!.User!.Email!,
                Name = t.Employee.User.DisplayName,
                t.PeriodStart,
                t.PeriodEnd,
            })
            .ToListAsync(ct);

        if (drafts.Count == 0)
        {
            logger.LogInformation("late-submissions: no draft timesheets; no notifications sent.");
            return;
        }

        var byEmployee = drafts
            .GroupBy(d => d.Email)
            .Select(g => new { Email = g.Key, g.First().Name, Periods = g.Select(x => $"{x.PeriodStart:dd MMM yyyy} – {x.PeriodEnd:dd MMM yyyy}").ToList() })
            .ToList();

        var sent = 0;
        if (settings.EmailNotificationsEnabled)
        {
            foreach (var emp in byEmployee)
            {
                var greeting = WebUtility.HtmlEncode(emp.Name ?? emp.Email);
                var items = string.Join("", emp.Periods.Select(p => $"<li>{WebUtility.HtmlEncode(p)}</li>"));
                var html = $"""
<p>Hello {greeting},</p>
<p>The following timesheet(s) are still in <strong>draft</strong> and have not been submitted:</p>
<ul>{items}</ul>
<p>Please log in to Jenus People and submit them for approval.</p>
""";
                var text = $"Hello {emp.Name ?? emp.Email},\n\nThese timesheet(s) are still in draft and not submitted:\n{string.Join("\n", emp.Periods.Select(p => "- " + p))}\n\nPlease log in to Jenus People and submit them.";
                if (await SendEmailAsync(emp.Email, "Jenus People: timesheet not yet submitted", html, text, ct))
                    sent++;
            }
        }
        else
        {
            logger.LogInformation("late-submissions: email notifications disabled; skipping emails.");
        }


        logger.LogInformation("late-submissions: dispatched. Emails sent: {Sent}.", sent);
    }

    // ── low-balance ──────────────────────────────────────────────────────────
    private async Task LowBalanceAsync(AppSettings settings, CancellationToken ct)
    {
        var low = await context.EmployeeProfiles
            .Where(p => p.AnnualLeaveEntitlement > 0 && p.LeaveBalance < LowBalanceThreshold
                        && p.User != null && p.User.Email != null && p.User.Email != "")
            .Select(p => new { Email = p.User!.Email!, Name = p.User.DisplayName, p.LeaveBalance })
            .ToListAsync(ct);

        if (low.Count == 0)
        {
            logger.LogInformation("low-balance: no employees below {Threshold} days; no notifications sent.", LowBalanceThreshold);
            return;
        }

        var sent = 0;
        if (settings.EmailNotificationsEnabled)
        {
            foreach (var emp in low)
            {
                var greeting = WebUtility.HtmlEncode(emp.Name ?? emp.Email);
                var html = $"""
<p>Hello {greeting},</p>
<p>Your remaining annual leave balance is <strong>{emp.LeaveBalance} day(s)</strong>, which is below {LowBalanceThreshold} days.</p>
<p>Plan any remaining time off soon, or speak to your manager if you have questions.</p>
""";
                var text = $"Hello {emp.Name ?? emp.Email},\n\nYour remaining annual leave balance is {emp.LeaveBalance} day(s), below {LowBalanceThreshold} days. Plan any remaining time off soon.";
                if (await SendEmailAsync(emp.Email, "Jenus People: your leave balance is running low", html, text, ct))
                    sent++;
            }
        }
        else
        {
            logger.LogInformation("low-balance: email notifications disabled; skipping emails.");
        }


        logger.LogInformation("low-balance: dispatched. Emails sent: {Sent}.", sent);
    }

    // ── birthday-reminder ────────────────────────────────────────────────────
    // Notifies System Administrators (whole org), HR Administrators (their departments) and managers (their department)
    // of employees whose birthday falls within the next BirthdayLookaheadDays.
    private async Task BirthdayRemindersAsync(AppSettings settings, CancellationToken ct)
    {
        var people = await context.EmployeeProfiles
            .Where(p => p.User != null && p.User.DateOfBirth != null)
            .Select(p => new { Name = p.User!.DisplayName, Dob = p.User.DateOfBirth!.Value, p.DepartmentId })
            .ToListAsync(ct);

        // "Upcoming" is measured from today on the org's clock, not the server's.
        var today = TodayLocal(settings);

        var upcoming = people
            .Select(p => new
            {
                p.Name,
                p.DepartmentId,
                Date = NextBirthday(p.Dob, today),
                TurningAge = NextBirthday(p.Dob, today).Year - p.Dob.Year,
            })
            .Where(p => p.Date <= today.AddDays(BirthdayLookaheadDays - 1))
            .OrderBy(p => p.Date)
            .ToList();

        if (upcoming.Count == 0)
        {
            logger.LogInformation("birthday-reminder: no birthdays in the next {Days} days; nothing sent.", BirthdayLookaheadDays);
            return;
        }

        var admins = await GetUsersInRolesAsync([AppRoles.SystemAdministrator], ct);
        var hrAdmins = await GetHrAdministratorsAsync(ct);
        var managers = await GetManagersWithDepartmentAsync(ct);

        string LineHtml(string name, DateOnly date, int age) =>
            $"<li><strong>{WebUtility.HtmlEncode(name)}</strong> — {date:dd MMM} (turns {age})</li>";
        string LineText(string name, DateOnly date, int age) =>
            $"- {name} — {date:dd MMM} (turns {age})";

        var sent = 0;
        if (settings.EmailNotificationsEnabled)
        {
            foreach (var admin in admins)
            {
                var html = $"<p>Hello {WebUtility.HtmlEncode(admin.DisplayName ?? admin.Email)},</p><p>Upcoming birthdays in the next {BirthdayLookaheadDays} days:</p><ul>{string.Join("", upcoming.Select(u => LineHtml(u.Name, u.Date, u.TurningAge)))}</ul>";
                var text = $"Hello {admin.DisplayName ?? admin.Email},\n\nUpcoming birthdays in the next {BirthdayLookaheadDays} days:\n{string.Join("\n", upcoming.Select(u => LineText(u.Name, u.Date, u.TurningAge)))}";
                if (await SendEmailAsync(admin.Email, "Jenus People: upcoming birthdays 🎂", html, text, ct)) sent++;
            }

            foreach (var hr in hrAdmins)
            {
                var scoped = upcoming.Where(u => u.DepartmentId != null && hr.DepartmentIds.Contains(u.DepartmentId.Value)).ToList();
                if (scoped.Count == 0) continue;
                var html = $"<p>Hello {WebUtility.HtmlEncode(hr.DisplayName ?? hr.Email)},</p><p>Upcoming birthdays in your departments:</p><ul>{string.Join("", scoped.Select(u => LineHtml(u.Name, u.Date, u.TurningAge)))}</ul>";
                var text = $"Hello {hr.DisplayName ?? hr.Email},\n\nUpcoming birthdays in your departments:\n{string.Join("\n", scoped.Select(u => LineText(u.Name, u.Date, u.TurningAge)))}";
                if (await SendEmailAsync(hr.Email, "Jenus People: upcoming birthdays 🎂", html, text, ct)) sent++;
            }

            foreach (var mgr in managers)
            {
                if (admins.Any(a => a.UserId == mgr.UserId) || hrAdmins.Any(h => h.UserId == mgr.UserId)) continue;
                var deptUpcoming = upcoming.Where(u => u.DepartmentId == mgr.DepartmentId).ToList();
                if (deptUpcoming.Count == 0) continue;
                var html = $"<p>Hello {WebUtility.HtmlEncode(mgr.DisplayName ?? mgr.Email)},</p><p>Upcoming birthdays in your department:</p><ul>{string.Join("", deptUpcoming.Select(u => LineHtml(u.Name, u.Date, u.TurningAge)))}</ul>";
                var text = $"Hello {mgr.DisplayName ?? mgr.Email},\n\nUpcoming birthdays in your department:\n{string.Join("\n", deptUpcoming.Select(u => LineText(u.Name, u.Date, u.TurningAge)))}";
                if (await SendEmailAsync(mgr.Email, "Jenus People: upcoming birthdays 🎂", html, text, ct)) sent++;
            }
        }
        else
        {
            logger.LogInformation("birthday-reminder: email notifications disabled; skipping emails.");
        }


        logger.LogInformation("birthday-reminder: dispatched. Emails sent: {Sent}.", sent);
    }

    // ── check-in ─────────────────────────────────────────────────────────────
    // Reminds each employee who has not yet checked in today (and isn't on
    // approved leave) to check in. Attendance is computed over the UTC calendar
    // day, matching how the attendance feature records and reports it.
    private async Task CheckInReminderAsync(AppSettings settings, CancellationToken ct)
    {
        var attendance = await LoadAttendanceTodayAsync(ct);
        var targets = attendance.Employees
            .Where(e => !attendance.OnLeaveUserIds.Contains(e.UserId) && !attendance.CheckedInProfileIds.Contains(e.ProfileId))
            .ToList();

        if (targets.Count == 0)
        {
            logger.LogInformation("check-in: everyone expected today has checked in (or is on leave); nothing sent.");
            return;
        }

        var sent = 0;
        if (settings.EmailNotificationsEnabled)
        {
            foreach (var emp in targets)
            {
                var greeting = WebUtility.HtmlEncode(emp.DisplayName ?? emp.Email);
                var html = $"""
<p>Hello {greeting},</p>
<p>This is a friendly reminder to <strong>check in</strong> for the day in Jenus People.</p>
<p>Open Jenus People and tap “Check in” so your attendance is recorded.</p>
""";
                var text = $"Hello {emp.DisplayName ?? emp.Email},\n\nThis is a friendly reminder to check in for the day in Jenus People. Open Jenus People and tap \"Check in\" so your attendance is recorded.";
                if (await SendEmailAsync(emp.Email, "Jenus People: don't forget to check in", html, text, ct))
                    sent++;
            }
        }
        else
        {
            logger.LogInformation("check-in: email notifications disabled; skipping emails.");
        }


        logger.LogInformation("check-in: dispatched. Emails sent: {Sent}.", sent);
    }

    // ── check-out ────────────────────────────────────────────────────────────
    // Reminds each employee who checked in today but hasn't checked out yet to
    // check out and complete their timesheet.
    private async Task CheckOutReminderAsync(AppSettings settings, CancellationToken ct)
    {
        var attendance = await LoadAttendanceTodayAsync(ct);
        var targets = attendance.Employees
            .Where(e => !attendance.OnLeaveUserIds.Contains(e.UserId)
                        && attendance.CheckedInProfileIds.Contains(e.ProfileId)
                        && !attendance.CheckedOutProfileIds.Contains(e.ProfileId))
            .ToList();

        if (targets.Count == 0)
        {
            logger.LogInformation("check-out: nobody is still checked in; nothing sent.");
            return;
        }

        var sent = 0;
        if (settings.EmailNotificationsEnabled)
        {
            foreach (var emp in targets)
            {
                var greeting = WebUtility.HtmlEncode(emp.DisplayName ?? emp.Email);
                var html = $"""
<p>Hello {greeting},</p>
<p>You're still <strong>checked in</strong> on Jenus People. Before you finish for the day, please <strong>check out</strong> and complete your timesheet.</p>
""";
                var text = $"Hello {emp.DisplayName ?? emp.Email},\n\nYou're still checked in on Jenus People. Before you finish for the day, please check out and complete your timesheet.";
                if (await SendEmailAsync(emp.Email, "Jenus People: remember to check out", html, text, ct))
                    sent++;
            }
        }
        else
        {
            logger.LogInformation("check-out: email notifications disabled; skipping emails.");
        }


        logger.LogInformation("check-out: dispatched. Emails sent: {Sent}.", sent);
    }

    // ── daily-attendance-report ──────────────────────────────────────────────
    // Every HR Administrator, each working morning, about the previous working
    // day in their departments: who checked in late, who never checked in, who
    // never checked out, who worked overtime, whose timesheet for the latest
    // week past its deadline is still unsubmitted, and who was on leave. Nothing
    // goes out on a non-working morning, and Monday's report covers Friday.
    // Administrators and deactivated accounts appear in none of the lists
    // (AttendanceDay.ExcludeAdmins; a leaver is not expected in), matching the
    // check-in reminders and the attendance dashboards.
    //
    // The System Administrator used to get a company-wide copy. They no longer
    // do: attendance is Leave & Time, which the HR Administrator runs, and a
    // System Administrator has no page to act on any of it. The one email the
    // role gets about the system is the error report (SystemErrorNotifier). An
    // HR Administrator with no departments assigned is likewise not a recipient
    // — an empty reach never means "everything" — so a workspace with nobody
    // assigned sends nothing rather than falling back to the System
    // Administrator.
    //
    // "Late" is the first check-in after WorkingHoursStart, compared in the
    // org's TimeZoneId — the first consumer that setting has had. Attendance
    // events are stored in UTC, so without the conversion 09:00 would mean 09:00
    // UTC, two or three hours into a Cypriot morning. "Overtime" is worked time
    // (AttendanceDayStateCalculator, so breaks are excluded) beyond the working
    // day WorkingHoursStart..WorkingHoursEnd describes, and is judged only on a
    // day that was checked out of — an open day would be measured to "now", the
    // next morning. The company dashboard's flat "over 10 hours" is a different
    // yardstick; this one follows the settings, as the late rule does.
    private async Task DailyAttendanceReportAsync(AppSettings settings, CancellationToken ct)
    {
        var today = TodayLocal(settings);
        var reportDay = await PreviousWorkingDayAsync(settings, today, ct);
        if (reportDay is null)
        {
            logger.LogInformation("daily-attendance-report: no working day found in the preceding two weeks; nothing sent.");
            return;
        }

        var hrAdmins = await GetHrAdministratorsAsync(ct);
        if (hrAdmins.Count == 0)
        {
            logger.LogInformation("daily-attendance-report: no HR Administrator with an email address and assigned departments; nothing sent.");
            return;
        }

        var sent = 0;
        if (settings.EmailNotificationsEnabled)
        {
            var subject = $"Jenus People: attendance report for {reportDay.Value:ddd dd MMM yyyy}";

            // One report per HR Administrator, over their departments alone.
            foreach (var hr in hrAdmins)
            {
                var scoped = await BuildDailyAttendanceReportAsync(settings, reportDay.Value, hr.DepartmentIds, ct);
                var html = RenderDailyReportHtml(hr.DisplayName ?? hr.Email, scoped);
                var text = RenderDailyReportText(hr.DisplayName ?? hr.Email, scoped);
                if (await SendEmailAsync(hr.Email, subject, html, text, ct)) sent++;
            }
        }
        else
        {
            logger.LogInformation("daily-attendance-report: email notifications disabled; skipping emails.");
        }

        logger.LogInformation("daily-attendance-report: dispatched for {Day}. Emails sent: {Sent}.", reportDay.Value, sent);
    }

    private sealed record DailyReport(
        DateOnly Day,
        string Coverage,
        DateOnly TimesheetWeekStart,
        List<string> Late,
        List<string> NotCheckedIn,
        List<string> NotCheckedOut,
        List<string> Overtime,
        List<string>? ShortDay,
        List<string>? BreakAllowance,
        List<string> TimesheetNotSubmitted,
        List<string> OnLeave);

    private async Task<DailyReport> BuildDailyAttendanceReportAsync(AppSettings settings, DateOnly day, IReadOnlyCollection<int> departmentIds, CancellationToken ct)
    {
        var ids = departmentIds.ToList();
        // Named in the email's opening line, so the reader knows whose absences
        // they are looking at — and, as importantly, whose they are not.
        var departmentNames = await context.Departments
            .Where(d => ids.Contains(d.Id))
            .OrderBy(d => d.Name)
            .Select(d => d.Name)
            .ToListAsync(ct);
        var coverage = departmentNames.Count == 0 ? "covering no departments" : "covering " + JoinNames(departmentNames);

        var people = await AttendanceDay.ExcludeAdmins(context.EmployeeProfiles)
            .Where(p => p.User != null && p.User.IsActive)
            .Where(p => p.DepartmentId != null && ids.Contains(p.DepartmentId.Value))
            .OrderBy(p => p.User!.DisplayName)
            .Select(p => new
            {
                ProfileId = p.Id,
                p.UserId,
                p.User!.DisplayName,
                p.User.Email,
                Department = p.Department != null ? p.Department.Name : null,
            })
            .ToListAsync(ct);

        var dayStart = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var dayEnd = dayStart.AddDays(1);
        var profileIds = people.Select(p => p.ProfileId).ToList();

        // Every event of the day, replayed through the same calculator the
        // attendance screens use, so a break is not billed as work.
        var events = await context.AttendanceEvents
            .AsNoTracking()
            .Where(e => profileIds.Contains(e.EmployeeProfileId) && e.At >= dayStart && e.At < dayEnd)
            .ToListAsync(ct);
        var nowUtc = DateTime.UtcNow;
        var stateByProfileId = events
            .GroupBy(e => e.EmployeeProfileId)
            .ToDictionary(g => g.Key, g => AttendanceDayStateCalculator.Calculate(g, nowUtc));

        // Matched on the profile, the key attendance is recorded against, as
        // DailyHoursContext does for the screens.
        var onLeave = (await context.AnnualLeaves
            .Where(l => l.Status == AnnualLeaveStatus.Approved
                        && l.StartDate < dayEnd && l.EndDate >= dayStart
                        && l.EmployeeProfileId != null
                        && profileIds.Contains(l.EmployeeProfileId))
            .Select(l => new { ProfileId = l.EmployeeProfileId!, LeaveType = l.LeaveType != null ? l.LeaveType.Name : null, l.Duration })
            .ToListAsync(ct))
            .GroupBy(l => l.ProfileId)
            .ToDictionary(g => g.Key, g => (Name: g.First().LeaveType, Covers: DailyHoursRule.CombineLeave(g.Select(x => x.Duration))));

        var weekStart = LatestTimesheetWeekPastDeadline(settings, DateTime.UtcNow);
        var weekStartUtc = weekStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var submittedProfileIds = (await context.Timesheets
            .Where(t => profileIds.Contains(t.EmployeeProfileId)
                        && t.PeriodStart >= weekStartUtc && t.PeriodStart < weekStartUtc.AddDays(1)
                        && (t.Status == TimesheetStatus.Submitted
                            || t.Status == TimesheetStatus.Approved
                            || t.Status == TimesheetStatus.Resubmitted))
            .Select(t => t.EmployeeProfileId)
            .ToListAsync(ct))
            .ToHashSet();

        // The same reading of "late" and of the working day's length as the
        // attendance dashboards, so the morning's email and the screen agree.
        var schedule = WorkingDaySchedule.From(settings);
        var scheduledMinutes = schedule.ScheduledMinutes;

        var late = new List<string>();
        var notIn = new List<string>();
        var notOut = new List<string>();
        var overtime = new List<string>();
        // Checked-out days under the scheduled day net of the break, beyond the
        // grace, with approved leave taken off the target (DailyHoursRule). Null,
        // and no section, when the settings describe no working day at all.
        var shortDay = scheduledMinutes > 0 ? new List<string>() : null;
        // The break taken against the break allowed, on checked-out days only
        // (WorkingDaySchedule.BreakVariance); null, and no section, when the
        // settings configure no break — "Nobody" under a heading nobody set
        // would read as a policy that is not there.
        var breakAllowance = schedule.BreakMinutes > 0 ? new List<string>() : null;
        var noTimesheet = new List<string>();
        var leave = new List<string>();

        foreach (var person in people)
        {
            var who = string.IsNullOrWhiteSpace(person.DisplayName) ? person.Email ?? person.UserId : person.DisplayName;
            var label = $"{who} ({person.Department ?? "No department"})";
            var isOnLeave = onLeave.TryGetValue(person.ProfileId, out var leaveInfo);

            if (isOnLeave)
                leave.Add($"{label} — {leaveInfo.Name ?? "Leave"}");

            if (stateByProfileId.TryGetValue(person.ProfileId, out var state) && state.CheckInAt is { } checkInAt)
            {
                if (schedule.IsLate(checkInAt))
                    late.Add($"{label} — checked in {schedule.LocalTimeOf(checkInAt):HH:mm}, {schedule.MinutesLate(checkInAt)} min late");

                if (state.CheckOutAt is null)
                {
                    notOut.Add(label);
                }
                else
                {
                    // The same grace as the dashboard's overtime line: staying on up to
                    // the grace past the scheduled day is not overtime.
                    if (state.WorkedMinutes > scheduledMinutes + DailyHoursRule.GraceMinutes)
                        overtime.Add($"{label} — {HoursAndMinutes(state.WorkedMinutes - scheduledMinutes)} over (worked {HoursAndMinutes(state.WorkedMinutes)})");

                    var verdict = DailyHoursRule.Judge(
                        isWorkingDay: true,
                        isOnLeave ? leaveInfo.Covers : LeaveOnDay.None,
                        scheduledMinutes,
                        state.WorkedMinutes,
                        dayClosed: true);
                    if (shortDay is not null && verdict.ShortByMinutes is { } shortBy)
                        shortDay.Add($"{label} — {HoursAndMinutes(shortBy)} short (worked {HoursAndMinutes(state.WorkedMinutes)} of {HoursAndMinutes(verdict.TargetMinutes!.Value)})");
                }

                if (breakAllowance is not null && schedule.BreakVariance(state, nowUtc) is { } variance && variance != 0)
                {
                    var taken = schedule.BreakMinutesTaken(state, nowUtc);
                    breakAllowance.Add(
                        $"{label} — {HoursAndMinutes(Math.Abs(variance))} {(variance > 0 ? "over" : "under")} (took {HoursAndMinutes(taken)} of {HoursAndMinutes(schedule.BreakMinutes)})");
                }
            }
            else if (!isOnLeave)
            {
                notIn.Add(label);
            }

            if (!submittedProfileIds.Contains(person.ProfileId))
                noTimesheet.Add(label);
        }

        return new DailyReport(day, coverage, weekStart, late, notIn, notOut, overtime, shortDay, breakAllowance, noTimesheet, leave);
    }

    /// <summary>"Engineering", "Engineering and Finance", "Engineering, Finance and Sales".</summary>
    private static string JoinNames(IReadOnlyList<string> names) => names.Count switch
    {
        1 => names[0],
        2 => $"{names[0]} and {names[1]}",
        _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
    };

    private static string HoursAndMinutes(int minutes) => $"{minutes / 60}h {minutes % 60:00}m";

    // Monday of the most recent timesheet week whose submission deadline
    // (TimesheetSubmissionDeadlineDay at TimesheetSubmissionDeadlineTime, UTC)
    // has already passed. Midweek that is last week; once Friday 18:00 is gone
    // it is this week — so the list is empty until a timesheet is actually due.
    private static DateOnly LatestTimesheetWeekPastDeadline(AppSettings settings, DateTime nowUtc)
    {
        var today = DateOnly.FromDateTime(nowUtc);
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7)); // Monday
        var deadlineOffset = Array.IndexOf(WeekTokensMondayFirst, (settings.TimesheetSubmissionDeadlineDay ?? "fri").Trim().ToLowerInvariant());
        if (deadlineOffset < 0) deadlineOffset = 4; // Friday
        var deadlineTime = TimeOnly.TryParse(settings.TimesheetSubmissionDeadlineTime, out var t) ? t : new TimeOnly(18, 0);

        while (weekStart.AddDays(deadlineOffset).ToDateTime(deadlineTime, DateTimeKind.Utc) > nowUtc)
            weekStart = weekStart.AddDays(-7);

        return weekStart;
    }

    private static readonly string[] WeekTokensMondayFirst = { "mon", "tue", "wed", "thu", "fri", "sat", "sun" };

    private static string RenderDailyReportHtml(string greetingName, DailyReport r)
    {
        static string Section(string heading, List<string> items, string? note = null) =>
            $"<h3>{heading}</h3>"
            + (note is null ? "" : $"<p>{WebUtility.HtmlEncode(note)}</p>")
            + (items.Count == 0
                ? "<p>Nobody</p>"
                : "<ul>" + string.Join("", items.Select(i => $"<li>{WebUtility.HtmlEncode(i)}</li>")) + "</ul>");

        return $"""
<p>Hello {WebUtility.HtmlEncode(greetingName)},</p>
<p>Attendance report for <strong>{r.Day:dddd dd MMMM yyyy}</strong>, {WebUtility.HtmlEncode(r.Coverage)}.</p>
{Section("Late check-ins", r.Late)}
{Section("Did not check in", r.NotCheckedIn)}
{Section("Did not check out", r.NotCheckedOut)}
{Section("Overtime", r.Overtime)}
{(r.ShortDay is null ? "" : Section("Short day", r.ShortDay))}
{(r.BreakAllowance is null ? "" : Section("Break allowance", r.BreakAllowance))}
{Section("Timesheet not submitted", r.TimesheetNotSubmitted, $"Week of {r.TimesheetWeekStart:dd MMM yyyy}")}
{Section("On leave", r.OnLeave)}
""";
    }

    private static string RenderDailyReportText(string greetingName, DailyReport r)
    {
        static string Section(string heading, List<string> items) =>
            $"{heading}:\n" + (items.Count == 0 ? "- Nobody" : string.Join("\n", items.Select(i => "- " + i)));

        return string.Join("\n\n", new[]
        {
            $"Hello {greetingName},",
            $"Attendance report for {r.Day:dddd dd MMMM yyyy}, {r.Coverage}.",
            Section("Late check-ins", r.Late),
            Section("Did not check in", r.NotCheckedIn),
            Section("Did not check out", r.NotCheckedOut),
            Section("Overtime", r.Overtime),
            r.ShortDay is null ? null : Section("Short day", r.ShortDay),
            r.BreakAllowance is null ? null : Section("Break allowance", r.BreakAllowance),
            Section($"Timesheet not submitted (week of {r.TimesheetWeekStart:dd MMM yyyy})", r.TimesheetNotSubmitted),
            Section("On leave", r.OnLeave),
        }.Where(section => section is not null));
    }

    // The most recent working day strictly before 'today', looking back at most
    // two weeks so a mis-configured calendar cannot loop forever.
    private async Task<DateOnly?> PreviousWorkingDayAsync(AppSettings settings, DateOnly today, CancellationToken ct)
    {
        for (var back = 1; back <= 14; back++)
        {
            var candidate = today.AddDays(-back);
            if (await IsWorkingDayAsync(settings, candidate, ct)) return candidate;
        }
        return null;
    }

    // Shared attendance snapshot for the check-in/check-out reminders: every
    // employee with a usable email, plus the sets of who has checked in / out
    // today (keyed by EmployeeProfile.Id, as attendance events are) and who is
    // on approved leave today (keyed by user id, as AnnualLeave.EmployeeId is).
    //
    // System Administrators are dropped the same way the attendance dashboards drop them
    // (AttendanceDay.ExcludeAdmins): a System Administrator may hold a profile, but the topbar
    // hides the check-in widget for the role, so a reminder to check in is one
    // they cannot act on and would receive every working morning.
    private async Task<AttendanceSnapshot> LoadAttendanceTodayAsync(CancellationToken ct)
    {
        var employees = await AttendanceDay.ExcludeAdmins(context.EmployeeProfiles)
            .Where(p => p.User != null && p.User.Email != null && p.User.Email != "")
            .Select(p => new EmployeeContact(p.Id, p.UserId, p.User!.Email!, p.User.DisplayName))
            .ToListAsync(ct);

        if (employees.Count == 0)
            return new AttendanceSnapshot(employees, new(), new(), new());

        var now = DateTime.UtcNow;
        var dayStart = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
        var dayEnd = dayStart.AddDays(1);

        var profileIds = employees.Select(e => e.ProfileId).ToList();
        var todayEvents = await context.AttendanceEvents
            .Where(e => profileIds.Contains(e.EmployeeProfileId) && e.At >= dayStart && e.At < dayEnd
                        && (e.Type == AttendanceEventType.CheckIn || e.Type == AttendanceEventType.CheckOut))
            .Select(e => new { e.EmployeeProfileId, e.Type })
            .ToListAsync(ct);

        var checkedIn = todayEvents.Where(e => e.Type == AttendanceEventType.CheckIn).Select(e => e.EmployeeProfileId).ToHashSet();
        var checkedOut = todayEvents.Where(e => e.Type == AttendanceEventType.CheckOut).Select(e => e.EmployeeProfileId).ToHashSet();

        var userIds = employees.Select(e => e.UserId).ToList();
        var onLeave = (await context.AnnualLeaves
            .Where(l => l.Status == AnnualLeaveStatus.Approved
                        && l.StartDate <= now && l.EndDate >= now
                        && userIds.Contains(l.EmployeeId))
            .Select(l => l.EmployeeId)
            .ToListAsync(ct)).ToHashSet();

        return new AttendanceSnapshot(employees, checkedIn, checkedOut, onLeave);
    }

    // Today's date on the org's clock (AppSettings.TimeZoneId) — the date a public
    // holiday is a date on. The scheduler (ReminderBackgroundService) already
    // refuses to dispatch on a non-working day; DispatchAsync repeats the
    // question because the on-demand run-reminder endpoint bypasses the schedule.
    private static DateOnly TodayLocal(AppSettings settings) => WorkingWeek.TodayLocal(settings, DateTime.UtcNow);

    // True when today is a working day for the org: not a weekend per the
    // configured WorkingDays, and not a public holiday for the configured
    // holiday country. One rule, shared with the scheduler (WorkingWeek). The
    // attendance snapshot it guards still reads the UTC calendar day, which is
    // how attendance events are recorded and reported everywhere.
    private Task<bool> IsWorkingDayTodayAsync(AppSettings settings, CancellationToken ct) =>
        IsWorkingDayAsync(settings, TodayLocal(settings), ct);

    private Task<bool> IsWorkingDayAsync(AppSettings settings, DateOnly day, CancellationToken ct) =>
        WorkingWeek.IsWorkingDayAsync(context, settings, day, ct);

    private record EmployeeContact(string ProfileId, string UserId, string Email, string? DisplayName);

    private record AttendanceSnapshot(
        List<EmployeeContact> Employees,
        HashSet<string> CheckedInProfileIds,
        HashSet<string> CheckedOutProfileIds,
        HashSet<string> OnLeaveUserIds);

    // Next occurrence of a birthday on/after 'from', clamping Feb 29 to Feb 28 in
    // non-leap years.
    private static DateOnly NextBirthday(DateOnly dob, DateOnly from)
    {
        var candidate = ClampToMonth(from.Year, dob.Month, dob.Day);
        if (candidate < from) candidate = ClampToMonth(from.Year + 1, dob.Month, dob.Day);
        return candidate;
    }

    private static DateOnly ClampToMonth(int year, int month, int day) =>
        new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));

    // ── shared helpers ───────────────────────────────────────────────────────
    private async Task<bool> SendEmailAsync(string email, string subject, string html, string text, CancellationToken ct)
    {
        try
        {
            return await emailService.SendEmailAsync(email, subject, html, text, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Reminder email to {Email} failed.", email);
            return false;
        }
    }

    private record UserContact(string UserId, string Email, string? DisplayName);
    private record ManagerContact(string UserId, string Email, string? DisplayName, int DepartmentId);
    private record ScopedContact(string UserId, string Email, string? DisplayName, List<int> DepartmentIds);

    /// <summary>Every HR Administrator with an email, and the departments assigned to them. One with none is not mailed a digest: there is nobody in it to tell them about.</summary>
    private async Task<List<ScopedContact>> GetHrAdministratorsAsync(CancellationToken ct)
    {
        var hr = await GetUsersInRolesAsync([AppRoles.HrAdministrator], ct);
        if (hr.Count == 0) return [];
        var ids = hr.Select(h => h.UserId).ToList();
        var rows = await context.UserDepartments
            .Where(ud => ids.Contains(ud.UserId))
            .Select(ud => new { ud.UserId, ud.DepartmentId })
            .ToListAsync(ct);
        return hr
            .Select(h => new ScopedContact(h.UserId, h.Email, h.DisplayName,
                rows.Where(r => r.UserId == h.UserId).Select(r => r.DepartmentId).Distinct().ToList()))
            .Where(h => h.DepartmentIds.Count > 0)
            .ToList();
    }

    private async Task<List<UserContact>> GetUsersInRolesAsync(IReadOnlyList<string> roleNames, CancellationToken ct)
    {
        var roleIds = await context.Roles.Where(r => roleNames.Contains(r.Name!)).Select(r => r.Id).ToListAsync(ct);
        if (roleIds.Count == 0) return [];

        return await (
            from ur in context.UserRoles
            where roleIds.Contains(ur.RoleId)
            join u in context.Users on ur.UserId equals u.Id
            where u.Email != null && u.Email != ""
            select new UserContact(u.Id, u.Email!, u.DisplayName)
        ).Distinct().ToListAsync(ct);
    }

    private async Task<List<ManagerContact>> GetManagersWithDepartmentAsync(CancellationToken ct)
    {
        var roleId = await context.Roles.Where(r => r.Name == AppRoles.Manager).Select(r => r.Id).FirstOrDefaultAsync(ct);
        if (roleId is null) return [];

        return await (
            from ep in context.EmployeeProfiles
            join ur in context.UserRoles on ep.UserId equals ur.UserId
            where ur.RoleId == roleId
            join u in context.Users on ep.UserId equals u.Id
            where u.Email != null && u.Email != ""
            // Every digest below is "what is pending in your department", so a
            // manager without one has nothing to be told about.
            where ep.DepartmentId != null
            select new ManagerContact(u.Id, u.Email!, u.DisplayName, ep.DepartmentId!.Value)
        ).Distinct().ToListAsync(ct);
    }
}
