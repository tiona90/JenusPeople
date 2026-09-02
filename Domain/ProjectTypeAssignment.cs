namespace Domain;

// Which types a project is classified as. ProjectType stays an org-wide
// catalogue — this picks the subset a given project is, the same way
// ProjectComponentAssignment narrows the component catalogue. A project can be
// several kinds of engagement at once (a Support project that also fields
// Inquiries), so the classification is a set rather than a single column.
// A project with no rows here is unclassified, which is a valid state.
public class ProjectTypeAssignment
{
    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public int ProjectTypeId { get; set; }
    public ProjectType? ProjectType { get; set; }
}
