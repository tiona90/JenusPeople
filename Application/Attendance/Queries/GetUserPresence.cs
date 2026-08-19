using Application.Attendance.DTOs;
using Application.Core;
using Domain.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Attendance.Queries;

/// <summary>
/// Authoritative per-user presence for today, keyed by Identity user id.
///
/// The admin Users panel used to infer this client-side from the company board's
/// "recent activity" feed, which was wrong in both directions: that feed is capped
/// at 20 events, is keyed by display name, and carries synthetic "Not checked in"
/// rows whose text contains the substring "checked in". Presence has one source of
/// truth — today's events run through
/// <see cref="AttendanceDayStateCalculator"/> — so it is computed here.
/// </summary>
public class GetUserPresence
{
    public class Query : IRequest<Result<List<UserPresenceDto>>>
    {
    }

    public class Handler(AppDbContext context) : IRequestHandler<Query, Result<List<UserPresenceDto>>>
    {
        public async Task<Result<List<UserPresenceDto>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;

            var profiles = await context.EmployeeProfiles
                .Select(p => new { p.Id, p.UserId })
                .ToListAsync(cancellationToken);

            var profileIds = profiles.Select(p => p.Id).ToList();
            var todayByEmployee = await AttendanceDay.LoadDayEventsByEmployeeAsync(
                context, profileIds, now, cancellationToken);

            var result = profiles
                .Where(p => !string.IsNullOrEmpty(p.UserId))
                .Select(p =>
                {
                    todayByEmployee.TryGetValue(p.Id, out var events);
                    events ??= [];

                    var state = AttendanceDayStateCalculator.Calculate(events, now);

                    // Done — checked in *and* out — collapses to offline alongside
                    // Out, which is never having checked in. Only an open day counts
                    // as present.
                    var status = state.Status switch
                    {
                        AttendanceDayStatus.In => "online",
                        AttendanceDayStatus.Break => "away",
                        _ => "offline",
                    };

                    DateTime? lastActivityAt = events.Count > 0
                        ? AttendanceDay.AsUtc(events.Max(e => e.At))
                        : null;

                    return new UserPresenceDto(
                        p.UserId,
                        status,
                        AttendanceDay.AsUtcNullable(state.CheckInAt),
                        lastActivityAt);
                })
                .ToList();

            return Result<List<UserPresenceDto>>.Success(result);
        }
    }
}
