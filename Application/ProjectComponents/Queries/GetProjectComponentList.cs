using Application.ProjectComponents.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.ProjectComponents.Queries;

public class GetProjectComponentList
{
    public class Query : IRequest<List<ProjectComponentDto>>
    {
    }

    public class Handler(AppDbContext context) : IRequestHandler<Query, List<ProjectComponentDto>>
    {
        public async Task<List<ProjectComponentDto>> Handle(Query request, CancellationToken cancellationToken)
        {
            // Disabled components are returned too: this is the admin catalogue,
            // and the panel filters by status itself.
            // Lower-cased rather than ordered on the column directly: component
            // names are not all capitalised (jDocs), and an ordinal collation
            // would sort those after every capitalised name instead of into the
            // alphabet where a reader scans for them.
            return await context.ProjectComponents
                .AsNoTracking()
                .OrderBy(c => c.Name.ToLower())
                .Select(c => new ProjectComponentDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    Description = c.Description,
                    Icon = c.Icon,
                    ColorKey = c.ColorKey,
                    IsActive = c.IsActive,
                })
                .ToListAsync(cancellationToken);
        }
    }
}
