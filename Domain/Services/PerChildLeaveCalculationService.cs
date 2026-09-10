namespace Domain.Services;

/// <summary>
/// Per-child leave arithmetic: the week-to-business-day conversion, and the age
/// and eligibility questions a date of birth answers.
///
/// Pure, like <see cref="LeaveCalculationService"/>: no DbContext, no clock, no I/O.
/// Business-day counting, holiday exclusion and leave-year windows are NOT repeated
/// here — callers use <see cref="LeaveCalculationService"/> for those, so there is
/// only ever one calendar in the app.
///
/// Nothing stores a child's age. It is computed on every read, which is what makes
/// a child aging out of eligibility automatic: no nightly job, and no IsEligible
/// column that can disagree with the date of birth beside it.
/// </summary>
public static class PerChildLeaveCalculationService
{
    /// <summary>
    /// The one place a week becomes days. A "week" of per-child leave is a working
    /// week, so weekends and public holidays inside a request do not consume it.
    /// </summary>
    public const int BusinessDaysPerWeek = 5;

    public static int WeeksToBusinessDays(int weeks)
        => weeks <= 0 ? 0 : weeks * BusinessDaysPerWeek;

    /// <summary>
    /// Days read back as weeks, for display only. One decimal place: a 6-day
    /// request is "1.2 weeks", which is honest, where rounding to 1 would not be.
    /// </summary>
    public static decimal BusinessDaysToWeeks(int businessDays)
        => businessDays <= 0
            ? 0m
            : Math.Round(businessDays / (decimal)BusinessDaysPerWeek, 1, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Whole years completed on <paramref name="onDate"/>. Zero before birth.
    /// </summary>
    public static int AgeOn(DateOnly dateOfBirth, DateOnly onDate)
    {
        if (onDate <= dateOfBirth) return 0;

        var age = onDate.Year - dateOfBirth.Year;
        if (onDate < BirthdayInYear(dateOfBirth, onDate.Year)) age--;
        return age;
    }

    /// <summary>
    /// The last date a request for this child may END on — the day before the
    /// birthday on which they reach <paramref name="maxAge"/>. Named in the
    /// refusal message, so an employee learns the boundary rather than guessing.
    /// </summary>
    public static DateOnly LastEligibleDate(DateOnly dateOfBirth, int maxAge)
        => BirthdayInYear(dateOfBirth, dateOfBirth.Year + maxAge).AddDays(-1);

    public static bool IsEligibleOn(DateOnly dateOfBirth, DateOnly onDate, int maxAge)
        => AgeOn(dateOfBirth, onDate) < maxAge;

    /// <summary>
    /// Floored at zero, mirroring
    /// <see cref="LeaveCalculationService.CalculateRemainingBalance"/>.
    /// </summary>
    public static int RemainingDays(int totalDays, int usedDays)
        => Math.Max(0, totalDays - usedDays);

    /// <summary>
    /// A 29 February birthday falls on 1 March in a common year: the child
    /// completes a year of life the day after 28 February, not on it.
    /// </summary>
    private static DateOnly BirthdayInYear(DateOnly dateOfBirth, int year)
        => dateOfBirth is { Month: 2, Day: 29 } && !DateTime.IsLeapYear(year)
            ? new DateOnly(year, 3, 1)
            : new DateOnly(year, dateOfBirth.Month, dateOfBirth.Day);
}
