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

            /// <summary>
            /// The HR Administrator also reaches a department-less timesheet — an
            /// administrator's own — which no department scope would otherwise include.
            /// </summary>
            public bool IsHrAdministrator { get; set; }
            public int? Page { get; set; }
            public int? PageSize { get; set; }

            /// <summary>
            /// Compare each weekday with attendance (<see cref="TimesheetAttendanceComparison"/>).
            /// Opt-in: the notification bell polls this list every 15 seconds and renders
            /// no chip, so it must not pay for loading everyone's attendance. Off, the
            /// DTO's comparison fields stay null and no comparison query runs.
            /// </summary>
            public bool IncludeAttendanceComparison { get; init; }

            /// <summary>Test seam for the clock; the controller leaves it null.</summary>
            public DateTime? NowUtc { get; init; }
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
                    cancellationToken,
                    isHrAdministrator: request.IsHrAdministrator);

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

                var now = request.NowUtc ?? DateTime.UtcNow;

                // Each weekday against the time attendance recorded for it. Flag only:
                // the reviewer sees the difference, nothing is blocked.
                var comparison = request.IncludeAttendanceComparison
                    ? await TimesheetAttendanceComparison.LoadAsync(_context, timesheets, now, cancellationToken)
                    : TimesheetAttendanceComparison.None;

                // Which open timesheets a manager is available to review today. The HR
                // pages leave those rows out: the manager stage is the manager's.
                var openSubmitters = timesheets
                    .Where(t => TimesheetReviewRule.IsOpen(t.Status) && t.Employee != null)
                    .Select(t => t.Employee!)
                    .ToList();
                var managerAvailable = openSubmitters.Count > 0
                    ? await TimesheetReviewRule.ManagerAvailableAsync(_context, openSubmitters, now, cancellationToken)
                    : new Dictionary<string, bool>();

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

                    var week = comparison.For(t.Id);

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
                        AwaitingManager = TimesheetReviewRule.IsOpen(t.Status)
                            && managerAvailable.TryGetValue(t.EmployeeProfileId, out var available)
                            && available,
                        AttendanceMinutes = week?.AttendanceMinutes,
                        DayMismatchMinutes = week?.MismatchMinutes,
                        OnLeaveDays = week?.OnLeave,
                        MismatchDayCount = week?.MismatchDayCount ?? 0,
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
