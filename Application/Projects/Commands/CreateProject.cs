using Application.Core;
using Application.Projects.DTOs;
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

            if (req.DepartmentId.HasValue
                && !await context.Departments.AnyAsync(d => d.Id == req.DepartmentId, cancellationToken))
                return Result<ProjectDto>.Failure("Selected department does not exist.");

            if (!string.IsNullOrEmpty(req.OwnerId)
                && !await context.Users.AnyAsync(u => u.Id == req.OwnerId, cancellationToken))
                return Result<ProjectDto>.Failure("Selected owner does not exist.");

            var project = new Project
            {
                Name = name,
                Code = code,
                Description = (req.Description ?? string.Empty).Trim(),
                DepartmentId = req.DepartmentId,
                OwnerId = string.IsNullOrEmpty(req.OwnerId) ? null : req.OwnerId,
                Status = req.Status,
                IsActive = req.Status != ProjectStatus.Inactive,
                ColorKey = string.IsNullOrWhiteSpace(req.ColorKey) ? "p1" : req.ColorKey.Trim(),
                TargetWeeklyHours = req.TargetWeeklyHours,
                TargetMonthlyHours = req.TargetMonthlyHours,
                CreatedAt = DateTime.UtcNow
            };

            context.Projects.Add(project);
            await context.SaveChangesAsync(cancellationToken);

            // Reload with includes for the response
            await context.Entry(project).Reference(p => p.Department).LoadAsync(cancellationToken);
            await context.Entry(project).Reference(p => p.Owner).LoadAsync(cancellationToken);

            return Result<ProjectDto>.Success(ToDto(project));
        }

        private static ProjectDto ToDto(Project p) => new()
        {
            Id = p.Id,
            Name = p.Name,
            Code = p.Code,
            Description = p.Description,
            IsActive = p.IsActive,
            Status = p.Status,
            DepartmentId = p.DepartmentId,
            DepartmentName = p.Department?.Name,
            OwnerId = p.OwnerId,
            OwnerName = p.Owner?.DisplayName,
            ColorKey = p.ColorKey,
            TargetWeeklyHours = p.TargetWeeklyHours,
            TargetMonthlyHours = p.TargetMonthlyHours,
            CreatedAt = p.CreatedAt
        };
    }
}
