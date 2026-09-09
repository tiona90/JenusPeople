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

    // No leave numbers on purpose. AnnualLeaveEntitlement and LeaveBalance are
    // written from the Leave Types allowance (CreateAdminUser on hire, UpdateLeaveType
    // when it moves), never per person from this screen. When they were here, a dialog
    // that no longer showed the field still echoed a value back — and an omitted one
    // arrived as 0, which switches the balance check off (AnnualLeaveBalanceCalculator).

    [StringLength(100)]
    public string? JobTitle { get; set; }
}