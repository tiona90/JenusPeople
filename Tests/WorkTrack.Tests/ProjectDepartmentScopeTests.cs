using Application.Departments.Commands;
using Application.Projects.Commands;
using Application.Projects.DTOs;
using Application.Projects.Queries;
using Application.Projects.Validators;
using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// A project used to carry a single, optional department that was never read for
/// anything but a label — <c>GET /api/projects</c> returned every project to every
/// authenticated caller. It now carries one or more departments through the
/// <see cref="ProjectDepartment"/> join, and those departments decide who sees it:
/// an admin sees all, everyone else sees the projects sharing a department with
/// them, and a project with no departments is therefore seen by nobody.
///
/// The persistence and delete-blocking tests use <c>TransactionalTestDb</c>, since
/// composite keys and restricting foreign keys are exactly what the in-memory
/// provider does not enforce. The scoping tests are query behaviour, so they use
/// the faster in-memory provider like the other scope tests do.
/// </summary>
public class ProjectDepartmentScopeTests
{
    private const int Engineering = 1;
    private const int Finance = 2;

    private static void SeedDepartments(AppDbContext db) =>
        db.Departments.AddRange(
            new Department { Id = Engineering, Name = "Engineering", Code = "ENG" },
            new Department { Id = Finance, Name = "Finance", Code = "FIN" });

    private static UpsertProjectRequest NewProjectRequest(string name, string code, params int[] departmentIds) => new()
    {
        Name = name,
        Code = code,
        Description = string.Empty,
        Status = ProjectStatus.Active,
        IsActive = true,
        ColorKey = "p1",
        DepartmentIds = departmentIds.ToList(),
    };

    private static Project ProjectIn(string name, string code, params int[] departmentIds) => new()
    {
        Name = name,
        Code = code,
        Description = string.Empty,
        ColorKey = "p1",
        DepartmentAssignments = departmentIds
            .Select(id => new ProjectDepartment { DepartmentId = id })
            .ToList(),
    };

    private static Task<List<ProjectDto>> VisibleTo(
        AppDbContext db, string userId, bool isAdmin = false, bool isManager = false) =>
        new GetProjectList.Handler(db).Handle(
            new GetProjectList.Query { RequestingUserId = userId, IsAdmin = isAdmin, IsManager = isManager },
            CancellationToken.None);

    // ── Assignment ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_persists_the_selected_departments()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        SeedDepartments(context);
        await context.SaveChangesAsync();

        var result = await new CreateProject.Handler(context).Handle(
            new CreateProject.Command { Project = NewProjectRequest("Apollo", "APL-001", Engineering, Finance) },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);

        var assigned = await context.ProjectDepartments
            .Where(a => a.ProjectId == result.Value!.Id)
            .Select(a => a.DepartmentId)
            .OrderBy(id => id)
            .ToListAsync();

        Assert.Equal(new[] { Engineering, Finance }, assigned);
    }

    [Fact]
    public async Task Create_rejects_a_department_that_does_not_exist()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        SeedDepartments(context);
        await context.SaveChangesAsync();

        var result = await new CreateProject.Handler(context).Handle(
            new CreateProject.Command { Project = NewProjectRequest("Apollo", "APL-001", Engineering, 99) },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(await context.ProjectDepartments.ToListAsync());
    }

    [Fact]
    public async Task Update_replaces_the_department_set()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        SeedDepartments(context);
        var project = ProjectIn("Apollo", "APL-001", Engineering);
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        var result = await new UpdateProject.Handler(context).Handle(
            new UpdateProject.Command
            {
                Id = project.Id,
                Project = NewProjectRequest("Apollo", "APL-001", Finance),
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(new[] { Finance }, result.Value!.Departments.Select(d => d.Id));

        var assigned = await context.ProjectDepartments
            .Where(a => a.ProjectId == project.Id)
            .Select(a => a.DepartmentId)
            .ToListAsync();

        Assert.Equal(new[] { Finance }, assigned);
    }

    // A project nobody can see is a support ticket waiting to happen, so the
    // dialog is not allowed to produce one in the first place.
    [Fact]
    public void A_project_must_be_given_at_least_one_department()
    {
        var result = new UpsertProjectRequestValidator()
            .Validate(NewProjectRequest("Apollo", "APL-001"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpsertProjectRequest.DepartmentIds));
    }

    // ── Visibility ──────────────────────────────────────────────────────────────

    private static void SeedThreeProjects(AppDbContext db)
    {
        SeedDepartments(db);
        db.Projects.AddRange(
            ProjectIn("Apollo", "APL-001", Engineering),
            ProjectIn("Borealis", "BOR-002", Finance),
            ProjectIn("Orphan", "ORP-003"));
    }

    [Fact]
    public async Task An_employee_sees_only_projects_in_their_department()
    {
        using var db = TestDb.Create();
        SeedThreeProjects(db);
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "ep", UserId = "emp", DepartmentId = Engineering });
        await db.SaveChangesAsync();

        var visible = await VisibleTo(db, "emp");

        Assert.Equal(new[] { "Apollo" }, visible.Select(p => p.Name));
    }

    [Fact]
    public async Task A_manager_also_sees_the_departments_they_manage()
    {
        using var db = TestDb.Create();
        SeedThreeProjects(db);
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "mp", UserId = "mgr", DepartmentId = Engineering });
        db.UserDepartments.Add(new UserDepartment { UserId = "mgr", DepartmentId = Finance });
        await db.SaveChangesAsync();

        var visible = await VisibleTo(db, "mgr", isManager: true);

        Assert.Equal(new[] { "Apollo", "Borealis" }, visible.Select(p => p.Name));
    }

    // The whole point of the rule: a project with no departments reaches nobody.
    [Fact]
    public async Task A_project_with_no_departments_is_invisible_to_a_non_admin()
    {
        using var db = TestDb.Create();
        SeedThreeProjects(db);
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "ep", UserId = "emp", DepartmentId = Engineering });
        db.UserDepartments.Add(new UserDepartment { UserId = "emp", DepartmentId = Finance });
        await db.SaveChangesAsync();

        var visible = await VisibleTo(db, "emp", isManager: true);

        Assert.DoesNotContain(visible, p => p.Name == "Orphan");
    }

    // Otherwise a project saved without a department could never be repaired.
    [Fact]
    public async Task An_admin_sees_every_project_including_ones_with_no_department()
    {
        using var db = TestDb.Create();
        SeedThreeProjects(db);
        await db.SaveChangesAsync();

        var visible = await VisibleTo(db, "admin", isAdmin: true);

        Assert.Equal(new[] { "Apollo", "Borealis", "Orphan" }, visible.Select(p => p.Name));
    }

    [Fact]
    public async Task A_user_with_no_department_at_all_sees_nothing()
    {
        using var db = TestDb.Create();
        SeedThreeProjects(db);
        await db.SaveChangesAsync();

        Assert.Empty(await VisibleTo(db, "nobody"));
    }

    [Fact]
    public async Task The_project_list_reports_every_department_a_project_belongs_to()
    {
        using var db = TestDb.Create();
        SeedDepartments(db);
        db.Projects.Add(ProjectIn("Apollo", "APL-001", Engineering, Finance));
        await db.SaveChangesAsync();

        var visible = await VisibleTo(db, "admin", isAdmin: true);

        Assert.Equal(
            new[] { "Engineering", "Finance" },
            visible.Single().Departments.Select(d => d.Name));
    }

    // ── Department deletion ─────────────────────────────────────────────────────

    // The join restricts rather than cascades, so without this the delete surfaces
    // as a raw database error instead of the conflict the admin can act on.
    [Fact]
    public async Task Deleting_a_department_that_still_has_projects_is_blocked()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        SeedDepartments(context);
        context.Projects.Add(ProjectIn("Apollo", "APL-001", Engineering));
        await context.SaveChangesAsync();

        var result = await new DeleteDepartment.Handler(context).Handle(
            new DeleteDepartment.Command { Id = Engineering },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("project", result.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(await context.Departments.FindAsync(Engineering));
    }
}
