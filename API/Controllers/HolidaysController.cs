using Application.Holidays.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;

namespace API.Controllers;

[ApiVersion("1.0")]

public class HolidaysController : BaseApiController
{
    [HttpGet("countries")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetCountries(CancellationToken cancellationToken) =>
        HandleResult(await Mediator.Send(new GetCountries.Query(), cancellationToken));

    [HttpGet("{year:int}")]
    [Authorize]
    public async Task<IActionResult> GetHolidays(int year, CancellationToken cancellationToken) =>
        HandleResult(await Mediator.Send(new GetHolidays.Query { Year = year }, cancellationToken));
}
