namespace Domain;

// Which components a project is made up of. ProjectComponent stays an org-wide
// catalogue — this picks the subset a given project actually delivers, the same
// way ProjectActivityAssignment narrows the activity catalogue. A project with
// no rows here has declared no components.
public class ProjectComponentAssignment
{
    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public int ComponentId { get; set; }
    public ProjectComponent? Component { get; set; }
}
