namespace Domain;

// Which activity types apply to a project. ProjectActivityType stays an org-wide
// catalogue — this picks the subset a given project logs time against, so the
// activity dropdown on a timesheet row can be narrowed to the work that project
// actually does. A project with no rows here falls back to the whole catalogue.
public class ProjectActivityAssignment
{
    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public int ActivityTypeId { get; set; }
    public ProjectActivityType? ActivityType { get; set; }
}
