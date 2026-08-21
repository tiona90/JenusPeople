using Application.Attendance.DTOs;
using Application.Core;
using Domain;
using Domain.Services;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Application.Attendance.Support;

/// <summary>
/// The loading, bucketing and wire-mapping every attendance handler shares.
///
/// The day-state rules themselves live in
/// <see cref="AttendanceDayStateCalculator"/> in the Domain layer; nothing here
/// decides anything, it only fetches the events, hands them over, and translates
/// the answer into the shapes the API returns.
///
/// The UTC day boundary is the one piece of policy that has to be applied
/// consistently: every query buckets events with <see cref="UtcDayStart"/> so a
/// shift is attributed to the same day everywhere.
/// </summary>
public static class AttendanceDay
{
    /// <summary>Midnight UTC of the day <paramref name="instant"/> falls in.</summary>
    public static DateTime UtcDayStart(DateTime instant) =>
        new(instant.Year, instant.Month, instant.Day, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Stamps Kind=Utc without shifting the value. SQL Server hands back
    /// datetime2 as Unspecified, and serialising that omits the timezone, so the
    /// browser would read a UTC instant as local time.
    /// </summary>
    public static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    public static DateTime? AsUtcNullable(DateTime? value) =>
        value.HasValue ? AsUtc(value.Value) : null;

    /// <summary>The wire vocabulary for a day status. The SPA's union type.</summary>
    public static string WireStatus(AttendanceDayStatus status) => status switch
    {
        AttendanceDayStatus.Out => "out",
        AttendanceDayStatus.In => "in",
        AttendanceDayStatus.Break => "break",
        AttendanceDayStatus.Done => "done",
        _ => "out",
    };

    public static string EventTypeName(AttendanceEventType type) => type switch
    {
        AttendanceEventType.CheckIn => "check-in",
        AttendanceEventType.CheckOut => "check-out",
        AttendanceEventType.BreakStart => "break-start",
        AttendanceEventType.BreakEnd => "break-end",
        _ => type.ToString().ToLowerInvariant(),
    };

    /// <summary>
    /// Attendance is recorded against an <see cref="EmployeeProfile"/>, so a user
    /// without one cannot take part. Every action and self-service query needs
    /// this first.
    /// </summary>
    public static Task<EmployeeProfile?> ResolveProfileAsync(
        AppDbContext context,
        string userId,
        CancellationToken cancellationToken) =>
        context.EmployeeProfiles.FirstOrDefaultAsync(ep => ep.UserId == userId, cancellationToken);

    /// <summary>
    /// Drops Admin-role accounts from an employee-profile query.
    ///
    /// An Admin may hold an <see cref="EmployeeProfile"/> — both admin accounts
    /// in this deployment do — but an admin is not part of the tracked workforce.
    /// Counting them inflated every company and department total, and put names in
    /// the "not checked in" feed that nobody expects to check in. The
    /// employee-facing queries already draw the line here: GetTeammateList excludes
    /// admins unconditionally, and the manager-scoped leave and profile queries do
    /// the same. Attendance was the one population that did not.
    /// </summary>
    public static IQueryable<EmployeeProfile> ExcludeAdmins(IQueryable<EmployeeProfile> profiles) =>
        profiles.Where(p => p.User == null
            || !p.User.UserRoles.Any(ur => ur.Role != null && ur.Role.Name == AppRoles.Admin));

    /// <summary>
    /// The refusal for a caller with no employee profile, phrased identically
    /// across all ten endpoints.
    /// </summary>
    public static Result<T> NoProfile<T>() =>
        Result<T>.Invalid("No employee profile found.");

    /// <summary>One employee's events for the UTC day containing <paramref name="instant"/>.</summary>
    public static async Task<List<AttendanceEvent>> LoadDayEventsAsync(
        AppDbContext context,
        string employeeProfileId,
        DateTime instant,
        CancellationToken cancellationToken)
    {
        var dayStart = UtcDayStart(instant);
        var dayEnd = dayStart.AddDays(1);

        return await context.AttendanceEvents
            .Where(e => e.EmployeeProfileId == employeeProfileId && e.At >= dayStart && e.At < dayEnd)
            .OrderBy(e => e.At)
            .ToListAsync(cancellationToken);
    }

    public static AttendanceEvent NewEvent(string employeeProfileId, DateTime at, AttendanceEventType type) =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            EmployeeProfileId = employeeProfileId,
            At = at,
            Type = type,
        };

    /// <summary>
    /// Today's state plus the raw event list, which is what every action returns
    /// so the caller does not have to re-fetch after checking in or out.
    /// </summary>
    public static async Task<TodayStateDto> BuildTodayStateAsync(
        AppDbContext context,
        string employeeProfileId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var events = await LoadDayEventsAsync(context, employeeProfileId, nowUtc, cancellationToken);
        var state = AttendanceDayStateCalculator.Calculate(events, nowUtc);

        return new TodayStateDto(
            UtcDayStart(nowUtc).ToString("yyyy-MM-dd"),
            WireStatus(state.Status),
            AsUtcNullable(state.CheckInAt),
            AsUtcNullable(state.CheckOutAt),
            AsUtcNullable(state.OnBreakSince),
            state.TotalBreakMinutes,
            state.WorkedMinutes,
            [.. events.Select(e => new AttendanceEventDto(e.Id, AsUtc(e.At), EventTypeName(e.Type)))]);
    }

    /// <summary>
    /// Profile ids on approved leave spanning <paramref name="instant"/>, limited
    /// to <paramref name="employeeProfileIds"/>.
    ///
    /// AnnualLeave carries two employee keys and they are not interchangeable:
    /// EmployeeId is an AspNetUsers.Id, EmployeeProfileId is an
    /// EmployeeProfile.Id. Filtering the profile list against the wrong one is
    /// what left "on leave today" permanently empty on both boards.
    /// </summary>
    public static async Task<HashSet<string>> LoadOnLeaveProfileIdsAsync(
        AppDbContext context,
        IReadOnlyCollection<string> employeeProfileIds,
        DateTime instant,
        CancellationToken cancellationToken)
    {
        var onLeave = await context.AnnualLeaves
            .Where(l => l.EmployeeProfileId != null
                && employeeProfileIds.Contains(l.EmployeeProfileId)
                && l.Status == AnnualLeaveStatus.Approved
                && l.StartDate <= instant && l.EndDate >= instant)
            .Select(l => l.EmployeeProfileId!)
            .ToListAsync(cancellationToken);

        return [.. onLeave];
    }

    /// <summary>
    /// Today's events for a set of employees, grouped by profile id. Employees
    /// with no events are absent from the dictionary rather than present with an
    /// empty list, so callers use TryGetValue and fall back to an empty day.
    /// </summary>
    public static async Task<Dictionary<string, List<AttendanceEvent>>> LoadDayEventsByEmployeeAsync(
        AppDbContext context,
        IReadOnlyCollection<string> employeeProfileIds,
        DateTime instant,
        CancellationToken cancellationToken)
    {
        var dayStart = UtcDayStart(instant);
        var dayEnd = dayStart.AddDays(1);

        var events = await context.AttendanceEvents
            .Where(e => employeeProfileIds.Contains(e.EmployeeProfileId) && e.At >= dayStart && e.At < dayEnd)
            .ToListAsync(cancellationToken);

        return events
            .GroupBy(e => e.EmployeeProfileId)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>
    /// The day state for one employee out of a grouped event set — an empty day
    /// when they have no events at all.
    /// </summary>
    public static AttendanceDayState StateFor(
        Dictionary<string, List<AttendanceEvent>> eventsByEmployee,
        string employeeProfileId,
        DateTime nowUtc)
    {
        eventsByEmployee.TryGetValue(employeeProfileId, out var events);
        return AttendanceDayStateCalculator.Calculate(events ?? [], nowUtc);
    }

    /// <summary>Display name for a profile, falling back through the user record.</summary>
    public static string DisplayNameOf(EmployeeProfile profile) =>
        profile.User?.DisplayName ?? profile.User?.UserName ?? "Unknown";
}
