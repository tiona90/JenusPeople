using Application.Projects.DTOs;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Projects.Queries;

public class GetProjectList
{
    public class Query : IRequest<List<ProjectDto>>
    {
    }

    public class Handler(AppDbContext context) : IRequestHandler<Query, List<ProjectDto>>
    {
        public async Task<List<ProjectDto>> Handle(Query request, CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow.Date;
            // Monday-anchored week
            var dow = (int)now.DayOfWeek;
            var daysFromMonday = dow == 0 ? 6 : dow - 1;
            var weekStart = now.AddDays(-daysFromMonday);
            var weekEnd = weekStart.AddDays(7);
            var monthStart = new DateTime(now.Year, now.Month, 1);
            var yearStart = new DateTime(now.Year, 1, 1);

            var projects = await context.Projects
                .AsNoTracking()
                .Include(p => p.Department)
                .Include(p => p.Owner)
                .OrderBy(p => p.Name)
                .ToListAsync(cancellationToken);

            if (projects.Count == 0) return new List<ProjectDto>();

            var projectIds = projects.Select(p => p.Id).ToList();

            var activityAssignments = await context.ProjectActivityAssignments
                .AsNoTracking()
                .Where(a => projectIds.Contains(a.ProjectId))
                .OrderBy(a => a.ActivityType!.Name)
                .Select(a => new
                {
                    a.ProjectId,
                    Activity = new ProjectActivityDto
                    {
                        Id = a.ActivityTypeId,
                        Name = a.ActivityType!.Name,
                        Icon = a.ActivityType.Icon,
                        ColorKey = a.ActivityType.ColorKey,
                    }
                })
                .ToListAsync(cancellationToken);

            var ytdEntries = await context.TimesheetEntries
                .AsNoTracking()
                .Where(e => projectIds.Contains(e.ProjectId) && e.Date >= yearStart)
                .Select(e => new
                {
                    e.ProjectId,
                    e.Date,
                    e.HoursWorked,
                    UserId = e.Timesheet!.Employee!.UserId,
                    DisplayName = e.Timesheet!.Employee!.User!.DisplayName
                })
                .ToListAsync(cancellationToken);

            var result = new List<ProjectDto>(projects.Count);

            foreach (var p in projects)
            {
                var entries = ytdEntries.Where(e => e.ProjectId == p.Id).ToList();
                var weekly = entries.Where(e => e.Date >= weekStart && e.Date < weekEnd).Sum(e => e.HoursWorked);
                var monthly = entries.Where(e => e.Date >= monthStart).Sum(e => e.HoursWorked);
                var ytd = entries.Sum(e => e.HoursWorked);

                var team = entries
                    .Where(e => !string.IsNullOrEmpty(e.UserId))
                    .GroupBy(e => new { e.UserId, e.DisplayName })
                    .Select(g => new ProjectTeamMemberDto
                    {
                        UserId = g.Key.UserId,
                        DisplayName = g.Key.DisplayName ?? string.Empty,
                        HoursThisWeek = g.Where(x => x.Date >= weekStart && x.Date < weekEnd).Sum(x => x.HoursWorked)
                    })
                    .OrderByDescending(m => m.HoursThisWeek)
                    .ThenBy(m => m.DisplayName)
                    .ToList();

                result.Add(new ProjectDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Code = p.Code,
                    Description = p.Description ?? string.Empty,
                    IsActive = p.IsActive,
                    Status = p.Status,
                    DepartmentId = p.DepartmentId,
                    DepartmentName = p.Department?.Name,
                    OwnerId = p.OwnerId,
                    OwnerName = p.Owner?.DisplayName,
                    ColorKey = string.IsNullOrEmpty(p.ColorKey) ? "p1" : p.ColorKey,
                    TargetWeeklyHours = p.TargetWeeklyHours,
                    TargetMonthlyHours = p.TargetMonthlyHours,
                    CreatedAt = p.CreatedAt,
                    HoursThisWeek = weekly,
                    HoursThisMonth = monthly,
                    HoursYTD = ytd,
                    TeamSize = team.Count,
                    Team = team,
                    Activities = activityAssignments.Where(a => a.ProjectId == p.Id).Select(a => a.Activity).ToList()
                });
            }

            return result;
        }
    }
}
