namespace Domain;

// A category of work that time can be logged against on projects
// (Development, Testing, Design, …). Defined org-wide, mirroring how LeaveType
// is configured, but a project narrows the catalogue to the activities it
// actually does through ProjectActivityAssignment. A project that assigns none
// offers all of them.
public class ProjectActivityType
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public string Icon { get; set; } = "🏷️";
    public string ColorKey { get; set; } = "default";

    // Enabled types are available when logging time; disabled ones are hidden.
    public bool IsActive { get; set; } = true;

    // The projects that have opted into this activity type.
    public ICollection<ProjectActivityAssignment> ProjectAssignments { get; set; } = new List<ProjectActivityAssignment>();
}
