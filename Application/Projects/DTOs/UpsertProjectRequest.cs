using System.ComponentModel.DataAnnotations;
using Domain;

namespace Application.Projects.DTOs;

public class UpsertProjectRequest
{
    [Required]
    [StringLength(150, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(20, MinimumLength = 1)]
    public string Code { get; set; } = string.Empty;

    [StringLength(500)]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// The departments this project belongs to. At least one is required — a
    /// project with none is visible to nobody but an admin.
    /// </summary>
    public List<int> DepartmentIds { get; set; } = new();

    [StringLength(450)]
    public string? OwnerId { get; set; }

    public ProjectStatus Status { get; set; } = ProjectStatus.Active;

    public bool IsActive { get; set; } = true;

    [StringLength(8)]
    public string ColorKey { get; set; } = "p1";

    [Range(0, 1000)]
    public int TargetWeeklyHours { get; set; }

    [Range(0, 5000)]
    public int TargetMonthlyHours { get; set; }

    /// <summary>
    /// The activity types this project logs time against. Empty means the project
    /// has not narrowed the catalogue, and every active activity type applies.
    /// </summary>
    public List<int> ActivityTypeIds { get; set; } = new();

    /// <summary>
    /// The components this project is made up of. Empty means the project has
    /// declared none — unlike activities, nothing falls back to the catalogue.
    /// </summary>
    public List<int> ComponentIds { get; set; } = new();

    /// <summary>
    /// What kinds of engagement this project is. Empty leaves it unclassified,
    /// which is a valid state rather than a missing field.
    /// </summary>
    public List<int> ProjectTypeIds { get; set; } = new();
}
