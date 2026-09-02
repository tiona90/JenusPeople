namespace Domain;

// Which departments a project belongs to, and by consequence who can see it:
// a project reaches the people in its departments and nobody else. A project
// with no rows here reaches nobody at all, which is why the upsert request
// insists on at least one.
public class ProjectDepartment
{
    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public int DepartmentId { get; set; }
    public Department? Department { get; set; }
}
