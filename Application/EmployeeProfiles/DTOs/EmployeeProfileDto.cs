namespace Application.EmployeeProfiles.DTOs;

public class EmployeeProfileDto
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int? DepartmentId { get; set; }
    public string? ManagerId { get; set; }
    public int AnnualLeaveEntitlement { get; set; }
    public int LeaveBalance { get; set; }
    public string? JobTitle { get; set; }
    public DateTime CreatedAt { get; set; }
}
