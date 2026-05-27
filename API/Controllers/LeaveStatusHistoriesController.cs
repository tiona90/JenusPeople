using Application.LeaveStatusHistories.DTOs;
using Application.LeaveStatusHistories.Queries;
using Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Asp.Versioning;

namespace API.Controllers;

[ApiVersion("1.0")]

public class LeaveStatusHistoriesController : BaseApiController
{
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<LeaveStatusHistoryDto>>> GetLeaveStatusHistories()
    {
        return await Mediator.Send(new GetLeaveStatusHistoryList.Query
        {
            RequestingUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            IsAdmin = User.IsInRole(AppRoles.Admin),
            IsManager = User.IsInRole(AppRoles.Manager),
        });
    }
}
