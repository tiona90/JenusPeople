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
/// The team board: today's status per member, a Mon–Fri minutes grid for the
/// current ISO week with short days and leave marked (DailyHoursRule), and who
/// was short on the previous working day.
///
/// A System Administrator sees everybody. A Manager sees their managed departments and their
/// direct reports, and never themselves — the board is for the people they are
/// responsible for, and their own day is on their personal page.
/// </summary>
public class GetTeamAttendance
{
    public class Query : IRequest<Result<TeamAttendanceDto>>
    {
        public required string RequestingUserId { get; set; }
        public bool IsAdmin { get; set; }

        /// <summary>Test seam for the clock; the controller leaves it null.</summary>
        public DateTime? NowUtc { get; init; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Query, Result<TeamAttendanceDto>>
    {
        public async Task<Result<TeamAttendanceDto>> Handle(Query request, CancellationToken cancellationToken)
        {
            var profilesQuery = AttendanceDay.ExcludeAdmins(
                context.EmployeeProfiles
                    .Include(p => p.User)
                    .Include(p => p.Department));

            if (!request.IsAdmin)
            {
                var me = await AttendanceDay.ResolveProfileAsync(context, request.RequestingUserId, cancellationToken);
                if (me is null) return AttendanceDay.NoProfile<TeamAttendanceDto>();

                var scope = await ManagerAccessScopeResolver.ResolveAsync(
                    context, request.RequestingUserId, cancellationToken);

                profilesQuery = profilesQuery.Where(p =>
                    p.UserId != request.RequestingUserId
                    && ((p.DepartmentId != null && scope.ManagedDepartmentIds.Contains(p.DepartmentId.Value))
                        || (p.ManagerId != null && scope.ManagerProfileIds.Contains(p.ManagerId))));
            }

            var profiles = await profilesQuery
                .OrderBy(p => p.User != null ? p.User.DisplayName : "")
                .ToListAsync(cancellationToken);

            var profileIds = profiles.Select(p => p.Id).ToList();
            var now = request.NowUtc ?? DateTime.UtcNow;
            var schedule = await WorkingDaySchedule.LoadAsync(context, cancellationToken);

            var todayByEmployee = await AttendanceDay.LoadDayEventsByEmployeeAsync(
                context, profileIds, now, cancellationToken);
            var onLeave = await AttendanceDay.LoadOnLeaveProfileIdsAsync(
                context, profileIds, now, cancellationToken);

            var today = DateOnly.FromDateTime(AttendanceDay.UtcDayStart(now));
            var hours = await DailyHoursContext.LoadAsync(
                context, profileIds, today.AddDays(-ShortDayDigest.LookBackDays), today.AddDays(6), now, cancellationToken);

            var members = profiles
                .Select(p => BuildMember(p, AttendanceDay.StateFor(todayByEmployee, p.Id, now), onLeave, schedule, now))
                .ToList();

            var week = await BuildWeekAsync(profiles, profileIds, hours, now, cancellationToken);
            var digest = await ShortDayDigest.BuildAsync(context, profiles, hours, now, cancellationToken);

            return Result<TeamAttendanceDto>.Success(new TeamAttendanceDto(
                members,
                week,
                digest?.Day.ToString("yyyy-MM-dd"),
                digest?.People ?? []));
        }

        private static TeamMemberAttendanceDto BuildMember(
            EmployeeProfile profile,
            AttendanceDayState state,
            HashSet<string> onLeave,
            WorkingDaySchedule schedule,
            DateTime now)
        {
            // Leave outranks attendance: someone on approved leave is reported as
            // away even if a stale event would otherwise place them at work.
            var (status, note) = onLeave.Contains(profile.Id)
                ? ("leave", "On leave today")
                : state.Status switch
                {
                    // CheckInAt is guaranteed non-null for In and Break — the
                    // calculator cannot reach either status without one.
                    AttendanceDayStatus.In => ("in", schedule.IsLate(state.CheckInAt!.Value) ? "Late check-in" : "On track"),
                    AttendanceDayStatus.Break => ("break", state.IsAutoBreak ? "Idle" : "On break"),
                    AttendanceDayStatus.Done => ("out", $"Done at {state.CheckOutAt:HH:mm}"),
                    _ => ("out", "Not checked in"),
                };

            return new TeamMemberAttendanceDto(
                profile.Id,
                AttendanceDay.DisplayNameOf(profile),
                profile.Department?.Name ?? "",
                profile.JobTitle,
                status,
                AttendanceDay.AsUtcNullable(state.CheckInAt),
                state.WorkedMinutes,
                AttendanceDay.AsUtcNullable(state.OnBreakSince),
                note,
                state.IsAutoBreak,
                schedule.BreakMinutesTaken(state, now),
                schedule.BreakVariance(state, now));
        }

        /// <summary>
        /// Monday to Friday of the current ISO week. Days with no check-in report
        /// null minutes rather than zero, so the grid can distinguish "did not work"
        /// from "worked no measurable time". Each day also carries DailyHoursRule's
        /// verdict: a short day's minutes and approved leave.
        /// </summary>
        private async Task<List<TeamWeekRowDto>> BuildWeekAsync(
            List<EmployeeProfile> profiles,
            List<string> profileIds,
            DailyHoursContext hours,
            DateTime now,
            CancellationToken cancellationToken)
        {
            var diffToMonday = (int)now.DayOfWeek - (int)DayOfWeek.Monday;
            if (diffToMonday < 0) diffToMonday += 7;

            var monday = AttendanceDay.UtcDayStart(now).AddDays(-diffToMonday);
            var friday = monday.AddDays(5);

            var weekEvents = await context.AttendanceEvents
                .Where(e => profileIds.Contains(e.EmployeeProfileId) && e.At >= monday && e.At < friday)
                .ToListAsync(cancellationToken);

            var rows = new List<TeamWeekRowDto>(capacity: profiles.Count);
            foreach (var profile in profiles)
            {
                var days = new List<WeekDayHoursDto>(capacity: 5);
                var total = 0;

                for (var i = 0; i < 5; i++)
                {
                    var dayStart = monday.AddDays(i);
                    var dayEnd = dayStart.AddDays(1);

                    var state = AttendanceDayStateCalculator.Calculate(
                        weekEvents.Where(e =>
                            e.EmployeeProfileId == profile.Id && e.At >= dayStart && e.At < dayEnd),
                        now);

                    int? minutes = state.CheckInAt is null ? null : state.WorkedMinutes;
                    if (minutes is not null) total += minutes.Value;

                    var note = state.Status switch
                    {
                        AttendanceDayStatus.In => "in",
                        AttendanceDayStatus.Break => "break",
                        _ => (string?)null,
                    };

                    var verdict = hours.Judge(profile.Id, DateOnly.FromDateTime(dayStart), state);
                    days.Add(new WeekDayHoursDto(
                        dayStart.ToString("yyyy-MM-dd"),
                        minutes,
                        note,
                        verdict.ShortByMinutes,
                        verdict.Kind == DayKind.Leave));
                }

                rows.Add(new TeamWeekRowDto(profile.Id, AttendanceDay.DisplayNameOf(profile), days, total));
            }

            return rows;
        }
    }
}
