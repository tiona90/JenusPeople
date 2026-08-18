namespace Application.EmployeeProfiles.DTOs;

/// <summary>
/// Minimal colleague card used by pickers (e.g. nominating leave coverage).
/// Deliberately carries no entitlement or balance figures — any authenticated
/// user may read this, unlike <see cref="EmployeeProfileDto"/>.
/// </summary>
public class TeammateDto
{
    public string UserId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? JobTitle { get; set; }

    public int DepartmentId { get; set; }
}
