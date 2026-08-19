using API.Models;
using Application.AdminUsers.Commands;
using Application.AdminUsers.DTOs;
using Application.AdminUsers.Queries;
using Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Asp.Versioning;

namespace API.Controllers;

[Authorize(Roles = AppRoles.Admin)]
[ApiVersion("1.0")]
public class AdminUsersController : BaseApiController
{
    [HttpGet]
    [ProducesResponseType(typeof(List<AdminUserDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<AdminUserDto>>> GetUsers()
    {
        return Ok(await Mediator.Send(new GetAdminUserList.Query(), HttpContext.RequestAborted));
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(AdminUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminUserDto>> GetUser(string id)
    {
        return HandleResult(await Mediator.Send(
            new GetAdminUserDetail.Query { Id = id },
            HttpContext.RequestAborted));
    }

    [HttpPost]
    [ProducesResponseType(typeof(AdminUserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminUserDto>> CreateUser(AdminCreateUserDto request)
    {
        var result = await Mediator.Send(
            new CreateAdminUser.Command { User = request },
            HttpContext.RequestAborted);

        // Not HandleResult on the success path: that answers 200, and this action
        // has always answered 201 with a Location header. The frontend only reads
        // the body, but a status change is not this migration's to make.
        if (result.IsSuccess && result.Value is not null)
        {
            return CreatedAtAction(nameof(GetUser), new { id = result.Value.Id }, result.Value);
        }

        return HandleResult(result);
    }

    [HttpPut("{id}")]
    [ProducesResponseType(typeof(AdminUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminUserDto>> UpdateUser(string id, AdminUpdateUserDto request)
    {
        return HandleResult(await Mediator.Send(
            new UpdateAdminUser.Command { Id = id, User = request },
            HttpContext.RequestAborted));
    }

    [HttpPut("{id}/roles")]
    [ProducesResponseType(typeof(AdminUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminUserDto>> SetUserRoles(string id, AdminSetUserRolesDto request)
    {
        return HandleResult(await Mediator.Send(
            new SetAdminUserRoles.Command { Id = id, Roles = request },
            HttpContext.RequestAborted));
    }

    [HttpPost("{id}/confirm-email")]
    [ProducesResponseType(typeof(AdminUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminUserDto>> ConfirmUserEmail(string id)
    {
        return HandleResult(await Mediator.Send(
            new ConfirmAdminUserEmail.Command { Id = id },
            HttpContext.RequestAborted));
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult> DeleteUser(string id)
    {
        var result = await Mediator.Send(
            new DeleteAdminUser.Command
            {
                Id = id,
                RequestingUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            },
            HttpContext.RequestAborted);

        // 204, as this action has always answered. HandleResult would return 200
        // with a Unit body — which is what the other MediatR deletes do, but
        // changing it here is not part of moving the logic.
        return result.IsSuccess ? NoContent() : HandleResult(result);
    }

}
