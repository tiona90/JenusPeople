namespace Application.ProjectActivityTypes.DTOs;

public class ProjectActivityTypeDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = "🏷️";
    public string ColorKey { get; set; } = "default";
    public bool IsActive { get; set; }

    // Hours logged against this activity so far this year. Timesheet entries do
    // carry an ActivityTypeId, but nothing aggregates them yet — a placeholder (0)
    // that keeps the API shape stable for the management UI.
    public int HoursYtd { get; set; }

    // How many projects have assigned this activity type.
    public int UsedInProjects { get; set; }
}
