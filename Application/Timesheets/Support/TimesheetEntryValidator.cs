using Domain;

namespace Application.Timesheets.Support;

/// <summary>
/// Pure rules for individual timesheet entries. Caller is responsible for
/// loading the existing entries for the same timesheet/employee and passing
/// them in; this class never touches the DbContext.
/// </summary>
public static class TimesheetEntryValidator
{
    public const decimal MaxHoursPerDay = 24m;

    public readonly record struct ValidationResult(bool IsValid, string? Error)
    {
        public static ValidationResult Ok() => new(true, null);
        public static ValidationResult Fail(string error) => new(false, error);
    }

    /// <summary>
    /// Validates that <paramref name="candidate"/> can be saved without:
    ///   - non-positive hours,
    ///   - hours exceeding 24 in a single entry,
    ///   - a date in the future,
    ///   - an activity the project has not assigned,
    ///   - a type the project is not classified as,
    ///   - a component the project is not made up of,
    ///   - duplicating an existing project, type *and component* on the same date (overlap),
    ///   - pushing the same-day total above 24 hours.
    ///
    /// <paramref name="existing"/> should contain all entries for the same
    /// employee/timesheet, INCLUDING the candidate itself if it's an update —
    /// the candidate is filtered out by Id before comparisons.
    ///
    /// <paramref name="today"/> is optional; defaults to <see cref="DateTime.UtcNow"/>.
    /// Tests should pass an explicit value to avoid wall-clock coupling.
    ///
    /// <paramref name="assignedActivityTypeIds"/> is the activity types the
    /// candidate's project has narrowed itself to. Null or empty means it has
    /// narrowed nothing, and any activity is allowed.
    ///
    /// <paramref name="assignedProjectTypeIds"/> is the types the candidate's
    /// project is classified as. Null or empty means it is unclassified — a valid
    /// state — and any type is allowed.
    ///
    /// <paramref name="assignedComponentIds"/> is the components the candidate's
    /// project is made up of. Null or empty means it has declared none, and any
    /// component is allowed.
    /// </summary>
    public static ValidationResult Validate(
        TimesheetEntry candidate,
        IEnumerable<TimesheetEntry> existing,
        DateTime? today = null,
        IReadOnlyCollection<int>? assignedActivityTypeIds = null,
        IReadOnlyCollection<int>? assignedProjectTypeIds = null,
        IReadOnlyCollection<int>? assignedComponentIds = null)
    {
        if (candidate.HoursWorked <= 0)
            return ValidationResult.Fail("Hours worked must be greater than zero.");

        if (candidate.HoursWorked > MaxHoursPerDay)
            return ValidationResult.Fail($"A single entry cannot exceed {MaxHoursPerDay} hours.");

        var todayDate = (today ?? DateTime.UtcNow).Date;
        if (candidate.Date.Date > todayDate)
            return ValidationResult.Fail("Entries for future dates are not allowed.");

        // A project that has narrowed the activity catalogue accepts only what it
        // assigned. One that has assigned nothing accepts any activity, which is
        // every project that predates project-level assignment.
        if (assignedActivityTypeIds is { Count: > 0 }
            && candidate.ActivityTypeId.HasValue
            && !assignedActivityTypeIds.Contains(candidate.ActivityTypeId.Value))
            return ValidationResult.Fail("That activity is not available on this project.");

        // Same shape as the activity rule: a classified project accepts only the
        // types it carries, an unclassified one accepts anything, and an entry
        // with no type is always fine.
        if (assignedProjectTypeIds is { Count: > 0 }
            && candidate.ProjectTypeId.HasValue
            && !assignedProjectTypeIds.Contains(candidate.ProjectTypeId.Value))
            return ValidationResult.Fail("That type is not available on this project.");

        // And again for components, narrowed by the project exactly as the
        // activity is.
        if (assignedComponentIds is { Count: > 0 }
            && candidate.ProjectComponentId.HasValue
            && !assignedComponentIds.Contains(candidate.ProjectComponentId.Value))
            return ValidationResult.Fail("That component is not available on this project.");

        var sameDayEntries = existing
            .Where(e => e.Id != candidate.Id && e.Date.Date == candidate.Date.Date)
            .ToList();

        // Project, type and component together, because one project can
        // legitimately be worked on as two kinds of engagement, or on two of its
        // components, in a single day — 2h of Support on DM and 3h of Support on
        // Lasernet are two facts, not a duplicate. Rows that leave both blank
        // still collide, which is the pre-type behaviour unchanged.
        if (sameDayEntries.Any(e =>
                e.ProjectId == candidate.ProjectId
                && e.ProjectTypeId == candidate.ProjectTypeId
                && e.ProjectComponentId == candidate.ProjectComponentId))
            return ValidationResult.Fail("An entry for this project, type and component on this date already exists.");

        var dailyTotal = sameDayEntries.Sum(e => e.HoursWorked) + candidate.HoursWorked;
        if (dailyTotal > MaxHoursPerDay)
            return ValidationResult.Fail(
                $"Total hours for {candidate.Date:yyyy-MM-dd} would be {dailyTotal} — exceeds the {MaxHoursPerDay}-hour daily cap.");

        return ValidationResult.Ok();
    }
}
