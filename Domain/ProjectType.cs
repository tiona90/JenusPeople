namespace Domain;

// The kind of engagement a project is — Task, Issue, Inquiry, Support — kept
// as an org-wide catalogue admins curate, exactly as ProjectActivityType and
// ProjectComponent are. The three answer different questions about the same
// project: an activity type says what kind of work was done, a component says
// which part of the product it was done on, and a type says what the project
// itself is.
//
// Projects declare which types they are through ProjectTypeAssignment, and can
// hold several at once — a Support project that also fields Inquiries is both.
// A project with none is unclassified, which is a valid state rather than a
// missing field. Deleting a type projects still carry is refused rather than
// cascading the assignments away — see DeleteProjectType — because a
// classification an admin made should not disappear as a side effect of tidying
// the catalogue.
public class ProjectType
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public string Icon { get; set; } = "🗂️";
    public string ColorKey { get; set; } = "default";

    // Enabled types are the ones offered elsewhere; disabled ones stay in the
    // catalogue but are hidden from pickers.
    public bool IsActive { get; set; } = true;

    // The projects classified as this type. Only ever read to count them, and the
    // reason a type in use cannot be deleted.
    public ICollection<ProjectTypeAssignment> ProjectAssignments { get; set; } = new List<ProjectTypeAssignment>();
}
