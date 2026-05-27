using Domain;

namespace Application.Timesheets;

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
    ///   - duplicating an existing project on the same date (overlap),
    ///   - pushing the same-day total above 24 hours.
    ///
    /// <paramref name="existing"/> should contain all entries for the same
    /// employee/timesheet, INCLUDING the candidate itself if it's an update —
    /// the candidate is filtered out by Id before comparisons.
    ///
    /// <paramref name="today"/> is optional; defaults to <see cref="DateTime.UtcNow"/>.
    /// Tests should pass an explicit value to avoid wall-clock coupling.
    /// </summary>
    public static ValidationResult Validate(
        TimesheetEntry candidate,
        IEnumerable<TimesheetEntry> existing,
        DateTime? today = null)
    {
        if (candidate.HoursWorked <= 0)
            return ValidationResult.Fail("Hours worked must be greater than zero.");

        if (candidate.HoursWorked > MaxHoursPerDay)
            return ValidationResult.Fail($"A single entry cannot exceed {MaxHoursPerDay} hours.");

        var todayDate = (today ?? DateTime.UtcNow).Date;
        if (candidate.Date.Date > todayDate)
            return ValidationResult.Fail("Entries for future dates are not allowed.");

        var sameDayEntries = existing
            .Where(e => e.Id != candidate.Id && e.Date.Date == candidate.Date.Date)
            .ToList();

        if (sameDayEntries.Any(e => e.ProjectId == candidate.ProjectId))
            return ValidationResult.Fail("An entry for this project on this date already exists.");

        var dailyTotal = sameDayEntries.Sum(e => e.HoursWorked) + candidate.HoursWorked;
        if (dailyTotal > MaxHoursPerDay)
            return ValidationResult.Fail(
                $"Total hours for {candidate.Date:yyyy-MM-dd} would be {dailyTotal} — exceeds the {MaxHoursPerDay}-hour daily cap.");

        return ValidationResult.Ok();
    }
}
