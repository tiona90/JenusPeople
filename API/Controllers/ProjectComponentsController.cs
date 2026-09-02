using Application.ProjectComponents.DTOs;
using Application.ProjectComponents.Commands;
using Application.ProjectComponents.Queries;
using Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;

namespace API.Controllers;

[ApiVersion("1.0")]
public class ProjectComponentsController : BaseApiController
{
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<ProjectComponentDto>>> GetProjectComponents()
    {
        return await Mediator.Send(new GetProjectComponentList.Query());
    }

    [HttpPost]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<ActionResult<ProjectComponentDto>> CreateProjectComponent(UpsertProjectComponentRequest request)
    {
        var result = await Mediator.Send(new CreateProjectComponent.Command { Component = request });
        return HandleResult(result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<ActionResult<ProjectComponentDto>> UpdateProjectComponent(int id, UpsertProjectComponentRequest request)
    {
        var result = await Mediator.Send(new UpdateProjectComponent.Command { Id = id, Component = request });
        return HandleResult(result);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<ActionResult> DeleteProjectComponent(int id)
    {
        var result = await Mediator.Send(new DeleteProjectComponent.Command { Id = id });
        return HandleResult(result);
    }
}
