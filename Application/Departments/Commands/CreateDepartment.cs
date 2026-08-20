using Application.Core;
using Application.Departments.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Departments.Commands;

public class CreateDepartment
{
    public class Command : IRequest<Result<DepartmentDto>>
    {
        public required UpsertDepartmentRequest Department { get; set; }
    }

    public class Handler(AppDbContext context) : IRequestHandler<Command, Result<DepartmentDto>>
    {
        public async Task<Result<DepartmentDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            if (await context.Departments.AnyAsync(d => d.Code == request.Department.Code, cancellationToken))
                return Result<DepartmentDto>.Conflict("A department with that code already exists.");

            var department = new Domain.Department
            {
                Name = request.Department.Name,
                Code = request.Department.Code.ToUpperInvariant(),
                IsActive = request.Department.IsActive,
                CreatedAt = DateTime.UtcNow
            };

            context.Departments.Add(department);
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
