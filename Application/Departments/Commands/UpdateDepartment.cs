using Application.Core;
using Application.Departments.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Departments.Commands;

public class UpdateDepartment
{
    public class Command : IRequest<Result<DepartmentDto>>
    {
        public int Id { get; set; }
        public required UpsertDepartmentRequest Department { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Command, Result<DepartmentDto>>
    {
        public async Task<Result<DepartmentDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var department = await context.Departments.FindAsync([request.Id], cancellationToken);
            if (department is null)
                return Result<DepartmentDto>.Failure("Department not found.");

            if (context.Departments.Any(d => d.Code == request.Department.Code && d.Id != request.Id))
                return Result<DepartmentDto>.Conflict("A department with that code already exists.");

            department.Name = request.Department.Name;
            department.Code = request.Department.Code.ToUpperInvariant();
            department.IsActive = request.Department.IsActive;

            await context.SaveChangesAsync(cancellationToken);

            return Result<DepartmentDto>.Success(new DepartmentDto
            {
                Id = department.Id,
                Name = department.Name,
                Code = department.Code,
                IsActive = department.IsActive,
                CreatedAt = department.CreatedAt
            });
        }
    }
}
