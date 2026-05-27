using Application.LeaveTypes.DTOs;
using Application.LeaveTypes.Commands;
using Application.LeaveTypes.Queries;
using Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;

namespace API.Controllers;

[ApiVersion("1.0")]

public class LeaveTypesController : BaseApiController
{
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<LeaveTypeDto>>> GetLeaveTypes()
    {
        return await Mediator.Send(new GetLeaveTypeList.Query());
    }

    [HttpPost]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<ActionResult<LeaveTypeDto>> CreateLeaveType(UpsertLeaveTypeRequest request)
    {
        var result = await Mediator.Send(new CreateLeaveType.Command { LeaveType = request });
        return HandleResult(result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<ActionResult<LeaveTypeDto>> UpdateLeaveType(int id, UpsertLeaveTypeRequest request)
    {
        var result = await Mediator.Send(new UpdateLeaveType.Command { Id = id, LeaveType = request });
        return HandleResult(result);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<ActionResult> DeleteLeaveType(int id)
    {
        var result = await Mediator.Send(new DeleteLeaveType.Command { Id = id });
        return HandleResult(result);
    }
}
