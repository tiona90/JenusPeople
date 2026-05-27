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
    public async Task<ActionResult<AppSettingsDto>> GetSettings(CancellationToken cancellationToken) =>
        Ok(await Mediator.Send(new GetAppSettings.Query(), cancellationToken));

    [HttpPut]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<AppSettingsDto>> UpdateSettings(
        [FromBody] UpdateAppSettings.Command command,
        CancellationToken cancellationToken) =>
        HandleResult(await Mediator.Send(command, cancellationToken));
}
