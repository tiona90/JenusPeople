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
/// The calling employee's own day-by-day history, one row per calendar day
/// including days with no events at all, graded against the day's target
/// (DailyHoursRule) — the caller renders a continuous strip, so gaps have to
/// come back as "absent" rather than be missing.
/// </summary>
public class GetMyAttendanceHistory
{
    /// <summary>Days of history to return, clamped to this range.</summary>
    public const int DefaultDays = 30;
    public const int MaxDays = 180;

    public class Query : IRequest<Result<List<DayHistoryDto>>>
    {
        public required string RequestingUserId { get; set; }
        public int Days { get; set; } = DefaultDays;

        /// <summary>Test seam for the clock; the controller leaves it null.</summary>
        public DateTime? NowUtc { get; init; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Query, Result<List<DayHistoryDto>>>
    {
        public async Task<Result<List<DayHistoryDto>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var days = request.Days is <= 0 or > MaxDays ? DefaultDays : request.Days;

            var profile = await AttendanceDay.ResolveProfileAsync(context, request.RequestingUserId, cancellationToken);
            if (profile is null) return AttendanceDay.NoProfile<List<DayHistoryDto>>();

            var now = request.NowUtc ?? DateTime.UtcNow;
            var schedule = await WorkingDaySchedule.LoadAsync(context, cancellationToken);
            var today = AttendanceDay.UtcDayStart(now);
            var from = today.AddDays(-(days - 1));

            var events = await context.AttendanceEvents
                .Where(e => e.EmployeeProfileId == profile.Id && e.At >= from)
                .OrderBy(e => e.At)
                .ToListAsync(cancellationToken);

            var byDay = events
                .GroupBy(e => AttendanceDay.UtcDayStart(e.At))
                .ToDictionary(g => g.Key, g => g.ToList());

            var hours = await DailyHoursContext.LoadAsync(
                context, [profile.Id], DateOnly.FromDateTime(from), DateOnly.FromDateTime(today), now, cancellationToken);

            var result = new List<DayHistoryDto>(capacity: days);
            for (var i = days - 1; i >= 0; i--)
            {
                var date = today.AddDays(-i);
                byDay.TryGetValue(date, out var dayEvents);
                var state = AttendanceDayStateCalculator.Calculate(dayEvents ?? [], now);

                var verdict = hours.Judge(profile.Id, DateOnly.FromDateTime(date), state);

                result.Add(new DayHistoryDto(
                    date.ToString("yyyy-MM-dd"),
                    HistoryStatus(state, schedule, verdict),
                    AttendanceDay.AsUtcNullable(state.CheckInAt),
                    AttendanceDay.AsUtcNullable(state.CheckOutAt),
                    state.TotalBreakMinutes,
                    state.WorkedMinutes,
                    schedule.BreakVariance(state, now),
                    verdict.ShortByMinutes,
                    verdict.Kind == DayKind.Leave,
                    verdict.TargetMinutes));
            }

            return Result<List<DayHistoryDto>>.Success(result);
        }

        /// <summary>
        /// The history strip has its own vocabulary. Approved leave reads as leave
        /// whatever else happened; a non-working day with nothing on it is off; a
        /// working day with nothing is absent; a day still open is in-progress; a
        /// finished day under its target is short, and otherwise late or complete
        /// on its check-in against the org's working-hours start. Short outranks
        /// late: the check-in time is in its own column anyway.
        /// </summary>
        private static string HistoryStatus(AttendanceDayState state, WorkingDaySchedule schedule, DailyHoursVerdict verdict)
        {
            if (verdict.Kind == DayKind.Leave) return "leave";
            if (state.Status == AttendanceDayStatus.Out)
                return verdict.Kind == DayKind.NonWorking ? "off" : "absent";
            if (state.Status is AttendanceDayStatus.In or AttendanceDayStatus.Break) return "in-progress";
            if (verdict.ShortByMinutes is not null) return "short";
            return state.CheckInAt.HasValue && schedule.IsLate(state.CheckInAt.Value) ? "late" : "complete";
        }
    }
}
