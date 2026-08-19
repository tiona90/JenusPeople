using Application.Attendance.DTOs;
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
    /// Thresholds the dashboard judgements rest on. Named because they were bare
    /// numbers scattered through one 200-line method — and because "late" meaning
    /// 10:00 here while the personal history strip grades lateness at 09:00 is a
    /// real inconsistency, easier to notice once both have names.
    /// </summary>
    private const int LateCheckInHour = 10;
    private const int NominalStartHour = 9;
    private const int OvertimeMinutes = 600;
    private const int RecentActivityLimit = 20;
    private const int NotCheckedInListLimit = 5;
    private const int LateNamesShown = 3;

    public class Query : IRequest<Result<CompanyAttendanceDto>>
    {
    }

    public class Handler(AppDbContext context) : IRequestHandler<Query, Result<CompanyAttendanceDto>>
    {
        public async Task<Result<CompanyAttendanceDto>> Handle(Query request, CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            var todayStart = AttendanceDay.UtcDayStart(now);

            var profiles = await context.EmployeeProfiles
                .Include(p => p.User)
                .Include(p => p.Department)
                .ToListAsync(cancellationToken);

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
                profiles, profileIds, todayByEmployee, onLeave, now, cancellationToken);

            var issues = BuildIssues(profiles, stateByProfileId, onLeave, departments, totals, now, todayStart);

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
                    ActionName(e.Type, AttendanceDay.AsUtc(e.At)),
                    AttendanceDay.AsUtc(e.At),
                    minutesAgo);
            }).ToList();

            // Synthetic "Not checked in" rows, added only once the morning is late
            // enough for an absence to mean anything. They carry no timestamp,
            // which is the only way a consumer can tell them from real events.
            if (now.Hour >= LateCheckInHour)
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

        private static string ActionName(AttendanceEventType type, DateTime atUtc) => type switch
        {
            AttendanceEventType.CheckIn => atUtc.Hour >= LateCheckInHour ? "Late check-in" : "Checked in",
            AttendanceEventType.CheckOut => "Checked out",
            AttendanceEventType.BreakStart => "Started break",
            _ => "Back from break",
        };

        private static List<IssueDto> BuildIssues(
            List<EmployeeProfile> profiles,
            Dictionary<string, AttendanceDayState> stateByProfileId,
            HashSet<string> onLeave,
            List<DeptAttendanceDto> departments,
            Totals totals,
            DateTime now,
            DateTime todayStart)
        {
            var issues = new List<IssueDto>();

            // 1) Departments with people who have not checked in.
            if (now.Hour >= LateCheckInHour)
            {
                foreach (var dept in departments.Where(d => d.Out > 0))
                {
                    issues.Add(new IssueDto(
                        "danger",
                        $"{dept.Out} not checked in ({dept.Name})",
                        $"No check-in by {now.Hour:D2}:00 · likely unscheduled absence"));
                }
            }

            // 2) Late check-ins, reported as minutes past the nominal start.
            var lateNames = new List<string>();
            foreach (var profile in profiles)
            {
                if (onLeave.Contains(profile.Id)) continue;

                var state = stateByProfileId[profile.Id];
                if (state.CheckInAt is not { } checkInAt) continue;
                if (AttendanceDay.AsUtc(checkInAt).Hour < LateCheckInHour) continue;

                var lateMinutes = (int)(checkInAt - todayStart.AddHours(NominalStartHour)).TotalMinutes;
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

            // 3) On-leave summary, broken down by department.
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

            // 4) Overtime. Always reported, so the panel says something reassuring
            // when nothing is wrong rather than going blank.
            var overtime = profiles.Count(p => stateByProfileId[p.Id].WorkedMinutes > OvertimeMinutes);
            issues.Add(overtime == 0
                ? new IssueDto("success", "No unusual overtime", "All employees within healthy hour ranges")
                : new IssueDto("warning", $"{overtime} over 10 hours today", "Consider checking in"));

            return issues;
        }
    }
}
