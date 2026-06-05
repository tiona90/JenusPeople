using Application.Core;
using Application.ProjectActivityTypes.DTOs;
using AutoMapper;
using MediatR;
using Persistence;

namespace Application.ProjectActivityTypes.Commands;

public class UpdateProjectActivityType
{
    public class Command : IRequest<Result<ProjectActivityTypeDto>>
    {
        public int Id { get; set; }
        public required UpsertProjectActivityTypeRequest ActivityType { get; set; }
    }

    public class Handler(AppDbContext context, IMapper mapper) : IRequestHandler<Command, Result<ProjectActivityTypeDto>>
    {
        public async Task<Result<ProjectActivityTypeDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var activityType = await context.ProjectActivityTypes.FindAsync([request.Id], cancellationToken);
            if (activityType is null)
                return Result<ProjectActivityTypeDto>.Failure("Activity type not found.");

            mapper.Map(request.ActivityType, activityType);

            await context.SaveChangesAsync(cancellationToken);

            return Result<ProjectActivityTypeDto>.Success(mapper.Map<ProjectActivityTypeDto>(activityType));
        }
    }
}
