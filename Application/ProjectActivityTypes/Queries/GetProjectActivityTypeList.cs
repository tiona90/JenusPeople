using Application.ProjectActivityTypes.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.ProjectActivityTypes.Queries;

public class GetProjectActivityTypeList
{
    public class Query : IRequest<List<ProjectActivityTypeDto>>
    {
    }

    public class Handler(AppDbContext context) : IRequestHandler<Query, List<ProjectActivityTypeDto>>
    {
        public async Task<List<ProjectActivityTypeDto>> Handle(Query request, CancellationToken cancellationToken)
        {
            return await context.ProjectActivityTypes
                .AsNoTracking()
                .OrderBy(a => a.Name)
                .Select(a => new ProjectActivityTypeDto
                {
                    Id = a.Id,
                    Name = a.Name,
                    Description = a.Description,
                    Icon = a.Icon,
                    ColorKey = a.ColorKey,
                    IsActive = a.IsActive,
                    HoursYtd = 0,
                    UsedInProjects = 0,
                })
                .ToListAsync(cancellationToken);
        }
    }
}
