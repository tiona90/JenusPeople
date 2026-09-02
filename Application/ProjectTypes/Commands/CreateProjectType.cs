using Application.Core;
using Application.ProjectTypes.DTOs;
using AutoMapper;
using Domain;
using MediatR;
using Persistence;

namespace Application.ProjectTypes.Commands;

public class CreateProjectType
{
    public class Command : IRequest<Result<ProjectTypeDto>>
    {
        public required UpsertProjectTypeRequest Type { get; set; }
    }

    public class Handler(AppDbContext context, IMapper mapper) : IRequestHandler<Command, Result<ProjectTypeDto>>
    {
        public async Task<Result<ProjectTypeDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var projectType = mapper.Map<ProjectType>(request.Type);

            context.ProjectTypes.Add(projectType);
            await context.SaveChangesAsync(cancellationToken);

            return Result<ProjectTypeDto>.Success(mapper.Map<ProjectTypeDto>(projectType));
        }
    }
}
