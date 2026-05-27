using System.Security.Claims;
using Application.Timesheets.Commands;
using Application.Timesheets.DTOs;
using Application.Timesheets.Queries;
using Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

    [ApiVersion("1.0")]

    public class TimesheetsController : BaseApiController
    {
        private readonly AppDbContext _context;

        public TimesheetsController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/timesheets
        [HttpGet]
        [Authorize]
        public async Task<ActionResult<List<TimesheetDto>>> GetTimesheets([FromQuery] bool myOnly = false)
        {
            var userId = ResolveUserId();
            var isAdmin = User.IsInRole(AppRoles.Admin);
            var isManager = User.IsInRole(AppRoles.Manager);

            return await Mediator.Send(new GetTimesheetList.Query
            {
                RequestingUserId = userId,
                IsAdmin = !myOnly && isAdmin,
                IsManager = !myOnly && isManager,
            });
        }

        // GET: api/timesheets/{id}
        [HttpGet("{id}")]
        [Authorize]
        public async Task<ActionResult<Timesheet>> GetTimesheet(string id)
        {
            var timesheet = await _context.Timesheets
                .Include(t => t.Entries)
                .FirstOrDefaultAsync(t => t.Id == id);
            if (timesheet == null) return NotFound();
            return timesheet;
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

        // DELETE: api/timesheets/{id}
        [HttpDelete("{id}")]
        [Authorize]
        public async Task<IActionResult> DeleteTimesheet(string id)
        {
            var timesheet = await _context.Timesheets
                .Include(t => t.Entries)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (timesheet == null) return NotFound();

            if (timesheet.Status != TimesheetStatus.Draft)
                return BadRequest("Only Draft timesheets can be deleted.");

            var userId = ResolveUserId();

            var employeeProfile = await _context.EmployeeProfiles
                .FirstOrDefaultAsync(ep => ep.UserId == userId);

            if (employeeProfile == null || timesheet.EmployeeId != employeeProfile.Id)
                return Forbid();

            _context.Timesheets.Remove(timesheet);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        // PATCH: api/timesheets/{id}/submit
        [HttpPatch("{id}/submit")]
        [Authorize]
        public async Task<IActionResult> SubmitTimesheet(string id)
        {
            var result = await Mediator.Send(new SubmitTimesheet.Command
            {
                Id = id,
                RequestingUserId = ResolveUserId(),
                IsAdmin = User.IsInRole(AppRoles.Admin),
            });

            return HandleResult(result);
        }

        // PATCH: api/timesheets/{id}/approve
        [HttpPatch("{id}/approve")]
        [Authorize(Roles = AppRoles.Admin + "," + AppRoles.Manager)]
        public async Task<IActionResult> ApproveTimesheet(string id)
        {
            var result = await Mediator.Send(new UpdateTimesheetStatus.Command
            {
                Id = id,
                NewStatus = TimesheetStatus.Approved,
                RequestingUserId = ResolveUserId(),
                IsAdmin = User.IsInRole(AppRoles.Admin),
                IsManager = User.IsInRole(AppRoles.Manager),
            });

            return HandleResult(result);
        }

        // PATCH: api/timesheets/{id}/reject
        [HttpPatch("{id}/reject")]
        [Authorize(Roles = AppRoles.Admin + "," + AppRoles.Manager)]
        public async Task<IActionResult> RejectTimesheet(string id)
        {
            var result = await Mediator.Send(new UpdateTimesheetStatus.Command
            {
                Id = id,
                NewStatus = TimesheetStatus.Rejected,
                RequestingUserId = ResolveUserId(),
                IsAdmin = User.IsInRole(AppRoles.Admin),
                IsManager = User.IsInRole(AppRoles.Manager),
            });

            return HandleResult(result);
        }

        // GET: api/timesheets/{id}/history
        [HttpGet("{id}/history")]
        [Authorize]
        public async Task<ActionResult<IEnumerable<TimesheetStatusHistory>>> GetStatusHistory(string id)
        {
            var history = await _context.TimesheetStatusHistories
                .Where(h => h.TimesheetId == id)
                .ToListAsync();
            return history;
        }

        /// <summary>
        /// Admin only: Retrieves status history across all timesheets, filterable by employee, department, date range, and status transition.
        /// </summary>
        [HttpGet("/api/admin/timesheets/history")]
        [ProducesResponseType(typeof(IEnumerable<TimesheetStatusHistory>), 200)]
        [ProducesResponseType(403)]
        [Authorize(Roles = AppRoles.Admin)]
        public async Task<ActionResult<IEnumerable<TimesheetStatusHistory>>> GetAllStatusHistories(
            [FromQuery] string? employeeId,
            [FromQuery] int? departmentId,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] int? fromStatus,
            [FromQuery] int? toStatus)
        {
            var query = _context.TimesheetStatusHistories
                .Include(h => h.Timesheet)
                .AsQueryable();

            if (!string.IsNullOrEmpty(employeeId))
                query = query.Where(h => h.Timesheet!.EmployeeId == employeeId);
            if (departmentId.HasValue)
                query = query.Where(h => h.Timesheet!.DepartmentId == departmentId.Value);
            if (from.HasValue)
                query = query.Where(h => h.ChangedAt >= from.Value);
            if (to.HasValue)
                query = query.Where(h => h.ChangedAt <= to.Value);
            if (fromStatus.HasValue)
                query = query.Where(h => h.FromStatus == fromStatus.Value);
            if (toStatus.HasValue)
                query = query.Where(h => h.ToStatus == toStatus.Value);

            var result = await query.ToListAsync();
            return Ok(result);
        }

        /// <summary>
        /// Retrieves all status history entries across all timesheets for a specific employee. Scoped by role.
        /// </summary>
        [HttpGet("/api/employees/{employeeId}/timesheets/history")]
        [ProducesResponseType(typeof(IEnumerable<TimesheetStatusHistory>), 200)]
        [ProducesResponseType(403)]
        [Authorize]
        public async Task<ActionResult<IEnumerable<TimesheetStatusHistory>>> GetEmployeeStatusHistories(string employeeId)
        {
            var isAdmin = User.IsInRole(AppRoles.Admin);
            var userId = User.Identity?.Name;

            if (!isAdmin && userId != employeeId)
                return Forbid();

            var histories = await _context.TimesheetStatusHistories
                .Include(h => h.Timesheet)
                .Where(h => h.Timesheet!.EmployeeId == employeeId)
                .ToListAsync();
            return Ok(histories);
        }

        private string ResolveUserId() =>
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.Identity?.Name
            ?? string.Empty;
    }
}
