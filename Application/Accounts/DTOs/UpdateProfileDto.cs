using System.ComponentModel.DataAnnotations;

namespace Application.Accounts.DTOs;

public class UpdateProfileDto
{
    [Required]
    [StringLength(100)]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Department is required.")]
    public int DepartmentId { get; set; }
}