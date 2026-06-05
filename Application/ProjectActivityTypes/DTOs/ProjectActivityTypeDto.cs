namespace Application.ProjectActivityTypes.DTOs;

public class ProjectActivityTypeDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = "🏷️";
    public string ColorKey { get; set; } = "default";
    public bool IsActive { get; set; }

    // Usage stats. Activity types are not yet linked to timesheet entries,
    // so these are placeholders (0) until that integration lands. They keep
    // the API shape stable for the management UI.
    public int HoursYtd { get; set; }
    public int UsedInProjects { get; set; }
}
