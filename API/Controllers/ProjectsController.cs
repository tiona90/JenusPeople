using Application.Projects.Commands;
using Application.Projects.DTOs;
using Application.Projects.Queries;
using Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;

namespace API.Controllers;

[ApiVersion("1.0")]

public class ProjectsController : BaseApiController
{
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<ProjectDto>>> GetProjects()
    {
        return await Mediator.Send(new GetProjectList.Query());
    }

    [HttpPost]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<ActionResult<ProjectDto>> CreateProject(UpsertProjectRequest request)
    {
        var result = await Mediator.Send(new CreateProject.Command { Project = request });
        return HandleResult(result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<ActionResult<ProjectDto>> UpdateProject(int id, UpsertProjectRequest request)
    {
        var result = await Mediator.Send(new UpdateProject.Command { Id = id, Project = request });
        return HandleResult(result);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<ActionResult> DeleteProject(int id)
    {
        var result = await Mediator.Send(new DeleteProject.Command { Id = id });
        return HandleResult(result);
    }
}
