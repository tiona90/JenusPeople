using Application.Departments.Commands;
using Application.Departments.DTOs;
using Application.Departments.Queries;
using Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;

namespace API.Controllers;

[ApiVersion("1.0")]

public class DepartmentsController : BaseApiController
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<List<DepartmentDto>>> GetDepartments()
    {
        return await Mediator.Send(new GetDepartmentList.Query());
    }

    [HttpPost]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<ActionResult<DepartmentDto>> CreateDepartment(UpsertDepartmentRequest request)
    {
        var result = await Mediator.Send(new CreateDepartment.Command { Department = request });
        return HandleResult(result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<ActionResult<DepartmentDto>> UpdateDepartment(int id, UpsertDepartmentRequest request)
    {
        var result = await Mediator.Send(new UpdateDepartment.Command { Id = id, Department = request });
        return HandleResult(result);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<ActionResult> DeleteDepartment(int id)
    {
        var result = await Mediator.Send(new DeleteDepartment.Command { Id = id });
        return HandleResult(result);
    }
}
