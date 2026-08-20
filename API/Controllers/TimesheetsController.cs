using System.Security.Claims;
using API.Hubs;
using API.Models;
using Application.Timesheets.Commands;
using Application.Timesheets.DTOs;
using Application.Timesheets.Queries;
using Application.TimesheetStatusHistories.DTOs;
using Application.TimesheetStatusHistories.Queries;
using Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Persistence;
using Asp.Versioning;

namespace API.Controllers
{
    public class CreateTimesheetRequest
    {
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
    }

    public class RejectTimesheetRequest
    {
        public string? Comment { get; set; }
    }

    public class GenerateDraftTimesheetRequest
    {
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public int ProjectId { get; set; }
    }

    [ApiVersion("1.0")]

    public class TimesheetsController : BaseApiController
    {
        private readonly AppDbContext _context;
        private readonly IHubContext<NotificationsHub> _notificationsHub;

        public TimesheetsController(AppDbContext context, IHubContext<NotificationsHub> notificationsHub)
        {
            _context = context;
            _notificationsHub = notificationsHub;
        }

        private async Task NotifyForTimesheetAsync(string timesheetId, CancellationToken cancellationToken = default)
        {
            var audience = await _context.Timesheets
                .AsNoTracking()
                .Where(t => t.Id == timesheetId)
                .Select(t => new
                {
                    t.DepartmentId,
                    EmployeeUserId = t.Employee != null ? t.Employee.UserId : null,
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (audience is null) return;

            var dispatch = new List<Task>
            {
                _notificationsHub.Clients.Group(NotificationsHub.AdminGroup).SendAsync("notificationsUpdated", cancellationToken),
                _notificationsHub.Clients.Group(NotificationsHub.DepartmentManagerGroup(audience.DepartmentId)).SendAsync("notificationsUpdated", cancellationToken),
            };

            if (!string.IsNullOrWhiteSpace(audience.EmployeeUserId))
            {
                dispatch.Add(_notificationsHub.Clients.User(audience.EmployeeUserId).SendAsync("notificationsUpdated", cancellationToken));
            }

            await Task.WhenAll(dispatch);
        }

        // GET: api/timesheets
        [HttpGet]
        [Authorize]
        public async Task<ActionResult<List<TimesheetDto>>> GetTimesheets([FromQuery] bool myOnly = false, [FromQuery] int? page = null, [FromQuery] int? pageSize = null)
        {
            var userId = ResolveUserId();
            var isAdmin = User.IsInRole(AppRoles.Admin);
            var isManager = User.IsInRole(AppRoles.Manager);

            var result = await Mediator.Send(new GetTimesheetList.Query
            {
                RequestingUserId = userId,
                IsAdmin = !myOnly && isAdmin,
                IsManager = !myOnly && isManager,
                Page = page,
                PageSize = pageSize,
            });
            return Paged(result);
        }

        // GET: api/timesheets/{id}
        [HttpGet("{id}")]
        [Authorize]
        [ProducesResponseType(typeof(Timesheet), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<Timesheet>> GetTimesheet(string id, CancellationToken cancellationToken)
        {
            var result = await Mediator.Send(new GetTimesheetDetail.Query
            {
                Id = id,
                RequestingUserId = ResolveUserId(),
                IsAdmin = User.IsInRole(AppRoles.Admin),
                IsManager = User.IsInRole(AppRoles.Manager),
            }, cancellationToken);

            return HandleResult(result);
        }

        // POST: api/timesheets
        [HttpPost]
        [Authorize]
        public async Task<ActionResult<TimesheetDto>> CreateTimesheet(CreateTimesheetRequest request)
        {
            var result = await Mediator.Send(new CreateTimesheet.Command
            {
                RequestingUserId = ResolveUserId(),
                PeriodStart = request.PeriodStart,
                PeriodEnd = request.PeriodEnd,
            });

            return HandleResult(result);
        }

        // POST: api/timesheets/generate-draft
        // Populates a Draft timesheet for the period by reading the caller's
        // AttendanceEvents and turning each day's worked-minus-break time into
        // a TimesheetEntry against the supplied project. Idempotent: reruns
        // for the same period replace the prior entries.
        [HttpPost("generate-draft")]
        [Authorize]
        public async Task<ActionResult<TimesheetDto>> GenerateDraft(GenerateDraftTimesheetRequest request)
        {
            var result = await Mediator.Send(new GenerateDraft.Command
            {
                RequestingUserId = ResolveUserId(),
                PeriodStart = request.PeriodStart,
                PeriodEnd = request.PeriodEnd,
                ProjectId = request.ProjectId,
            });

            return HandleResult(result);
        }

        // DELETE: api/timesheets/{id}
        [HttpDelete("{id}")]
        [Authorize]
        public async Task<ActionResult> DeleteTimesheet(string id, CancellationToken cancellationToken)
        {
            var result = await Mediator.Send(new DeleteTimesheet.Command
            {
                Id = id,
                RequestingUserId = ResolveUserId(),
                IsAdmin = User.IsInRole(AppRoles.Admin),
                IsManager = User.IsInRole(AppRoles.Manager),
            }, cancellationToken);

            return HandleResult(result);
        }

        // PATCH: api/timesheets/{id}/submit
        [HttpPatch("{id}/submit")]
        [Authorize]
        public async Task<IActionResult> SubmitTimesheet(string id, CancellationToken cancellationToken)
        {
            var result = await Mediator.Send(new SubmitTimesheet.Command
            {
                Id = id,
                RequestingUserId = ResolveUserId(),
                IsAdmin = User.IsInRole(AppRoles.Admin),
            }, cancellationToken);

            if (result.IsSuccess)
            {
                await NotifyForTimesheetAsync(id, cancellationToken);
            }

            return HandleResult(result);
        }

        // PATCH: api/timesheets/{id}/approve
        [HttpPatch("{id}/approve")]
        [Authorize(Roles = AppRoles.Admin + "," + AppRoles.Manager)]
        public async Task<IActionResult> ApproveTimesheet(string id, CancellationToken cancellationToken)
        {
            var result = await Mediator.Send(new UpdateTimesheetStatus.Command
            {
                Id = id,
                NewStatus = TimesheetStatus.Approved,
                RequestingUserId = ResolveUserId(),
                IsAdmin = User.IsInRole(AppRoles.Admin),
                IsManager = User.IsInRole(AppRoles.Manager),
            }, cancellationToken);

            if (result.IsSuccess)
            {
                await NotifyForTimesheetAsync(id, cancellationToken);
            }

            return HandleResult(result);
        }

        // PATCH: api/timesheets/{id}/reject
        [HttpPatch("{id}/reject")]
        [Authorize(Roles = AppRoles.Admin + "," + AppRoles.Manager)]
        public async Task<IActionResult> RejectTimesheet(string id, [FromBody] RejectTimesheetRequest? body, CancellationToken cancellationToken)
        {
            var result = await Mediator.Send(new UpdateTimesheetStatus.Command
            {
                Id = id,
                NewStatus = TimesheetStatus.Rejected,
                RequestingUserId = ResolveUserId(),
                IsAdmin = User.IsInRole(AppRoles.Admin),
                IsManager = User.IsInRole(AppRoles.Manager),
                Comment = body?.Comment,
            }, cancellationToken);

            if (result.IsSuccess)
            {
                await NotifyForTimesheetAsync(id, cancellationToken);
            }

            return HandleResult(result);
        }

        // GET: api/timesheets/{id}/history
        // Scoped through GetTimesheetStatusHistoryList rather than read directly, so
        // a caller with no claim on this timesheet gets an empty list — the same
        // answer the list endpoint already gives them.
        [HttpGet("{id}/history")]
        [Authorize]
        [ProducesResponseType(typeof(IEnumerable<TimesheetStatusHistoryDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult> GetStatusHistory(string id, CancellationToken cancellationToken)
        {
            var result = await Mediator.Send(new GetTimesheetStatusHistoryList.Query
            {
                TimesheetId = id,
                RequestingUserId = ResolveUserId(),
                IsAdmin = User.IsInRole(AppRoles.Admin),
                IsManager = User.IsInRole(AppRoles.Manager),
            }, cancellationToken);

            return Paged(result);
        }

        /// <summary>
        /// Admin only: status history across all timesheets, filterable by employee,
        /// department, date range and status transition.
        /// </summary>
        // The template is relative, so this action inherits both of
        // BaseApiController's routes — api/v{version}/timesheets/history and the
        // unversioned api/timesheets/history alias. The absolute template it used to
        // carry sat outside versioning entirely, so the endpoint could never be
        // revised behind a new API version like every other action can.
        //
        // The filtering now lives in GetTimesheetStatusHistoryList, so this route,
        // {id}/history above it and the per-employee route below all share one scope
        // filter, one projection and one paging path. Reading the entities here also
        // meant serialising the whole Included graph — timesheet, employee profile,
        // user — straight to the client.
        [HttpGet("history")]
        [ProducesResponseType(typeof(IEnumerable<TimesheetStatusHistoryDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [Authorize(Roles = AppRoles.Admin)]
        public async Task<ActionResult> GetAllStatusHistories(
            [FromQuery] string? employeeProfileId,
            [FromQuery] int? departmentId,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] int? fromStatus,
            [FromQuery] int? toStatus,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            CancellationToken cancellationToken)
        {
            var result = await Mediator.Send(new GetTimesheetStatusHistoryList.Query
            {
                EmployeeProfileId = employeeProfileId,
                DepartmentId = departmentId,
                From = from,
                To = to,
                FromStatus = fromStatus,
                ToStatus = toStatus,
                RequestingUserId = ResolveUserId(),
                IsAdmin = User.IsInRole(AppRoles.Admin),
                IsManager = User.IsInRole(AppRoles.Manager),
                Page = page,
                PageSize = pageSize,
            }, cancellationToken);

            return Paged(result);
        }

        /// <summary>
        /// Retrieves all status history entries across all timesheets for one
        /// employee, identified by their <see cref="EmployeeProfile"/>.Id.
        /// </summary>
        // Scoped through GetTimesheetStatusHistoryList for the same reason
        // {id}/history is: the handler's scope filter *is* the authorization, so an
        // employee sees their own trail, a manager sees their scope, and asking for
        // someone you have no claim on returns an empty list.
        //
        // What this replaced hand-rolled the check, and got it wrong in both halves:
        // it compared User.Identity.Name — an email — against the route's
        // EmployeeProfile.Id, which no non-admin could ever match, so every one of
        // them was refused outright; and it then returned raw entities rather than
        // the DTO every other history endpoint serves.
        [HttpGet("employees/{employeeProfileId}/history")]
        [ProducesResponseType(typeof(IEnumerable<TimesheetStatusHistoryDto>), StatusCodes.Status200OK)]
        [Authorize]
        public async Task<ActionResult> GetEmployeeStatusHistories(
            string employeeProfileId,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            CancellationToken cancellationToken)
        {
            var result = await Mediator.Send(new GetTimesheetStatusHistoryList.Query
            {
                EmployeeProfileId = employeeProfileId,
                RequestingUserId = ResolveUserId(),
                IsAdmin = User.IsInRole(AppRoles.Admin),
                IsManager = User.IsInRole(AppRoles.Manager),
                Page = page,
                PageSize = pageSize,
            }, cancellationToken);

            return Paged(result);
        }

        private string ResolveUserId() =>
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.Identity?.Name
            ?? string.Empty;
    }
}
