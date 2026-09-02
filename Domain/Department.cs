namespace Domain;

public class Department
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<UserDepartment> UserDepartments { get; set; } = new List<UserDepartment>();
    public ICollection<EmployeeProfile> EmployeeProfiles { get; set; } = new List<EmployeeProfile>();
    public ICollection<AnnualLeave> AnnualLeaves { get; set; } = new List<AnnualLeave>();
    public ICollection<ProjectDepartment> ProjectAssignments { get; set; } = new List<ProjectDepartment>();
    public ICollection<Timesheet> Timesheets { get; set; } = new List<Timesheet>();
}
