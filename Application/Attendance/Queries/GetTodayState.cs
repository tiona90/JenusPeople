using Application.Attendance.DTOs;
using Application.Core;
using MediatR;
using Persistence;

namespace Application.Attendance.Queries;

/// <summary>The calling employee's own state for today.</summary>
public class GetTodayState
{
    public class Query : IRequest<Result<TodayStateDto>>
    {
        public required string RequestingUserId { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Query, Result<TodayStateDto>>
    {
        public async Task<Result<TodayStateDto>> Handle(Query request, CancellationToken cancellationToken)
        {
            var profile = await AttendanceDay.ResolveProfileAsync(context, request.RequestingUserId, cancellationToken);
            if (profile is null) return AttendanceDay.NoProfile<TodayStateDto>();

            return Result<TodayStateDto>.Success(
                await AttendanceDay.BuildTodayStateAsync(context, profile.Id, DateTime.UtcNow, cancellationToken));
        }
    }
}
