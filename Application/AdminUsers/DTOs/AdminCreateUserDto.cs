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
}