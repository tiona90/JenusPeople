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
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<AppSettingsDto>> UpdateSettings(
        [FromBody] UpdateAppSettings.Command command,
        CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(command, cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("reset-reminders")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<AppSettingsDto>> ResetReminders(
        CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new ResetReminders.Command(), cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("clear-approval-history")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<int>> ClearApprovalHistory(CancellationToken cancellationToken) =>
        HandleResult(await Mediator.Send(new ClearApprovalHistory.Command(), cancellationToken));

    // On-demand dispatch of a single reminder, ignoring its schedule. Lets an
    // admin verify reminder delivery without waiting for the configured time.
    [HttpPost("run-reminder/{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> RunReminder(
        string id,
        [FromServices] ReminderDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        await dispatcher.DispatchAsync(id, cancellationToken);
        return Ok(new { message = $"Reminder '{id}' dispatched. Check the logs and recipient inboxes." });
    }
}
