using Application.Core;
using Application.Projects.DTOs;
using Application.Projects.Support;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Projects.Commands;

public class UpdateProject
{
    public class Command : IRequest<Result<ProjectDto>>
    {
        public int Id { get; set; }
        public required UpsertProjectRequest Project { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Command, Result<ProjectDto>>
    {
        public async Task<Result<ProjectDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var project = await context.Projects
                .Include(p => p.Department)
                .Include(p => p.Owner)
                .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken);

            if (project is null)
                return Result<ProjectDto>.Failure("Project not found.");

            var req = request.Project;
            var name = req.Name.Trim();
            var code = req.Code.Trim().ToUpperInvariant();

            if (await context.Projects.AnyAsync(p => p.Id != request.Id && p.Name.ToLower() == name.ToLower(), cancellationToken))
                return Result<ProjectDto>.Conflict("A project with that name already exists.");
            if (await context.Projects.AnyAsync(p => p.Id != request.Id && p.Code == code, cancellationToken))
                return Result<ProjectDto>.Conflict("A project with that code already exists.");

            if (req.DepartmentId.HasValue
                && !await context.Departments.AnyAsync(d => d.Id == req.DepartmentId, cancellationToken))
                return Result<ProjectDto>.Failure("Selected department does not exist.");

            if (!string.IsNullOrEmpty(req.OwnerId)
                && !await context.Users.AnyAsync(u => u.Id == req.OwnerId, cancellationToken))
                return Result<ProjectDto>.Failure("Selected owner does not exist.");

            var activityTypeIds = req.ActivityTypeIds.Distinct().ToList();
            if (activityTypeIds.Count > 0
                && await context.ProjectActivityTypes.CountAsync(a => activityTypeIds.Contains(a.Id), cancellationToken) != activityTypeIds.Count)
                return Result<ProjectDto>.Failure("One or more selected activity types do not exist.");

            project.Name = name;
            project.Code = code;
            project.Description = (req.Description ?? string.Empty).Trim();
            project.DepartmentId = req.DepartmentId;
            project.OwnerId = string.IsNullOrEmpty(req.OwnerId) ? null : req.OwnerId;
            project.Status = req.Status;
            project.IsActive = req.Status != ProjectStatus.Inactive;
            project.ColorKey = string.IsNullOrWhiteSpace(req.ColorKey) ? "p1" : req.ColorKey.Trim();
            project.TargetWeeklyHours = req.TargetWeeklyHours;
            project.TargetMonthlyHours = req.TargetMonthlyHours;

            // Diffed rather than cleared and re-added so an unchanged selection
            // produces no writes at all.
            var existingAssignments = await context.ProjectActivityAssignments
                .Where(a => a.ProjectId == project.Id)
                .ToListAsync(cancellationToken);

            context.ProjectActivityAssignments.RemoveRange(
                existingAssignments.Where(a => !activityTypeIds.Contains(a.ActivityTypeId)));

            context.ProjectActivityAssignments.AddRange(activityTypeIds
                .Where(id => existingAssignments.All(a => a.ActivityTypeId != id))
                .Select(id => new ProjectActivityAssignment { ProjectId = project.Id, ActivityTypeId = id }));

            await context.SaveChangesAsync(cancellationToken);

            await context.Entry(project).Reference(p => p.Department).LoadAsync(cancellationToken);
            await context.Entry(project).Reference(p => p.Owner).LoadAsync(cancellationToken);

            return Result<ProjectDto>.Success(new ProjectDto
            {
                Id = project.Id,
                Name = project.Name,
                Code = project.Code,
                Description = project.Description,
                IsActive = project.IsActive,
                Status = project.Status,
                DepartmentId = project.DepartmentId,
                DepartmentName = project.Department?.Name,
                OwnerId = project.OwnerId,
                OwnerName = project.Owner?.DisplayName,
                ColorKey = project.ColorKey,
                TargetWeeklyHours = project.TargetWeeklyHours,
                TargetMonthlyHours = project.TargetMonthlyHours,
                CreatedAt = project.CreatedAt,
                Activities = await ProjectActivityLookup.ForProjectAsync(context, project.Id, cancellationToken)
            });
        }
    }
}
