using Application.Attendance.DTOs;
using Application.Attendance.Support;
using Application.Core;
using Domain;
using Domain.Services;
using MediatR;
using Persistence;

namespace Application.Attendance.Commands;

/// <summary>
/// Opens a break. Only valid while working: not before checking in, not after
/// checking out, and not while a break is already running.
/// </summary>
public class StartBreak
{
    public class Command : IRequest<Result<TodayStateDto>>
    {
        public required string RequestingUserId { get; set; }

        /// <summary>
        /// True when this break is opened by client-side idle detection rather
        /// than the user clicking Start Break. Recorded as a distinct event type
        /// so <see cref="EndBreak"/> can tell an idle-triggered break apart from
        /// one the user opened by hand.
        /// </summary>
        public bool IsAutomatic { get; set; }
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

            if (state.Status != AttendanceDayStatus.In)
            {
                return Result<TodayStateDto>.Conflict("Can only start a break while working.");
            }

            var eventType = request.IsAutomatic ? AttendanceEventType.AutoBreakStart : AttendanceEventType.BreakStart;
            context.AttendanceEvents.Add(
                AttendanceDay.NewEvent(profile.Id, now, eventType));
            await context.SaveChangesAsync(cancellationToken);

            return Result<TodayStateDto>.Success(
                await AttendanceDay.BuildTodayStateAsync(context, profile.Id, now, cancellationToken));
        }
    }
}
