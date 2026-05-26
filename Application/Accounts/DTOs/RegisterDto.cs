using System.ComponentModel.DataAnnotations;

namespace Application.Accounts.DTOs;

public class RegisterDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MinLength(6)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    [Range(1, int.MaxValue)]
    public int DepartmentId { get; set; }
}