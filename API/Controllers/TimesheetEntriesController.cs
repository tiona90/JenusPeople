using System.Security.Claims;
using API.Extensions;
using API.Models;
using Application.Core;
using Application.Timesheets.Support;
using Asp.Versioning;
using Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace API.Controllers;

public class CreateEntryRequest
{
    public int ProjectId { get; set; }
    public DateTime Date { get; set; }
    public decimal HoursWorked { get; set; }
    public string? Notes { get; set; }
    public int? ActivityTypeId { get; set; }
    public int? ProjectTypeId { get; set; }
    public int? ProjectComponentId { get; set; }
    /// <summary>One of the owner's open tasks on this project, or null — see TimesheetEntryTaskRule.</summary>
    public int? WorkTaskId { get; set; }
}

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/timesheets/{timesheetId}/entries")]
[Route("api/timesheets/{timesheetId}/entries")] // unversioned alias resolves to the default API version (v1)
[Authorize]
public class TimesheetEntriesController : ControllerBase
{
    private readonly AppDbContext _context;

    public TimesheetEntriesController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Returns <c>null</c> when the caller may write to <paramref name="timesheetId"/>,
    /// otherwise the response to send back. <c>[Authorize]</c> alone only proves the
    /// caller is signed in — without this, any authenticated user could add, edit or
    /// delete entries on anyone's timesheet just by supplying its id.
    /// </summary>
    private async Task<ActionResult?> DenyIfNotWritableAsync(string timesheetId, CancellationToken cancellationToken)
    {
        var access = await TimesheetAccess.AuthorizeWriteAsync(
            _context,
            timesheetId,
            ResolveUserId(),
            User.IsSystemAdministrator(),
            User.IsDepartmentScoped(),
            cancellationToken,
            isHrAdministrator: User.IsHrAdministrator());

        if (access.IsSuccess)
        {
            if (TimesheetAccess.IsEditable(access.Value!.Status)) return null;

            return StatusCode(
                StatusCodes.Status409Conflict,
                ApiErrorResponseExtensions.Create(HttpContext, StatusCodes.Status409Conflict, TimesheetAccess.NotEditableMessage));
        }

        // Same Result<T> → ApiErrorResponse shape BaseApiController.HandleResult
        // produces; this controller talks to the DbContext directly rather than
        // through MediatR, so it maps its own.
        var statusCode = access.ErrorKind == ResultErrorKind.Forbidden
            ? StatusCodes.Status403Forbidden
            : StatusCodes.Status404NotFound;

        return StatusCode(statusCode, ApiErrorResponseExtensions.Create(HttpContext, statusCode, access.Error));
    }

    /// <summary>
    /// The activity types the project has narrowed itself to — empty when it has
    /// narrowed nothing, which leaves the whole catalogue available.
    /// </summary>
    private Task<List<int>> AssignedActivityTypeIdsAsync(int projectId, CancellationToken cancellationToken) =>
        _context.ProjectActivityAssignments
            .Where(a => a.ProjectId == projectId)
            .Select(a => a.ActivityTypeId)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The types the project is classified as — empty when it is unclassified,
    /// which leaves any type acceptable.
    /// </summary>
    private Task<List<int>> AssignedProjectTypeIdsAsync(int projectId, CancellationToken cancellationToken) =>
        _context.ProjectTypeAssignments
            .Where(a => a.ProjectId == projectId)
            .Select(a => a.ProjectTypeId)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The components the project is made up of — empty when it has declared
    /// none, which leaves the whole catalogue available.
    /// </summary>
    private Task<List<int>> AssignedComponentIdsAsync(int projectId, CancellationToken cancellationToken) =>
        _context.ProjectComponentAssignments
            .Where(a => a.ProjectId == projectId)
            .Select(a => a.ComponentId)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Saves the pending entry change and the sheet's recalculated TotalHours as
    /// one transaction — the total is summed from the database, so it has to follow
    /// the entry's own save, and a failure on it must not leave the entry written
    /// against a stale total. A concurrency conflict (another write to the sheet
    /// since this request read it) answers 409 and writes nothing; anything else
    /// propagates to GlobalExceptionMiddleware. Null on success.
    /// </summary>
    private async Task<ActionResult?> SaveWithTotalAsync(string timesheetId, CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);

            var timesheet = await _context.Timesheets.FindAsync([timesheetId], cancellationToken);
            if (timesheet is not null)
            {
                timesheet.TotalHours = await _context.TimesheetEntries
                    .Where(e => e.TimesheetId == timesheetId)
                    .SumAsync(e => e.HoursWorked, cancellationToken);
                await _context.SaveChangesAsync(cancellationToken);
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            return StatusCode(
                StatusCodes.Status409Conflict,
                ApiErrorResponseExtensions.Create(HttpContext, StatusCodes.Status409Conflict, ConcurrencyError.Message));
        }

        await transaction.CommitAsync(cancellationToken);
        return null;
    }

    private async Task<(DateTime Start, DateTime End)> PeriodOfAsync(string timesheetId, CancellationToken cancellationToken)
    {
        var sheet = await _context.Timesheets
            .AsNoTracking()
            .Where(t => t.Id == timesheetId)
            .Select(t => new { t.PeriodStart, t.PeriodEnd })
            .FirstAsync(cancellationToken);
        return (sheet.PeriodStart, sheet.PeriodEnd);
    }

    // POST: api/timesheets/{timesheetId}/entries
    [HttpPost]
    [ProducesResponseType(typeof(TimesheetEntry), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TimesheetEntry>> AddEntry(string timesheetId, CreateEntryRequest request, CancellationToken cancellationToken)
    {
        var denied = await DenyIfNotWritableAsync(timesheetId, cancellationToken);
        if (denied is not null) return denied;

        var entry = new TimesheetEntry
        {
            Id = Guid.NewGuid().ToString(),
            TimesheetId = timesheetId,
            ProjectId = request.ProjectId,
            Date = request.Date,
            HoursWorked = request.HoursWorked,
            Notes = request.Notes,
            ActivityTypeId = request.ActivityTypeId,
            ProjectTypeId = request.ProjectTypeId,
            ProjectComponentId = request.ProjectComponentId,
            WorkTaskId = request.WorkTaskId,
        };

        var existing = await _context.TimesheetEntries
            .Where(e => e.TimesheetId == timesheetId)
            .ToListAsync(cancellationToken);
        var validation = TimesheetEntryValidator.Validate(
            entry,
            existing,
            today: null,
            assignedActivityTypeIds: await AssignedActivityTypeIdsAsync(entry.ProjectId, cancellationToken),
            assignedProjectTypeIds: await AssignedProjectTypeIdsAsync(entry.ProjectId, cancellationToken),
            assignedComponentIds: await AssignedComponentIdsAsync(entry.ProjectId, cancellationToken),
            period: await PeriodOfAsync(timesheetId, cancellationToken));
        if (!validation.IsValid)
            throw new ArgumentException(validation.Error);

        var taskRefusal = await TimesheetEntryTaskRule.CheckAsync(_context, timesheetId, entry, stored: null, cancellationToken);
        if (taskRefusal is not null)
            throw new ArgumentException(taskRefusal);

        _context.TimesheetEntries.Add(entry);
        var conflict = await SaveWithTotalAsync(timesheetId, cancellationToken);
        if (conflict is not null) return conflict;

        return CreatedAtAction(null, new { timesheetId, entryId = entry.Id }, entry);
    }

    // PUT: api/timesheets/{timesheetId}/entries/{entryId}
    [HttpPut("{entryId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateEntry(string timesheetId, string entryId, TimesheetEntry entry, CancellationToken cancellationToken)
    {
        if (entryId != entry.Id || timesheetId != entry.TimesheetId)
            return BadRequest("Id or TimesheetId mismatch");

        var denied = await DenyIfNotWritableAsync(timesheetId, cancellationToken);
        if (denied is not null) return denied;

        var existing = await _context.TimesheetEntries
            .AsNoTracking()
            .Where(e => e.TimesheetId == timesheetId)
            .ToListAsync(cancellationToken);

        // The save below keys off the entry id alone, so an id belonging to some
        // other timesheet would otherwise be silently reassigned into this one.
        if (!existing.Any(e => e.Id == entryId))
            return NotFound();

        var validation = TimesheetEntryValidator.Validate(
            entry,
            existing,
            today: null,
            assignedActivityTypeIds: await AssignedActivityTypeIdsAsync(entry.ProjectId, cancellationToken),
            assignedProjectTypeIds: await AssignedProjectTypeIdsAsync(entry.ProjectId, cancellationToken),
            assignedComponentIds: await AssignedComponentIdsAsync(entry.ProjectId, cancellationToken),
            period: await PeriodOfAsync(timesheetId, cancellationToken));
        if (!validation.IsValid)
            throw new ArgumentException(validation.Error);

        var taskRefusal = await TimesheetEntryTaskRule.CheckAsync(
            _context, timesheetId, entry, existing.Single(e => e.Id == entryId), cancellationToken);
        if (taskRefusal is not null)
            throw new ArgumentException(taskRefusal);

        _context.Entry(entry).State = EntityState.Modified;
        var conflict = await SaveWithTotalAsync(timesheetId, cancellationToken);
        if (conflict is not null)
        {
            // The entry itself may have gone since it was read.
            _context.ChangeTracker.Clear();
            if (!await _context.TimesheetEntries.AnyAsync(e => e.Id == entryId && e.TimesheetId == timesheetId, cancellationToken))
                return NotFound();
            return conflict;
        }

        return NoContent();
    }

    // DELETE: api/timesheets/{timesheetId}/entries/{entryId}
    [HttpDelete("{entryId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteEntry(string timesheetId, string entryId, CancellationToken cancellationToken)
    {
        var denied = await DenyIfNotWritableAsync(timesheetId, cancellationToken);
        if (denied is not null) return denied;

        var entry = await _context.TimesheetEntries
            .FirstOrDefaultAsync(e => e.Id == entryId && e.TimesheetId == timesheetId, cancellationToken);
        if (entry == null) return NotFound();

        _context.TimesheetEntries.Remove(entry);
        var conflict = await SaveWithTotalAsync(timesheetId, cancellationToken);
        if (conflict is not null) return conflict;

        return NoContent();
    }

    private string ResolveUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue("sub")
        ?? User.Identity?.Name
        ?? string.Empty;
}
