using Application.ProjectActivityTypes.DTOs;
using Application.ProjectActivityTypes.Commands;
using Application.ProjectActivityTypes.Queries;
using Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;

namespace API.Controllers;

[ApiVersion("1.0")]
public class ProjectActivityTypesController : BaseApiController
{
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<ProjectActivityTypeDto>>> GetProjectActivityTypes()
    {
        return await Mediator.Send(new GetProjectActivityTypeList.Query());
    }

    [HttpPost]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<ActionResult<ProjectActivityTypeDto>> CreateProjectActivityType(UpsertProjectActivityTypeRequest request)
    {
        var result = await Mediator.Send(new CreateProjectActivityType.Command { ActivityType = request });
        return HandleResult(result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<ActionResult<ProjectActivityTypeDto>> UpdateProjectActivityType(int id, UpsertProjectActivityTypeRequest request)
    {
        var result = await Mediator.Send(new UpdateProjectActivityType.Command { Id = id, ActivityType = request });
        return HandleResult(result);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<ActionResult> DeleteProjectActivityType(int id)
    {
        var result = await Mediator.Send(new DeleteProjectActivityType.Command { Id = id });
        return HandleResult(result);
    }
}
