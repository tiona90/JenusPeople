using Application.Attendance.DTOs;
using Application.Attendance.Support;
using Application.Core;
using Domain;
using Domain.Services;
using MediatR;
using Persistence;

namespace Application.Attendance.Commands;

/// <summary>
/// Records a check-out for the calling employee. Checking out while on a break
/// closes the break first, so the unfinished break is not billed as work — the
/// calculator would otherwise treat everything from break-start to check-out as
/// break time, which is the same answer but reached by accident rather than by
/// recording what happened.
/// </summary>
public class CheckOut
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

            if (state.Status is not (AttendanceDayStatus.In or AttendanceDayStatus.Break))
            {
                return Result<TodayStateDto>.Conflict("Not currently checked in.");
            }

            if (state.Status == AttendanceDayStatus.Break)
            {
                context.AttendanceEvents.Add(
                    AttendanceDay.NewEvent(profile.Id, now, AttendanceEventType.BreakEnd));
            }

            context.AttendanceEvents.Add(
                AttendanceDay.NewEvent(profile.Id, now, AttendanceEventType.CheckOut));
            await context.SaveChangesAsync(cancellationToken);

            return Result<TodayStateDto>.Success(
                await AttendanceDay.BuildTodayStateAsync(context, profile.Id, now, cancellationToken));
        }
    }
}
