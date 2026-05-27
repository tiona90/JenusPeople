using Application.TimesheetStatusHistories.DTOs;
using Application.TimesheetStatusHistories.Queries;
using Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Asp.Versioning;

namespace API.Controllers;

[ApiVersion("1.0")]

public class TimesheetStatusHistoriesController : BaseApiController
{
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<TimesheetStatusHistoryDto>>> GetTimesheetStatusHistories()
    {
        return await Mediator.Send(new GetTimesheetStatusHistoryList.Query
        {
            RequestingUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
            IsAdmin = User.IsInRole(AppRoles.Admin),
            IsManager = User.IsInRole(AppRoles.Manager),
        });
    }
}
