using Application.EmployeeProfiles.Commands;
using Application.EmployeeProfiles.DTOs;
using Application.EmployeeProfiles.Queries;
using Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Asp.Versioning;

namespace API.Controllers;

[ApiVersion("1.0")]

public class EmployeeProfilesController : BaseApiController
{
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<EmployeeProfileDto>>> GetEmployeeProfiles()
    {
        return await Mediator.Send(new GetEmployeeProfileList.Query
        {
            RequestingUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            IsAdmin = User.IsInRole(AppRoles.Admin),
            IsManager = User.IsInRole(AppRoles.Manager),
        });
    }

    [HttpPut]
    [Authorize(Policy = "EmployeeProfileUpdate")]
    public async Task<ActionResult> EditEmployeeProfile(EditEmployeeProfileRequest request)
    {
        await Mediator.Send(new EditEmployeeProfile.Command
        {
            EmployeeProfile = request
        });

        return NoContent();
    }
}
