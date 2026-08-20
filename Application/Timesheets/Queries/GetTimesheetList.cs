using Application.Core;
using Application.Timesheets.DTOs;
using Application.Timesheets.Support;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Timesheets.Queries
{
    public class GetTimesheetList
    {
        public class Query : IRequest<PagedResult<TimesheetDto>>
        {
            public string RequestingUserId { get; set; } = string.Empty;
            public bool IsAdmin { get; set; }
            public bool IsManager { get; set; }
            public int? Page { get; set; }
            public int? PageSize { get; set; }
        }

        public class Handler : IRequestHandler<Query, PagedResult<TimesheetDto>>
        {
            private readonly AppDbContext _context;
            public Handler(AppDbContext context) { _context = context; }

            public async Task<PagedResult<TimesheetDto>> Handle(Query request, CancellationToken cancellationToken)
            {
                IQueryable<Domain.Timesheet> query = _context.Timesheets
                    .Include(t => t.Employee).ThenInclude(e => e.User)
                    .Include(t => t.Entries).ThenInclude(e => e.Project)
                    .AsNoTracking();

                // Shared with GetTimesheetDetail so listing timesheets and
                // reading one by id agree on who is allowed to see what.
                query = await TimesheetScope.ApplyAsync(
                    _context,
                    query,
                    request.RequestingUserId,
                    request.IsAdmin,
                    request.IsManager,
                    cancellationToken);

                // Filtering above runs in SQL. Count the filtered set, then order +
                // optionally page (in SQL) before materializing the entries.
                var total = await query.CountAsync(cancellationToken);

                var ordered = query
                    .OrderByDescending(t => t.PeriodStart)
                    .ThenBy(t => t.Id);

                var paging = Pagination.Resolve(request.Page, request.PageSize);
                IQueryable<Domain.Timesheet> pageQuery = ordered;
                if (paging is { } pg)
                    pageQuery = ordered.Skip((pg.Page - 1) * pg.Size).Take(pg.Size);

                var timesheets = await pageQuery.ToListAsync(cancellationToken);

                var items = timesheets.Select(t =>
                {
                    var weekStart = t.PeriodStart.Date;
                    var daily = new List<decimal> { 0m, 0m, 0m, 0m, 0m };
                    foreach (var entry in t.Entries)
                    {
                        var idx = (int)(entry.Date.Date - weekStart).TotalDays;
                        if (idx >= 0 && idx < 5)
                            daily[idx] += entry.HoursWorked;
                    }

                    return new TimesheetDto
                    {
                        Id = t.Id,
                        EmployeeId = t.EmployeeProfileId,
                        EmployeeName = t.Employee != null && t.Employee.User != null
                            ? (t.Employee.User.DisplayName ?? t.Employee.User.UserName ?? t.EmployeeProfileId)
                            : t.EmployeeProfileId,
                        DepartmentId = t.DepartmentId,
                        PeriodStart = t.PeriodStart,
                        PeriodEnd = t.PeriodEnd,
                        TotalHours = t.TotalHours,
                        Status = t.Status.ToString(),
                        SubmittedAt = t.SubmittedAt,
                        ApprovedAt = t.ApprovedAt,
                        CreatedAt = t.CreatedAt,
                        ProjectSummaries = t.Entries
                            .Where(e => e.Project != null)
                            .GroupBy(e => new { e.ProjectId, e.Project!.Code, e.Project.Name })
                            .Select(g => new TimesheetProjectSummaryDto
                            {
                                ProjectId = g.Key.ProjectId,
                                Code = g.Key.Code,
                                Name = g.Key.Name,
                                Hours = g.Sum(x => x.HoursWorked),
                            })
                            .OrderByDescending(p => p.Hours)
                            .ToList(),
                        DailyHours = daily,
                    };
                }).ToList();

                return new PagedResult<TimesheetDto>
                {
                    Items = items,
                    Total = total,
                    Page = paging?.Page,
                    PageSize = paging?.Size,
                };
            }
        }
    }
}
