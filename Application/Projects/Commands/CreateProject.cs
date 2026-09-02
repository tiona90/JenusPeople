using Application.Core;
using Application.Projects.DTOs;
using Application.Projects.Support;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Projects.Commands;

public class CreateProject
{
    public class Command : IRequest<Result<ProjectDto>>
    {
        public required UpsertProjectRequest Project { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Command, Result<ProjectDto>>
    {
        public async Task<Result<ProjectDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var req = request.Project;
            var name = req.Name.Trim();
            var code = req.Code.Trim().ToUpperInvariant();

            if (await context.Projects.AnyAsync(p => p.Name.ToLower() == name.ToLower(), cancellationToken))
                return Result<ProjectDto>.Conflict("A project with that name already exists.");
            if (await context.Projects.AnyAsync(p => p.Code == code, cancellationToken))
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

            var project = new Project
            {
                Name = name,
                Code = code,
                Description = (req.Description ?? string.Empty).Trim(),
                OwnerId = string.IsNullOrEmpty(req.OwnerId) ? null : req.OwnerId,
                Status = req.Status,
                IsActive = req.Status != ProjectStatus.Inactive,
                ColorKey = string.IsNullOrWhiteSpace(req.ColorKey) ? "p1" : req.ColorKey.Trim(),
                TargetWeeklyHours = req.TargetWeeklyHours,
                TargetMonthlyHours = req.TargetMonthlyHours,
                CreatedAt = DateTime.UtcNow
            };

            foreach (var departmentId in departmentIds)
                project.DepartmentAssignments.Add(new ProjectDepartment { DepartmentId = departmentId });

            foreach (var activityTypeId in activityTypeIds)
                project.ActivityAssignments.Add(new ProjectActivityAssignment { ActivityTypeId = activityTypeId });

            foreach (var componentId in componentIds)
                project.ComponentAssignments.Add(new ProjectComponentAssignment { ComponentId = componentId });

            foreach (var projectTypeId in projectTypeIds)
                project.TypeAssignments.Add(new ProjectTypeAssignment { ProjectTypeId = projectTypeId });

            context.Projects.Add(project);
            await context.SaveChangesAsync(cancellationToken);

            // Reload with includes for the response
            await context.Entry(project).Reference(p => p.Owner).LoadAsync(cancellationToken);

            var dto = ToDto(project);
            dto.Departments = await ProjectDepartmentLookup.ForProjectAsync(context, project.Id, cancellationToken);
            dto.Activities = await ProjectActivityLookup.ForProjectAsync(context, project.Id, cancellationToken);
            dto.Components = await ProjectComponentLookup.ForProjectAsync(context, project.Id, cancellationToken);
            dto.Types = await ProjectTypeLookup.ForProjectAsync(context, project.Id, cancellationToken);
            return Result<ProjectDto>.Success(dto);
        }

        private static ProjectDto ToDto(Project p) => new()
        {
            Id = p.Id,
            Name = p.Name,
            Code = p.Code,
            Description = p.Description,
            IsActive = p.IsActive,
            Status = p.Status,
            OwnerId = p.OwnerId,
            OwnerName = p.Owner?.DisplayName,
            ColorKey = p.ColorKey,
            TargetWeeklyHours = p.TargetWeeklyHours,
            TargetMonthlyHours = p.TargetMonthlyHours,
            CreatedAt = p.CreatedAt
        };
    }
}
