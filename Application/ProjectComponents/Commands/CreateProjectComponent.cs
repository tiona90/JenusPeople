using Application.Core;
using Application.ProjectComponents.DTOs;
using AutoMapper;
using Domain;
using MediatR;
using Persistence;

namespace Application.ProjectComponents.Commands;

public class CreateProjectComponent
{
    public class Command : IRequest<Result<ProjectComponentDto>>
    {
        public required UpsertProjectComponentRequest Component { get; set; }
    }

    public class Handler(AppDbContext context, IMapper mapper) : IRequestHandler<Command, Result<ProjectComponentDto>>
    {
        public async Task<Result<ProjectComponentDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var component = mapper.Map<ProjectComponent>(request.Component);

            context.ProjectComponents.Add(component);
            await context.SaveChangesAsync(cancellationToken);

            return Result<ProjectComponentDto>.Success(mapper.Map<ProjectComponentDto>(component));
        }
    }
}
