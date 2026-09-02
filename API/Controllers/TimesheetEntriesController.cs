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
            User.IsInRole(AppRoles.Admin),
            User.IsInRole(AppRoles.Manager),
            cancellationToken);

        if (access.IsSuccess) return null;

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

    private async Task RecalculateTotalHoursAsync(string timesheetId)
    {
        var timesheet = await _context.Timesheets.FindAsync(timesheetId);
        if (timesheet == null) return;

        var totalHours = await _context.TimesheetEntries
            .Where(e => e.TimesheetId == timesheetId)
            .SumAsync(e => e.HoursWorked);

        timesheet.TotalHours = totalHours;
        await _context.SaveChangesAsync();
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
        };

        var existing = await _context.TimesheetEntries
            .Where(e => e.TimesheetId == timesheetId)
            .ToListAsync(cancellationToken);
        var validation = TimesheetEntryValidator.Validate(
            entry,
            existing,
            today: null,
            assignedActivityTypeIds: await AssignedActivityTypeIdsAsync(entry.ProjectId, cancellationToken),
            assignedProjectTypeIds: await AssignedProjectTypeIdsAsync(entry.ProjectId, cancellationToken));
        if (!validation.IsValid)
            throw new ArgumentException(validation.Error);

        _context.TimesheetEntries.Add(entry);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            var inner = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            return StatusCode(500, new { error = "Database error", details = inner });
        }

        await RecalculateTotalHoursAsync(timesheetId);

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
            assignedProjectTypeIds: await AssignedProjectTypeIdsAsync(entry.ProjectId, cancellationToken));
        if (!validation.IsValid)
            throw new ArgumentException(validation.Error);

        _context.Entry(entry).State = EntityState.Modified;
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!await _context.TimesheetEntries.AnyAsync(e => e.Id == entryId && e.TimesheetId == timesheetId, cancellationToken))
                return NotFound();
            throw;
        }

        await RecalculateTotalHoursAsync(timesheetId);

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
        await _context.SaveChangesAsync(cancellationToken);

        await RecalculateTotalHoursAsync(timesheetId);

        return NoContent();
    }

    private string ResolveUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue("sub")
        ?? User.Identity?.Name
        ?? string.Empty;
}
