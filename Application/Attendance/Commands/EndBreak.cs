using Application.Attendance.DTOs;
using Application.Attendance.Support;
using Application.Core;
using Domain;
using Domain.Services;
using MediatR;
using Persistence;

namespace Application.Attendance.Commands;

/// <summary>Closes an open break. Only valid while one is running.</summary>
public class EndBreak
{
    public class Command : IRequest<Result<TodayStateDto>>
    {
        public required string RequestingUserId { get; set; }

        /// <summary>
        /// True when this is client-side idle detection reporting activity
        /// resumed, rather than the user clicking Resume. An automatic call
        /// only ever closes a break it opened itself — it must not silently end
        /// a break the user started by hand.
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

            if (state.Status != AttendanceDayStatus.Break)
            {
                return Result<TodayStateDto>.Conflict("Not currently on break.");
            }

            if (request.IsAutomatic && !state.IsAutoBreak)
            {
                // Idle detection resuming after a manually-started break: leave
                // it running, the user has to end it themselves.
                return Result<TodayStateDto>.Success(
                    await AttendanceDay.BuildTodayStateAsync(context, profile.Id, now, cancellationToken));
            }

            var eventType = request.IsAutomatic ? AttendanceEventType.AutoBreakEnd : AttendanceEventType.BreakEnd;
            context.AttendanceEvents.Add(
                AttendanceDay.NewEvent(profile.Id, now, eventType));
            await context.SaveChangesAsync(cancellationToken);

            return Result<TodayStateDto>.Success(
                await AttendanceDay.BuildTodayStateAsync(context, profile.Id, now, cancellationToken));
        }
    }
}
