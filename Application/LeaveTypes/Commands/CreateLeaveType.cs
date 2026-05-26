using Application.Core;
using Application.LeaveTypes.DTOs;
using AutoMapper;
using Domain;
using MediatR;
using Persistence;

namespace Application.LeaveTypes.Commands;

public class CreateLeaveType
{
    public class Command : IRequest<Result<LeaveTypeDto>>
    {
        public required UpsertLeaveTypeRequest LeaveType { get; set; }
    }

    public class Handler(AppDbContext context, IMapper mapper) : IRequestHandler<Command, Result<LeaveTypeDto>>
    {
        public async Task<Result<LeaveTypeDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            var leaveType = mapper.Map<LeaveType>(request.LeaveType);

            context.LeaveTypes.Add(leaveType);
            await context.SaveChangesAsync(cancellationToken);

            return Result<LeaveTypeDto>.Success(mapper.Map<LeaveTypeDto>(leaveType));
        }
    }
}
