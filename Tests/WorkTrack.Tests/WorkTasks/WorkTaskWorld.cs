using Domain;
using Persistence;

namespace WorkTrack.Tests.WorkTasks;

/// <summary>
/// Two departments and five people:
/// Sales (1) — Manager "u-mgr-sales" (profile), HR "u-hr" (UserDepartment row);
/// Ops (2) — Manager "u-mgr-ops" (profile), and "u-mgr-gone" (deactivated, profile);
/// "u-emp" — an Employee in Sales, who is never eligible.
/// </summary>
internal static class WorkTaskWorld
{
    public const int Sales = 1;
    public const int Ops = 2;
    public const string SalesManager = "u-mgr-sales";
    public const string OpsManager = "u-mgr-ops";
    public const string GoneManager = "u-mgr-gone";
    public const string Hr = "u-hr";
    public const string Employee = "u-emp";
    /// <summary>Active, assigned to Sales.</summary>
    public const int SalesProject = 10;
    /// <summary>Assigned to Sales but switched off.</summary>
    public const int InactiveSalesProject = 11;
    /// <summary>Active, assigned to Ops.</summary>
    public const int OpsProject = 12;

    public static async Task SeedAsync(AppDbContext db)
    {
        db.Departments.Add(new Department { Id = Sales, Name = "Sales", Code = "SAL" });
        db.Departments.Add(new Department { Id = Ops, Name = "Ops", Code = "OPS" });

        db.Roles.Add(new Role { Id = "r-mgr", Name = AppRoles.Manager, NormalizedName = AppRoles.Manager.ToUpperInvariant() });
        db.Roles.Add(new Role { Id = "r-hr", Name = AppRoles.HrAdministrator, NormalizedName = AppRoles.HrAdministrator.ToUpperInvariant() });
        db.Roles.Add(new Role { Id = "r-emp", Name = AppRoles.Employee, NormalizedName = AppRoles.Employee.ToUpperInvariant() });

        AddUser(db, SalesManager, "Sam Sales", "r-mgr", Sales);
        AddUser(db, OpsManager, "Olga Ops", "r-mgr", Ops);
        AddUser(db, GoneManager, "Gary Gone", "r-mgr", Ops, isActive: false);
        AddUser(db, Hr, "Hana HR", "r-hr", departmentId: null);
        AddUser(db, Employee, "Eve Employee", "r-emp", Sales);

        db.UserDepartments.Add(new UserDepartment { UserId = Hr, DepartmentId = Sales });

        db.Projects.Add(new Project { Id = SalesProject, Name = "CRM Rollout", Code = "CRM", ColorKey = "p3" });
        db.Projects.Add(new Project { Id = InactiveSalesProject, Name = "Legacy", Code = "LEG", IsActive = false });
        db.Projects.Add(new Project { Id = OpsProject, Name = "Ops Tooling", Code = "OPT" });
        db.ProjectDepartments.Add(new ProjectDepartment { ProjectId = SalesProject, DepartmentId = Sales });
        db.ProjectDepartments.Add(new ProjectDepartment { ProjectId = InactiveSalesProject, DepartmentId = Sales });
        db.ProjectDepartments.Add(new ProjectDepartment { ProjectId = OpsProject, DepartmentId = Ops });

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    public static WorkTask NewTask(int departmentId, string createdBy, string assignee, string title = "Task",
        WorkTaskStatus status = WorkTaskStatus.ToDo) => new()
    {
        Title = title,
        DepartmentId = departmentId,
        ProjectId = departmentId == Sales ? SalesProject : OpsProject,
        CreatedById = createdBy,
        Assignees = [new WorkTaskAssignee { UserId = assignee }],
        Status = status,
        CreatedAtUtc = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc),
        UpdatedAtUtc = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc),
    };

    private static void AddUser(AppDbContext db, string id, string name, string roleId, int? departmentId, bool isActive = true)
    {
        db.Users.Add(new User { Id = id, UserName = $"{id}@t", Email = $"{id}@t", DisplayName = name, IsActive = isActive });
        db.UserRoles.Add(new UserRole { UserId = id, RoleId = roleId });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = $"p-{id}", UserId = id, DepartmentId = departmentId });
    }
}
