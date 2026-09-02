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

            var departmentIds = req.DepartmentIds.Distinct().ToList();
            if (await context.Departments.CountAsync(d => departmentIds.Contains(d.Id), cancellationToken) != departmentIds.Count)
                return Result<ProjectDto>.Failure("One or more selected departments do not exist.");

            if (!string.IsNullOrEmpty(req.OwnerId)
                && !await context.Users.AnyAsync(u => u.Id == req.OwnerId, cancellationToken))
                return Result<ProjectDto>.Failure("Selected owner does not exist.");

            var activityTypeIds = req.ActivityTypeIds.Distinct().ToList();
            if (activityTypeIds.Count > 0
                && await context.ProjectActivityTypes.CountAsync(a => activityTypeIds.Contains(a.Id), cancellationToken) != activityTypeIds.Count)
                return Result<ProjectDto>.Failure("One or more selected activity types do not exist.");

            var componentIds = req.ComponentIds.Distinct().ToList();
            if (componentIds.Count > 0
                && await context.ProjectComponents.CountAsync(c => componentIds.Contains(c.Id), cancellationToken) != componentIds.Count)
                return Result<ProjectDto>.Failure("One or more selected components do not exist.");

            var projectTypeIds = req.ProjectTypeIds.Distinct().ToList();
            if (projectTypeIds.Count > 0
                && await context.ProjectTypes.CountAsync(t => projectTypeIds.Contains(t.Id), cancellationToken) != projectTypeIds.Count)
                return Result<ProjectDto>.Failure("One or more selected project types do not exist.");

            project.Name = name;
            project.Code = code;
            project.Description = (req.Description ?? string.Empty).Trim();
            project.OwnerId = string.IsNullOrEmpty(req.OwnerId) ? null : req.OwnerId;
            project.Status = req.Status;
            project.IsActive = req.Status != ProjectStatus.Inactive;
            project.ColorKey = string.IsNullOrWhiteSpace(req.ColorKey) ? "p1" : req.ColorKey.Trim();
            project.TargetWeeklyHours = req.TargetWeeklyHours;
            project.TargetMonthlyHours = req.TargetMonthlyHours;

            // Every set is diffed rather than cleared and re-added, so an
            // unchanged selection produces no writes at all.
            var existingDepartments = await context.ProjectDepartments
                .Where(a => a.ProjectId == project.Id)
                .ToListAsync(cancellationToken);

            context.ProjectDepartments.RemoveRange(
                existingDepartments.Where(a => !departmentIds.Contains(a.DepartmentId)));

            context.ProjectDepartments.AddRange(departmentIds
                .Where(id => existingDepartments.All(a => a.DepartmentId != id))
                .Select(id => new ProjectDepartment { ProjectId = project.Id, DepartmentId = id }));

            var existingAssignments = await context.ProjectActivityAssignments
                .Where(a => a.ProjectId == project.Id)
                .ToListAsync(cancellationToken);

            context.ProjectActivityAssignments.RemoveRange(
                existingAssignments.Where(a => !activityTypeIds.Contains(a.ActivityTypeId)));

            context.ProjectActivityAssignments.AddRange(activityTypeIds
                .Where(id => existingAssignments.All(a => a.ActivityTypeId != id))
                .Select(id => new ProjectActivityAssignment { ProjectId = project.Id, ActivityTypeId = id }));

            var existingComponents = await context.ProjectComponentAssignments
                .Where(a => a.ProjectId == project.Id)
                .ToListAsync(cancellationToken);

            context.ProjectComponentAssignments.RemoveRange(
                existingComponents.Where(a => !componentIds.Contains(a.ComponentId)));

            context.ProjectComponentAssignments.AddRange(componentIds
                .Where(id => existingComponents.All(a => a.ComponentId != id))
                .Select(id => new ProjectComponentAssignment { ProjectId = project.Id, ComponentId = id }));

            // Diffed like every other set, so clearing the select really does
            // unclassify the project rather than leaving the old types in place.
            var existingTypes = await context.ProjectTypeAssignments
                .Where(a => a.ProjectId == project.Id)
                .ToListAsync(cancellationToken);

            context.ProjectTypeAssignments.RemoveRange(
                existingTypes.Where(a => !projectTypeIds.Contains(a.ProjectTypeId)));

            context.ProjectTypeAssignments.AddRange(projectTypeIds
                .Where(id => existingTypes.All(a => a.ProjectTypeId != id))
                .Select(id => new ProjectTypeAssignment { ProjectId = project.Id, ProjectTypeId = id }));

            await context.SaveChangesAsync(cancellationToken);

            await context.Entry(project).Reference(p => p.Owner).LoadAsync(cancellationToken);

            return Result<ProjectDto>.Success(new ProjectDto
            {
                Id = project.Id,
                Name = project.Name,
                Code = project.Code,
                Description = project.Description,
                IsActive = project.IsActive,
                Status = project.Status,
                Departments = await ProjectDepartmentLookup.ForProjectAsync(context, project.Id, cancellationToken),
                OwnerId = project.OwnerId,
                OwnerName = project.Owner?.DisplayName,
                ColorKey = project.ColorKey,
                TargetWeeklyHours = project.TargetWeeklyHours,
                TargetMonthlyHours = project.TargetMonthlyHours,
                CreatedAt = project.CreatedAt,
                Activities = await ProjectActivityLookup.ForProjectAsync(context, project.Id, cancellationToken),
                Components = await ProjectComponentLookup.ForProjectAsync(context, project.Id, cancellationToken),
                Types = await ProjectTypeLookup.ForProjectAsync(context, project.Id, cancellationToken)
            });
        }
    }
}
