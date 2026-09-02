using Application.Core;
using Application.ProjectTypes.DTOs;
using AutoMapper;
using MediatR;
using Persistence;

namespace Application.ProjectTypes.Commands;

public class UpdateProjectType
{
    public class Command : IRequest<Result<ProjectTypeDto>>
    {
        public int Id { get; set; }
        public required UpsertProjectTypeRequest Type { get; set; }
    }

    public class Handler(AppDbContext context, IMapper mapper) : IRequestHandler<Command, Result<ProjectTypeDto>>
    {
        public async Task<Result<ProjectTypeDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var projectType = await context.ProjectTypes.FindAsync([request.Id], cancellationToken);
            if (projectType is null)
                return Result<ProjectTypeDto>.Failure("Project type not found.");

            mapper.Map(request.Type, projectType);

            await context.SaveChangesAsync(cancellationToken);

            return Result<ProjectTypeDto>.Success(mapper.Map<ProjectTypeDto>(projectType));
        }
    }
}
