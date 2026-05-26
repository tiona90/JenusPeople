using System;
using Application.Core;
using Application.AnnualLeaves.DTOs;
using AutoMapper;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.AnnualLeaves.Queries;

public class GetAnnualLeaveDetails
{
    public class Query : IRequest<Result<AnnualLeaveDto>>
    {
        public required string Id { get; set; }
        public string RequestingUserId { get; set; } = string.Empty;
        public bool IsAdmin { get; set; }
        public bool IsManager { get; set; }
        public bool IsEmployee { get; set; }
    }
    public class Handler(AppDbContext context, IMapper mapper) : IRequestHandler<Query, Result<AnnualLeaveDto>>
    {
        public async Task<Result<AnnualLeaveDto>> Handle(Query request, CancellationToken cancellationToken)
        {
            IQueryable<Domain.AnnualLeave> annualLeaveQuery = context.AnnualLeaves
                .AsNoTracking()
                .Where(al => al.Id == request.Id);

            if (request.IsAdmin)
            {
                // Admin sees everything.
            }
            else if (request.IsManager)
            {
                var managerScope = await ManagerAccessScopeResolver.ResolveAsync(
                    context,
                    request.RequestingUserId,
                    cancellationToken);

                annualLeaveQuery = annualLeaveQuery
                    .Where(al =>
                        ((al.DepartmentId.HasValue && managerScope.ManagedDepartmentIds.Contains(al.DepartmentId.Value))
                         || managerScope.DirectReportUserIds.Contains(al.EmployeeId))
                        && (al.Employee == null || !al.Employee.UserRoles.Any(ur => ur.Role != null && ur.Role.Name == AppRoles.Admin)));
            }
            else if (request.IsEmployee)
            {
                annualLeaveQuery = annualLeaveQuery.Where(al => al.EmployeeId == request.RequestingUserId);
            }
            else
            {
                annualLeaveQuery = annualLeaveQuery.Where(_ => false);
            }

            var annualLeave = await annualLeaveQuery.FirstOrDefaultAsync(cancellationToken);
            if (annualLeave == null) return Result<AnnualLeaveDto>.Failure("Annual leave not found");

            var annualLeaveDto = mapper.Map<AnnualLeaveDto>(annualLeave);
            return Result<AnnualLeaveDto>.Success(annualLeaveDto);
        }
    }
}
