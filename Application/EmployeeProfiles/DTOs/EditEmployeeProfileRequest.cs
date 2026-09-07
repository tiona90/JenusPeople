using System.ComponentModel.DataAnnotations;

namespace Application.EmployeeProfiles.DTOs;

public class EditEmployeeProfileRequest
{
    [Required]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Null only for an Admin, who has no department. Which roles may leave it
    /// blank and which must fill it in is role-dependent, so it cannot be settled
    /// by an annotation here — see <c>EditEmployeeProfileRequestValidator</c>.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int? DepartmentId { get; set; }

    public string? ManagerId { get; set; }

    [Range(0, 365)]
    public int AnnualLeaveEntitlement { get; set; }

    [Range(0, 365)]
    public int LeaveBalance { get; set; }

    [StringLength(100)]
    public string? JobTitle { get; set; }
}