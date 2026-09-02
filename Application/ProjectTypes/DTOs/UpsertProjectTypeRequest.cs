using System.ComponentModel.DataAnnotations;

namespace Application.ProjectTypes.DTOs;

public class UpsertProjectTypeRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(300)]
    public string Description { get; set; } = string.Empty;

    [StringLength(16)]
    public string Icon { get; set; } = "🗂️";

    [StringLength(30)]
    public string ColorKey { get; set; } = "default";

    public bool IsActive { get; set; } = true;
}
