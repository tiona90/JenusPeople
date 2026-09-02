using Application.ProjectTypes.DTOs;
using Application.ProjectTypes.Commands;
using Application.ProjectTypes.Queries;
using Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;

namespace API.Controllers;

[ApiVersion("1.0")]
public class ProjectTypesController : BaseApiController
{
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<ProjectTypeDto>>> GetProjectTypes()
    {
        return await Mediator.Send(new GetProjectTypeList.Query());
    }

    [HttpPost]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<ActionResult<ProjectTypeDto>> CreateProjectType(UpsertProjectTypeRequest request)
    {
        var result = await Mediator.Send(new CreateProjectType.Command { Type = request });
        return HandleResult(result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<ActionResult<ProjectTypeDto>> UpdateProjectType(int id, UpsertProjectTypeRequest request)
    {
        var result = await Mediator.Send(new UpdateProjectType.Command { Id = id, Type = request });
        return HandleResult(result);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<ActionResult> DeleteProjectType(int id)
    {
        var result = await Mediator.Send(new DeleteProjectType.Command { Id = id });
        return HandleResult(result);
    }
}
