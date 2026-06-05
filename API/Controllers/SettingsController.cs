using Application.Settings.Commands;
using Application.Settings.DTOs;
using Application.Settings.Queries;
using Infrastructure.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Asp.Versioning;

namespace API.Controllers;

[ApiVersion("1.0")]

public class SettingsController : BaseApiController
{
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<AppSettingsDto>> GetSettings(
        [FromServices] IOptionsMonitor<SlackOptions> slackOptions,
        CancellationToken cancellationToken)
    {
        var dto = await Mediator.Send(new GetAppSettings.Query(), cancellationToken);
        dto.SlackConnected = IsSlackConnected(slackOptions);
        return Ok(dto);
    }

    [HttpPut]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<AppSettingsDto>> UpdateSettings(
        [FromBody] UpdateAppSettings.Command command,
        [FromServices] IOptionsMonitor<SlackOptions> slackOptions,
        CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(command, cancellationToken);
        if (result.IsSuccess && result.Value is not null)
            result.Value.SlackConnected = IsSlackConnected(slackOptions);
        return HandleResult(result);
    }

    [HttpPost("reset-reminders")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<AppSettingsDto>> ResetReminders(
        [FromServices] IOptionsMonitor<SlackOptions> slackOptions,
        CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new ResetReminders.Command(), cancellationToken);
        if (result.IsSuccess && result.Value is not null)
            result.Value.SlackConnected = IsSlackConnected(slackOptions);
        return HandleResult(result);
    }

    [HttpPost("clear-approval-history")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<int>> ClearApprovalHistory(CancellationToken cancellationToken) =>
        HandleResult(await Mediator.Send(new ClearApprovalHistory.Command(), cancellationToken));

    private static bool IsSlackConnected(IOptionsMonitor<SlackOptions> slackOptions) =>
        !string.IsNullOrWhiteSpace(slackOptions.CurrentValue.WebhookUrl);
}
