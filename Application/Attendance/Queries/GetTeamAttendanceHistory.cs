using Application.Attendance.DTOs;
using Application.Core;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Attendance.Queries;

/// <summary>
/// Per-day earliest check-in time per team member over the last N days, for the
/// "Team Health" line chart. Reported as minutes from midnight UTC so the chart
/// plots a numeric y-axis without reconstructing timezones, and null for a day
/// with no check-in (off, on leave, or a weekend).
///
/// Note this scopes to direct reports only (ManagerId), unlike the team board,
/// which also covers managed departments. Preserved as-is: the chart and the board
/// have always drawn different populations for a non-admin, and reconciling them
/// is a product decision, not a refactor.
/// </summary>
public class GetTeamAttendanceHistory
{
    public const int DefaultDays = 30;
    public const int MaxDays = 90;

    public class Query : IRequest<Result<TeamHistoryDto>>
    {
        public required string RequestingUserId { get; set; }
        public bool IsAdmin { get; set; }
        public int Days { get; set; } = DefaultDays;
    }

    public class Handler(AppDbContext context) : IRequestHandler<Query, Result<TeamHistoryDto>>
    {
        public async Task<Result<TeamHistoryDto>> Handle(Query request, CancellationToken cancellationToken)
        {
            var days = request.Days is <= 0 or > MaxDays ? DefaultDays : request.Days;

            var profilesQuery = context.EmployeeProfiles
                .Include(p => p.User)
                .AsQueryable();

            if (!request.IsAdmin)
            {
                var me = await AttendanceDay.ResolveProfileAsync(context, request.RequestingUserId, cancellationToken);
                if (me is null) return AttendanceDay.NoProfile<TeamHistoryDto>();

                profilesQuery = profilesQuery.Where(p => p.ManagerId == me.Id);
            }

            var profiles = await profilesQuery
                .OrderBy(p => p.User != null ? p.User.DisplayName : "")
                .ToListAsync(cancellationToken);

            var profileIds = profiles.Select(p => p.Id).ToList();

            var now = DateTime.UtcNow;
            var today = AttendanceDay.UtcDayStart(now);
            var rangeStart = today.AddDays(-(days - 1));
            var rangeEnd = today.AddDays(1);

            // Only check-ins matter here, and only the earliest one per day.
            var checkIns = await context.AttendanceEvents
                .Where(e => profileIds.Contains(e.EmployeeProfileId)
                    && e.Type == AttendanceEventType.CheckIn
                    && e.At >= rangeStart && e.At < rangeEnd)
                .Select(e => new { e.EmployeeProfileId, e.At })
                .ToListAsync(cancellationToken);

            var earliestPerDay = checkIns
                .GroupBy(e => (e.EmployeeProfileId, Day: AttendanceDay.UtcDayStart(e.At)))
                .ToDictionary(g => g.Key, g => g.Min(x => x.At));

            var members = profiles.Select(profile =>
            {
                var dayList = new List<MemberCheckInDayDto>(capacity: days);
                for (var i = days - 1; i >= 0; i--)
                {
                    var day = today.AddDays(-i);
                    int? minutes = earliestPerDay.TryGetValue((profile.Id, day), out var at)
                        ? at.Hour * 60 + at.Minute
                        : null;

                    dayList.Add(new MemberCheckInDayDto(day.ToString("yyyy-MM-dd"), minutes));
                }

                return new TeamMemberHistoryDto(profile.Id, AttendanceDay.DisplayNameOf(profile), dayList);
            }).ToList();

            return Result<TeamHistoryDto>.Success(new TeamHistoryDto(members));
        }
    }
}
