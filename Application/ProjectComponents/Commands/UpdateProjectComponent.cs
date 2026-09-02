using Application.Core;
using Application.ProjectComponents.DTOs;
using AutoMapper;
using MediatR;
using Persistence;

namespace Application.ProjectComponents.Commands;

public class UpdateProjectComponent
{
    public class Command : IRequest<Result<ProjectComponentDto>>
    {
        public int Id { get; set; }
        public required UpsertProjectComponentRequest Component { get; set; }
    }

    public class Handler(AppDbContext context, IMapper mapper) : IRequestHandler<Command, Result<ProjectComponentDto>>
    {
        public async Task<Result<ProjectComponentDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var component = await context.ProjectComponents.FindAsync([request.Id], cancellationToken);
            if (component is null)
                return Result<ProjectComponentDto>.Failure("Component not found.");

            mapper.Map(request.Component, component);

            await context.SaveChangesAsync(cancellationToken);

            return Result<ProjectComponentDto>.Success(mapper.Map<ProjectComponentDto>(component));
        }
    }
}
