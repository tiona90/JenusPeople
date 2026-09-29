using API.Hubs;
using Application.TaskSettings;
using Application.TaskSettings.Commands;
using Application.TaskSettings.Queries;
using Asp.Versioning;
using Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace API.Controllers;

/// <summary>
/// The Task Settings. Every role's task dialog reads them; only the System
/// Administrator — who configures the workspace and does not see the tasks — writes them.
/// </summary>
[ApiVersion("1.0")]
public class WorkTaskSettingsController(IHubContext<NotificationsHub> notificationsHub) : BaseApiController
{
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<WorkTaskSettingsDto>> GetSettings() =>
        HandleResult(await Mediator.Send(new GetWorkTaskSettings.Query()));

    [HttpPut]
    [Authorize(Roles = AppRoles.SystemAdministrator)]
    public async Task<ActionResult<WorkTaskSettingsDto>> UpdateSettings([FromBody] WorkTaskSettingsDto settings, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new UpdateWorkTaskSettings.Command { Settings = settings }, cancellationToken);
        // Rare, and it can close waiting tasks: every open task page refetches.
        if (result.IsSuccess)
            await notificationsHub.Clients.All.SendAsync("notificationsUpdated", cancellationToken);
        return HandleResult(result);
    }
}
