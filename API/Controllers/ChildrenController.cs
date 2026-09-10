using Application.Children.Commands;
using Application.Children.DTOs;
using Application.Children.Queries;
using Asp.Versioning;
using Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace API.Controllers;

/// <summary>
/// An employee's declared children, which is what a per-child leave entitlement is
/// measured against. Every action is authorized inside the handler as well, against
/// the caller's own profile — the role attributes here only say who may reach the
/// endpoint at all, not whose children they may touch.
/// </summary>
[ApiVersion("1.0")]
[Authorize]
public class ChildrenController : BaseApiController
{
    private string CallerUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    private bool IsAdmin => User.IsInRole(AppRoles.Admin);
    private bool IsManager => User.IsInRole(AppRoles.Manager);

    [HttpGet]
    public async Task<ActionResult<List<ChildDto>>> GetChildren([FromQuery] string? employeeId)
    {
        var result = await Mediator.Send(new GetChildList.Query
        {
            EmployeeId = employeeId,
            CallerUserId = CallerUserId,
            IsAdmin = IsAdmin,
            IsManager = IsManager,
        });
        return HandleResult(result);
    }

    [HttpPost]
    public async Task<ActionResult<ChildDto>> CreateChild(
        UpsertChildRequest request, [FromQuery] string? employeeId)
    {
        var result = await Mediator.Send(new CreateChild.Command
        {
            EmployeeId = employeeId,
            Child = request,
            CallerUserId = CallerUserId,
            IsAdmin = IsAdmin,
            IsManager = IsManager,
        });
        return HandleResult(result);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ChildDto>> UpdateChild(string id, UpsertChildRequest request)
    {
        var result = await Mediator.Send(new UpdateChild.Command
        {
            Id = id,
            Child = request,
            CallerUserId = CallerUserId,
            IsAdmin = IsAdmin,
            IsManager = IsManager,
        });
        return HandleResult(result);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteChild(string id)
    {
        var result = await Mediator.Send(new DeleteChild.Command
        {
            Id = id,
            CallerUserId = CallerUserId,
            IsAdmin = IsAdmin,
            IsManager = IsManager,
        });
        return HandleResult(result);
    }
}
