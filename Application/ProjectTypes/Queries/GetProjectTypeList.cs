using Application.ProjectTypes.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.ProjectTypes.Queries;

public class GetProjectTypeList
{
    public class Query : IRequest<List<ProjectTypeDto>>
    {
    }

    public class Handler(AppDbContext context) : IRequestHandler<Query, List<ProjectTypeDto>>
    {
        public async Task<List<ProjectTypeDto>> Handle(Query request, CancellationToken cancellationToken)
        {
            // Disabled types are returned too: this is the admin catalogue, and
            // the panel filters by status itself.
            // Lower-cased rather than ordered on the column directly, matching
            // GetProjectComponentList: a type name need not be capitalised, and an
            // ordinal collation would sort those after every capitalised name
            // instead of into the alphabet where a reader scans for them.
            return await context.ProjectTypes
                .AsNoTracking()
                .OrderBy(t => t.Name.ToLower())
                .Select(t => new ProjectTypeDto
                {
                    Id = t.Id,
                    Name = t.Name,
                    Description = t.Description,
                    Icon = t.Icon,
                    ColorKey = t.ColorKey,
                    IsActive = t.IsActive,
                    UsedInProjects = t.ProjectAssignments.Count,
                })
                .ToListAsync(cancellationToken);
        }
    }
}
