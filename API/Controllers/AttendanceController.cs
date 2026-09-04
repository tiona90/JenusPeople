using System.Security.Claims;
using Application.Attendance.Commands;
using Application.Attendance.DTOs;
using Application.Attendance.Queries;
using Asp.Versioning;
using Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// Attendance: personal check-in/out and break actions, the manager team board,
/// and the admin presence and company views.
///
/// Thin by design. The day-state rules live in
/// <see cref="Domain.Services.AttendanceDayStateCalculator"/> and the loading and
/// shaping in <c>Application.Attendance</c>, because none of it was reachable from
/// a test while it sat here behind a private method and a DbContext.
/// </summary>
[ApiVersion("1.0")]
[Authorize]
public class AttendanceController : BaseApiController
{
    private string ResolveUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue("sub")
        ?? User.Identity?.Name
        ?? string.Empty;

    private bool IsAdmin => User.IsInRole(AppRoles.Admin);

    // GET: api/attendance/me/today
    [HttpGet("me/today")]
    [ProducesResponseType(typeof(TodayStateDto), StatusCodes.Status200OK)]
    public async Task<ActionResult> GetToday(CancellationToken cancellationToken) =>
        HandleResult(await Mediator.Send(
            new GetTodayState.Query { RequestingUserId = ResolveUserId() },
            cancellationToken));

    // POST: api/attendance/check-in
    [HttpPost("check-in")]
    [ProducesResponseType(typeof(TodayStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> CheckIn(CancellationToken cancellationToken) =>
        HandleResult(await Mediator.Send(
            new CheckIn.Command { RequestingUserId = ResolveUserId() },
            cancellationToken));

    // POST: api/attendance/check-out
    [HttpPost("check-out")]
    [ProducesResponseType(typeof(TodayStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> CheckOut(CancellationToken cancellationToken) =>
        HandleResult(await Mediator.Send(
            new CheckOut.Command { RequestingUserId = ResolveUserId() },
            cancellationToken));

    // POST: api/attendance/break/start
    [HttpPost("break/start")]
    [ProducesResponseType(typeof(TodayStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> StartBreak(CancellationToken cancellationToken) =>
        HandleResult(await Mediator.Send(
            new StartBreak.Command { RequestingUserId = ResolveUserId() },
            cancellationToken));

    // POST: api/attendance/break/end
    [HttpPost("break/end")]
    [ProducesResponseType(typeof(TodayStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> EndBreak(CancellationToken cancellationToken) =>
        HandleResult(await Mediator.Send(
            new EndBreak.Command { RequestingUserId = ResolveUserId() },
            cancellationToken));

    // POST: api/attendance/break/auto-start — client-side idle detection reporting 5+ minutes of inactivity.
    [HttpPost("break/auto-start")]
    [ProducesResponseType(typeof(TodayStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> AutoStartBreak(CancellationToken cancellationToken) =>
        HandleResult(await Mediator.Send(
            new StartBreak.Command { RequestingUserId = ResolveUserId(), IsAutomatic = true },
            cancellationToken));

    // POST: api/attendance/break/auto-end — client-side idle detection reporting activity resumed.
    [HttpPost("break/auto-end")]
    [ProducesResponseType(typeof(TodayStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> AutoEndBreak(CancellationToken cancellationToken) =>
        HandleResult(await Mediator.Send(
            new EndBreak.Command { RequestingUserId = ResolveUserId(), IsAutomatic = true },
            cancellationToken));

    // GET: api/attendance/me/history
    [HttpGet("me/history")]
    [ProducesResponseType(typeof(List<DayHistoryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult> GetMyHistory(
        [FromQuery] int days = GetMyAttendanceHistory.DefaultDays,
        CancellationToken cancellationToken = default) =>
        HandleResult(await Mediator.Send(
            new GetMyAttendanceHistory.Query { RequestingUserId = ResolveUserId(), Days = days },
            cancellationToken));

    // GET: api/attendance/team
    [HttpGet("team")]
    [Authorize(Roles = AppRoles.Admin + "," + AppRoles.Manager)]
    [ProducesResponseType(typeof(TeamAttendanceDto), StatusCodes.Status200OK)]
    public async Task<ActionResult> GetTeam(CancellationToken cancellationToken) =>
        HandleResult(await Mediator.Send(
            new GetTeamAttendance.Query { RequestingUserId = ResolveUserId(), IsAdmin = IsAdmin },
            cancellationToken));

    // GET: api/attendance/team/history
    [HttpGet("team/history")]
    [Authorize(Roles = AppRoles.Admin + "," + AppRoles.Manager)]
    [ProducesResponseType(typeof(TeamHistoryDto), StatusCodes.Status200OK)]
    public async Task<ActionResult> GetTeamHistory(
        [FromQuery] int days = GetTeamAttendanceHistory.DefaultDays,
        CancellationToken cancellationToken = default) =>
        HandleResult(await Mediator.Send(
            new GetTeamAttendanceHistory.Query
            {
                RequestingUserId = ResolveUserId(),
                IsAdmin = IsAdmin,
                Days = days,
            },
            cancellationToken));

    // GET: api/attendance/presence
    [HttpGet("presence")]
    [Authorize(Roles = AppRoles.Admin)]
    [ProducesResponseType(typeof(List<UserPresenceDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult> GetPresence(CancellationToken cancellationToken) =>
        HandleResult(await Mediator.Send(new GetUserPresence.Query(), cancellationToken));

    // GET: api/attendance/company
    [HttpGet("company")]
    [Authorize(Roles = AppRoles.Admin)]
    [ProducesResponseType(typeof(CompanyAttendanceDto), StatusCodes.Status200OK)]
    public async Task<ActionResult> GetCompany(CancellationToken cancellationToken) =>
        HandleResult(await Mediator.Send(new GetCompanyAttendance.Query(), cancellationToken));
}
