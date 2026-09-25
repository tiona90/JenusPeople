using Application.Attendance.DTOs;
using Application.Attendance.Support;
using Application.Core;
using Domain;
using Domain.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Attendance.Queries;

/// <summary>
/// The admin company dashboard: headline counts, a per-department breakdown, a
/// recent-activity feed and a set of flagged issues, all for today.
/// </summary>
public class GetCompanyAttendance
{
    /// <summary>
    /// Thresholds the dashboard judgements rest on. "Late" itself is not one of
    /// them any more: it comes from <see cref="WorkingDaySchedule"/>, the org's
    /// working-hours start in its own time zone. It used to be a check-in at or
    /// after 10:00 UTC against a nominal 09:00 UTC start, which on a UTC+3
    /// deployment flagged nothing before one in the afternoon.
    /// </summary>
    private const int OvertimeMinutes = 600;
    /// <summary>
    /// How long past the start an empty morning is left alone before it is
    /// reported as an absence. Someone can be late; a whole hour with nothing is
    /// a different kind of news.
    /// </summary>
    private const int NotCheckedInGraceMinutes = 60;
    // High enough that a day's feed is effectively complete, because Company
    // Attendance filters it client-side: a cap of 20 would let a department or
    // action filter report "no activity" while the day holds plenty. The
    // dashboard's card shows the first 8 regardless, so this costs it nothing.
    private const int RecentActivityLimit = 200;
    private const int NotCheckedInListLimit = 5;
    private const int LateNamesShown = 3;
    private const int OverBreakNamesShown = 3;

    public class Query : IRequest<Result<CompanyAttendanceDto>>
    {
        /// <summary>
        /// The instant to judge the day at. A test seam: the controller leaves it
        /// null and the handler reads the clock, but the issues and the activity
        /// feed depend on the time of day and could not be asserted on otherwise.
        /// </summary>
        public DateTime? NowUtc { get; init; }

        public string RequestingUserId { get; init; } = string.Empty;

        /// <summary>
        /// True for an HR Administrator: only the departments assigned to them. False
        /// — the default, the System Administrator's, and every existing test's — is
        /// the whole company.
        /// </summary>
        public bool ScopeToCaller { get; init; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Query, Result<CompanyAttendanceDto>>
    {
        public async Task<Result<CompanyAttendanceDto>> Handle(Query request, CancellationToken cancellationToken)
        {
            var now = request.NowUtc ?? DateTime.UtcNow;
            var schedule = await WorkingDaySchedule.LoadAsync(context, cancellationToken);

            var profilesQuery = AttendanceDay.ExcludeAdmins(
                context.EmployeeProfiles
                    .Include(p => p.User)
                    .Include(p => p.Department));

            if (request.ScopeToCaller)
            {
                var scope = await ManagerAccessScopeResolver.ResolveAsync(context, request.RequestingUserId, cancellationToken);
                profilesQuery = profilesQuery.Where(p =>
                    p.DepartmentId != null && scope.ManagedDepartmentIds.Contains(p.DepartmentId.Value));
            }

            var profiles = await profilesQuery.ToListAsync(cancellationToken);

            var profileIds = profiles.Select(p => p.Id).ToList();

            var todayByEmployee = await AttendanceDay.LoadDayEventsByEmployeeAsync(
                context, profileIds, now, cancellationToken);
            var onLeave = await AttendanceDay.LoadOnLeaveProfileIdsAsync(
                context, profileIds, now, cancellationToken);

            // One state per employee, computed once and shared by every section
            // below. The version this replaces recomputed it five separate times.
            var stateByProfileId = profiles.ToDictionary(
                p => p.Id,
                p => AttendanceDay.StateFor(todayByEmployee, p.Id, now));

            var departments = BuildDepartments(profiles, stateByProfileId, onLeave, out var totals);

            var workedPeopleAll = profiles.Count(p => stateByProfileId[p.Id].WorkedMinutes > 0);
            var avgMinutesAll = workedPeopleAll > 0 ? totals.Minutes / workedPeopleAll : 0;

            var recent = await BuildRecentActivityAsync(
                profiles, profileIds, todayByEmployee, onLeave, schedule, now, cancellationToken);

            var issues = BuildIssues(profiles, stateByProfileId, onLeave, departments, totals, schedule, now);

            return Result<CompanyAttendanceDto>.Success(new CompanyAttendanceDto(
                totals.Total,
                totals.In,
                totals.Break,
                totals.Out,
                totals.Leave,
                totals.Minutes,
                avgMinutesAll,
                departments,
                recent,
                issues));
        }

        private sealed record Totals(int Total, int In, int Break, int Out, int Leave, int Minutes);

        private static List<DeptAttendanceDto> BuildDepartments(
            List<EmployeeProfile> profiles,
            Dictionary<string, AttendanceDayState> stateByProfileId,
            HashSet<string> onLeave,
            out Totals totals)
        {
            var departments = new List<DeptAttendanceDto>();
            int total = 0, inCount = 0, breakCount = 0, outCount = 0, leaveCount = 0, minutesAll = 0;

            var groups = profiles
                .GroupBy(p => p.Department?.Name ?? "Unassigned")
                .OrderBy(g => g.Key, StringComparer.Ordinal);

            foreach (var group in groups)
            {
                int dIn = 0, dBreak = 0, dOut = 0, dLeave = 0, dMinutes = 0, dWorkedPeople = 0;

                foreach (var profile in group)
                {
                    // Leave is counted instead of attendance, not as well as it.
                    if (onLeave.Contains(profile.Id))
                    {
                        dLeave++;
                        continue;
                    }

                    var state = stateByProfileId[profile.Id];
                    switch (state.Status)
                    {
                        case AttendanceDayStatus.In: dIn++; break;
                        case AttendanceDayStatus.Break: dBreak++; break;
                        default: dOut++; break;
                    }

                    if (state.WorkedMinutes > 0)
                    {
                        dMinutes += state.WorkedMinutes;
                        dWorkedPeople++;
                    }
                }

                departments.Add(new DeptAttendanceDto(
                    group.Key,
                    group.Count(),
                    dIn,
                    dBreak,
                    dOut,
                    dLeave,
                    dMinutes,
                    dWorkedPeople > 0 ? dMinutes / dWorkedPeople : 0));

                total += group.Count();
                inCount += dIn;
                breakCount += dBreak;
                outCount += dOut;
                leaveCount += dLeave;
                minutesAll += dMinutes;
            }

            totals = new Totals(total, inCount, breakCount, outCount, leaveCount, minutesAll);
            return departments;
        }

        private async Task<List<RecentActivityDto>> BuildRecentActivityAsync(
            List<EmployeeProfile> profiles,
            List<string> profileIds,
            Dictionary<string, List<AttendanceEvent>> todayByEmployee,
            HashSet<string> onLeave,
            WorkingDaySchedule schedule,
            DateTime now,
            CancellationToken cancellationToken)
        {
            var todayStart = AttendanceDay.UtcDayStart(now);
            var todayEnd = todayStart.AddDays(1);

            var recentEvents = await context.AttendanceEvents
                .Where(e => profileIds.Contains(e.EmployeeProfileId) && e.At >= todayStart && e.At < todayEnd)
                .OrderByDescending(e => e.At)
                .Take(RecentActivityLimit)
                .ToListAsync(cancellationToken);

            var profileById = profiles.ToDictionary(p => p.Id);

            var feed = recentEvents.Select(e =>
            {
                profileById.TryGetValue(e.EmployeeProfileId, out var profile);
                var minutesAgo = (int)Math.Max(0, (now - e.At).TotalMinutes);

                return new RecentActivityDto(
                    profile is null ? "Unknown" : AttendanceDay.DisplayNameOf(profile),
                    profile?.Department?.Name ?? "Unassigned",
                    ActionName(e.Type, AttendanceDay.AsUtc(e.At), schedule),
                    AttendanceDay.AsUtc(e.At),
                    minutesAgo,
                    BreakOverAt(e, todayByEmployee, schedule));
            }).ToList();

            // Synthetic "Not checked in" rows, added only once the morning is late
            // enough for an absence to mean anything. They carry no timestamp,
            // which is the only way a consumer can tell them from real events.
            if (schedule.IsPastStart(now, NotCheckedInGraceMinutes))
            {
                var notChecked = profiles
                    .Where(p => !onLeave.Contains(p.Id) && !todayByEmployee.ContainsKey(p.Id))
                    .Take(NotCheckedInListLimit)
                    .Select(p => new RecentActivityDto(
                        AttendanceDay.DisplayNameOf(p),
                        p.Department?.Name ?? "Unassigned",
                        "Not checked in",
                        null,
                        null));

                feed = [.. feed, .. notChecked];
            }

            return feed;
        }

        /// <summary>
        /// Minutes over the break allowance the day's break stood at, as of a
        /// break's end — the way a check-in row is judged late. Judged at the event
        /// rather than at now, so a later break that takes the day over marks its
        /// own row and not the earlier one. Null on any other event, and null when
        /// within the allowance or no break is configured (BreakVariance's own
        /// reading): the feed, like the issues card, flags what needs attention.
        /// </summary>
        private static int? BreakOverAt(
            AttendanceEvent e,
            Dictionary<string, List<AttendanceEvent>> todayByEmployee,
            WorkingDaySchedule schedule)
        {
            if (e.Type is not (AttendanceEventType.BreakEnd or AttendanceEventType.AutoBreakEnd)) return null;
            if (!todayByEmployee.TryGetValue(e.EmployeeProfileId, out var dayEvents)) return null;

            var at = AttendanceDay.AsUtc(e.At);
            var stateAt = AttendanceDayStateCalculator.Calculate(dayEvents.Where(x => x.At <= e.At), at);
            return schedule.BreakVariance(stateAt, at) is { } variance && variance > 0 ? variance : null;
        }

        private static string ActionName(AttendanceEventType type, DateTime atUtc, WorkingDaySchedule schedule) => type switch
        {
            AttendanceEventType.CheckIn => schedule.IsLate(atUtc) ? "Late check-in" : "Checked in",
            AttendanceEventType.CheckOut => "Checked out",
            AttendanceEventType.BreakStart => "Started break",
            AttendanceEventType.AutoBreakStart => "Went idle",
            AttendanceEventType.AutoBreakEnd => "Back from idle",
            _ => "Back from break",
        };

        private static List<IssueDto> BuildIssues(
            List<EmployeeProfile> profiles,
            Dictionary<string, AttendanceDayState> stateByProfileId,
            HashSet<string> onLeave,
            List<DeptAttendanceDto> departments,
            Totals totals,
            WorkingDaySchedule schedule,
            DateTime now)
        {
            var issues = new List<IssueDto>();

            // 1) Departments with people who have not checked in, once the local
            //    clock is an hour past the start.
            if (schedule.IsPastStart(now, NotCheckedInGraceMinutes))
            {
                foreach (var dept in departments.Where(d => d.Out > 0))
                {
                    issues.Add(new IssueDto(
                        "danger",
                        $"{dept.Out} not checked in ({dept.Name})",
                        $"No check-in by {schedule.StartPlus(NotCheckedInGraceMinutes)} · likely unscheduled absence"));
                }
            }

            // 2) Late check-ins, reported as minutes past the configured start.
            var lateNames = new List<string>();
            foreach (var profile in profiles)
            {
                if (onLeave.Contains(profile.Id)) continue;

                var state = stateByProfileId[profile.Id];
                if (state.CheckInAt is not { } checkInAt) continue;
                if (!schedule.IsLate(checkInAt)) continue;

                var lateMinutes = schedule.MinutesLate(checkInAt);
                var department = profile.Department?.Name ?? "Unassigned";
                lateNames.Add($"{AttendanceDay.DisplayNameOf(profile)} ({department}) · {lateMinutes} min late");
            }

            if (lateNames.Count > 0)
            {
                issues.Add(new IssueDto(
                    "warning",
                    $"{lateNames.Count} late check-in{(lateNames.Count == 1 ? "" : "s")}",
                    string.Join(" · ", lateNames.Take(LateNamesShown))));
            }

            // 3) Over the break allowance, reported as minutes over the configured
            //    break (WorkingDaySchedule.BreakVariance), a running break included.
            //    Under is not an issue: the panel flags what needs attention, and
            //    the team board is where a short break shows.
            var overBreak = new List<string>();
            foreach (var profile in profiles)
            {
                if (onLeave.Contains(profile.Id)) continue;

                var state = stateByProfileId[profile.Id];
                if (schedule.BreakVariance(state, now) is not { } variance || variance <= 0) continue;

                var department = profile.Department?.Name ?? "Unassigned";
                overBreak.Add($"{AttendanceDay.DisplayNameOf(profile)} ({department}) · {variance} min over");
            }

            if (overBreak.Count > 0)
            {
                issues.Add(new IssueDto(
                    "warning",
                    $"{overBreak.Count} over break allowance",
                    string.Join(" · ", overBreak.Take(OverBreakNamesShown))));
            }

            // 4) On-leave summary, broken down by department.
            if (totals.Leave > 0)
            {
                var profileById = profiles.ToDictionary(p => p.Id);
                var breakdown = onLeave
                    .Select(id => profileById.TryGetValue(id, out var p)
                        ? p.Department?.Name ?? "Unassigned"
                        : "Unassigned")
                    .GroupBy(name => name)
                    .Select(g => $"{g.Count()} {g.Key}")
                    .ToList();

                issues.Add(new IssueDto(
                    "info",
                    $"{totals.Leave} on approved leave",
                    string.Join(" · ", breakdown)));
            }

            // 5) Overtime. Always reported, so the panel says something reassuring
            // when nothing is wrong rather than going blank.
            var overtime = profiles.Count(p => stateByProfileId[p.Id].WorkedMinutes > OvertimeMinutes);
            issues.Add(overtime == 0
                ? new IssueDto("success", "No unusual overtime", "All employees within healthy hour ranges")
                : new IssueDto("warning", $"{overtime} over 10 hours today", "Consider checking in"));

            return issues;
        }
    }
}
