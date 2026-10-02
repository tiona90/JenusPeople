using Domain;
using Application.Reminders;
using Application.Settings.Commands;
using Application.Settings.DTOs;
using Application.Settings.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;

namespace API.Controllers;

[ApiVersion("1.0")]

public class SettingsController : BaseApiController
{
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<AppSettingsDto>> GetSettings(
        CancellationToken cancellationToken)
    {
        var dto = await Mediator.Send(new GetAppSettings.Query(), cancellationToken);
        return Ok(dto);
    }

    [HttpPut]
    [Authorize(Roles = AppRoles.SystemAdministrator)]
    public async Task<ActionResult<AppSettingsDto>> UpdateSettings(
        [FromBody] UpdateAppSettings.Command command,
        CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(command, cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("reset-reminders")]
    [Authorize(Roles = AppRoles.SystemAdministrator)]
    public async Task<ActionResult<AppSettingsDto>> ResetReminders(
        CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new ResetReminders.Command(), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("clear-approval-history")]
    [Authorize(Roles = AppRoles.SystemAdministrator)]
    public async Task<ActionResult<int>> ClearApprovalHistory(CancellationToken cancellationToken) =>
        HandleResult(await Mediator.Send(new ClearApprovalHistory.Command(), cancellationToken));

    // On-demand dispatch of a single reminder, ignoring its time and frequency.
    // Lets an admin verify reminder delivery without waiting for the configured
    // time. A non-working day (weekend or public holiday) still sends nothing.
    [HttpPost("run-reminder/{id}")]
    [Authorize(Roles = AppRoles.SystemAdministrator)]
    public async Task<ActionResult> RunReminder(
        string id,
        [FromServices] ReminderDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var dispatched = await dispatcher.DispatchAsync(id, cancellationToken);
        return Ok(new
        {
            message = dispatched
                ? $"Reminder '{id}' dispatched. Check the logs and recipient inboxes."
                : $"Reminder '{id}' not sent: today is not a working day, or the reminder has no dispatcher. See the logs.",
        });
    }
}
