using Application.Attendance.DTOs;
using Application.Core;
using Domain;
using Domain.Services;
using MediatR;
using Persistence;

namespace Application.Attendance.Commands;

/// <summary>
/// Records a check-in for the calling employee and returns the resulting day.
/// Refused if the day is already open, whether they are working or on a break.
/// </summary>
public class CheckIn
{
    public class Command : IRequest<Result<TodayStateDto>>
    {
        public required string RequestingUserId { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Command, Result<TodayStateDto>>
    {
        public async Task<Result<TodayStateDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var profile = await AttendanceDay.ResolveProfileAsync(context, request.RequestingUserId, cancellationToken);
            if (profile is null) return AttendanceDay.NoProfile<TodayStateDto>();

            var now = DateTime.UtcNow;
            var events = await AttendanceDay.LoadDayEventsAsync(context, profile.Id, now, cancellationToken);
            var state = AttendanceDayStateCalculator.Calculate(events, now);

            if (state.Status is AttendanceDayStatus.In or AttendanceDayStatus.Break)
            {
                return Result<TodayStateDto>.Conflict("Already checked in.");
            }

            context.AttendanceEvents.Add(
                AttendanceDay.NewEvent(profile.Id, now, AttendanceEventType.CheckIn));
            await context.SaveChangesAsync(cancellationToken);

            return Result<TodayStateDto>.Success(
                await AttendanceDay.BuildTodayStateAsync(context, profile.Id, now, cancellationToken));
        }
    }
}
