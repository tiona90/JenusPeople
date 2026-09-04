namespace Domain;

public enum AttendanceEventType
{
    CheckIn = 0,
    CheckOut = 1,
    BreakStart = 2,
    BreakEnd = 3,

    /// <summary>System-triggered break, opened by client-side idle detection rather than a user action.</summary>
    AutoBreakStart = 4,

    /// <summary>Closes an <see cref="AutoBreakStart"/> break once activity resumes.</summary>
    AutoBreakEnd = 5,
}

public class AttendanceEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    /// <summary>
    /// FK to <see cref="Domain.EmployeeProfile"/>.Id — <b>not</b> AspNetUsers.Id.
    /// The sibling <see cref="AnnualLeave.EmployeeId"/> is a user id, so the two
    /// must never be compared or interchanged.
    /// </summary>
    public string EmployeeProfileId { get; set; } = string.Empty;
    public EmployeeProfile? Employee { get; set; }
    public DateTime At { get; set; }
    public AttendanceEventType Type { get; set; }
}
