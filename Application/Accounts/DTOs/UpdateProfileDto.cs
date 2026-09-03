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

    [Phone]
    [StringLength(30)]
    public string? PhoneNumber { get; set; }

    // Date only (no time). Null clears it.
    public DateOnly? DateOfBirth { get; set; }
}