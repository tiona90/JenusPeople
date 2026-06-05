using Application.Core;
using Application.ProjectActivityTypes.DTOs;
using AutoMapper;
using Domain;
using MediatR;
using Persistence;

namespace Application.ProjectActivityTypes.Commands;

public class CreateProjectActivityType
{
    public class Command : IRequest<Result<ProjectActivityTypeDto>>
    {
        public required UpsertProjectActivityTypeRequest ActivityType { get; set; }
    }

    public class Handler(AppDbContext context, IMapper mapper) : IRequestHandler<Command, Result<ProjectActivityTypeDto>>
    {
        public async Task<Result<ProjectActivityTypeDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var activityType = mapper.Map<ProjectActivityType>(request.ActivityType);

            context.ProjectActivityTypes.Add(activityType);
            await context.SaveChangesAsync(cancellationToken);

            return Result<ProjectActivityTypeDto>.Success(mapper.Map<ProjectActivityTypeDto>(activityType));
        }
    }
}
