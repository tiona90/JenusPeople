using System.ComponentModel.DataAnnotations;

namespace Application.AdminUsers.DTOs;

public class AdminCreateUserDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    [MinLength(6)]
    public string Password { get; set; } = string.Empty;

    [MinLength(1)]
    public List<string> Roles { get; set; } = new();

    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Department is required.")]
    public int DepartmentId { get; set; }
}