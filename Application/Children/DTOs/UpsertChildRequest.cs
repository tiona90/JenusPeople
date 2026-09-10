using System.ComponentModel.DataAnnotations;

namespace Application.Children.DTOs;

public class UpsertChildRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public DateOnly DateOfBirth { get; set; }
}
