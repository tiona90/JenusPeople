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
/// including days with no events at all — the caller renders a continuous strip,
/// so gaps have to come back as "absent" rather than be missing.
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
    }

    public class Handler(AppDbContext context) : IRequestHandler<Query, Result<List<DayHistoryDto>>>
    {
        public async Task<Result<List<DayHistoryDto>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var days = request.Days is <= 0 or > MaxDays ? DefaultDays : request.Days;

            var profile = await AttendanceDay.ResolveProfileAsync(context, request.RequestingUserId, cancellationToken);
            if (profile is null) return AttendanceDay.NoProfile<List<DayHistoryDto>>();

            var now = DateTime.UtcNow;
            var today = AttendanceDay.UtcDayStart(now);
            var from = today.AddDays(-(days - 1));

            var events = await context.AttendanceEvents
                .Where(e => e.EmployeeProfileId == profile.Id && e.At >= from)
                .OrderBy(e => e.At)
                .ToListAsync(cancellationToken);

            var byDay = events
                .GroupBy(e => AttendanceDay.UtcDayStart(e.At))
                .ToDictionary(g => g.Key, g => g.ToList());

            var result = new List<DayHistoryDto>(capacity: days);
            for (var i = days - 1; i >= 0; i--)
            {
                var date = today.AddDays(-i);
                byDay.TryGetValue(date, out var dayEvents);
                var state = AttendanceDayStateCalculator.Calculate(dayEvents ?? [], now);

                result.Add(new DayHistoryDto(
                    date.ToString("yyyy-MM-dd"),
                    HistoryStatus(state),
                    AttendanceDay.AsUtcNullable(state.CheckInAt),
                    AttendanceDay.AsUtcNullable(state.CheckOutAt),
                    state.TotalBreakMinutes,
                    state.WorkedMinutes));
            }

            return Result<List<DayHistoryDto>>.Success(result);
        }

        /// <summary>
        /// The history strip has its own vocabulary: a finished day is graded
        /// complete or late on its check-in hour, and a day still open reads as
        /// in-progress whether or not a break is running.
        /// </summary>
        private static string HistoryStatus(AttendanceDayState state) => state.Status switch
        {
            AttendanceDayStatus.Out => "absent",
            AttendanceDayStatus.In => "in-progress",
            AttendanceDayStatus.Break => "in-progress",
            AttendanceDayStatus.Done => state.CheckInAt.HasValue && state.CheckInAt.Value.Hour > 9
                ? "late"
                : "complete",
            _ => AttendanceDay.WireStatus(state.Status),
        };
    }
}
