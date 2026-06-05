namespace Domain;

// A category of work that time can be logged against on projects
// (Development, Testing, Design, …). Global / org-wide — shared across
// all projects, mirroring how LeaveType is configured org-wide.
public class ProjectActivityType
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public string Icon { get; set; } = "🏷️";
    public string ColorKey { get; set; } = "default";

    // Enabled types are available when logging time; disabled ones are hidden.
    public bool IsActive { get; set; } = true;
}
