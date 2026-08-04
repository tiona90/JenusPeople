using System.ComponentModel.DataAnnotations;

namespace Application.AdminUsers.DTOs;

/// <summary>
/// An administrator creating an account never sets a password: the account is
/// created without one and the new user chooses their own from the link in the
/// welcome email. So there is deliberately no password field here.
/// </summary>
public class AdminCreateUserDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Display name is required.")]
    [StringLength(100, MinimumLength = 2)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Exactly one role per user (Admin, Manager or Employee).</summary>
    [MinLength(1, ErrorMessage = "A role is required.")]
    [MaxLength(1, ErrorMessage = "A user can have only one role.")]
    public List<string> Roles { get; set; } = new();

    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Department is required.")]
    public int DepartmentId { get; set; }

    [Phone]
    [StringLength(30)]
    public string? PhoneNumber { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    /// <summary>
    /// The profile id of who this person reports to. The admin UI derives this
    /// from the department's manager rather than letting it be hand-picked —
    /// see AdminUsersPanel's EditUserDialog — but the field stays free-form
    /// here too, matching EditEmployeeProfileRequest, so validation is the
    /// same for both.
    /// </summary>
    public string? ManagerId { get; set; }

    [StringLength(150)]
    public string? JobTitle { get; set; }

    /// <summary>Defaults to 20 when omitted — see CreateUser in AdminUsersController.</summary>
    [Range(0, 365)]
    public int? AnnualLeaveEntitlement { get; set; }
}