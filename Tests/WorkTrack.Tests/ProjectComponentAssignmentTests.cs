using Application.ProjectComponents.Queries;
using Application.Projects.Commands;
using Application.Projects.DTOs;
using Application.Projects.Queries;
using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Components are an org-wide catalogue of the deliverables projects are made of.
/// A project declares the subset that applies to it, through the
/// <see cref="ProjectComponentAssignment"/> join — the same shape as
/// <see cref="ProjectActivityAssignment"/>, minus the fallback: a project that
/// declares no components has none, rather than all of them.
///
/// These run against <c>TransactionalTestDb</c> rather than the in-memory provider
/// because most of what is worth pinning here is constraint behaviour the
/// in-memory provider does not enforce: the composite key, the foreign keys, and
/// the cascade that clears assignments when a component is deleted.
/// </summary>
public class ProjectComponentAssignmentTests
{
    private static ProjectComponent Component(int id, string name, bool isActive = true) => new()
    {
        Id = id,
        Name = name,
        Description = string.Empty,
        Icon = "🧩",
        ColorKey = "default",
        IsActive = isActive,
    };

    private static UpsertProjectRequest NewProjectRequest(string name, string code, params int[] componentIds) => new()
    {
        Name = name,
        Code = code,
        Description = string.Empty,
        Status = ProjectStatus.Active,
        IsActive = true,
        ColorKey = "p1",
        ComponentIds = componentIds.ToList(),
    };

    [Fact]
    public async Task Create_persists_the_selected_components()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        context.ProjectComponents.AddRange(
            Component(1, "DM"),
            Component(2, "Lasernet"),
            Component(3, "jDocs"));
        await context.SaveChangesAsync();

        var handler = new CreateProject.Handler(context);
        var result = await handler.Handle(
            new CreateProject.Command { Project = NewProjectRequest("Apollo", "APL-001", 1, 3) },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);

        var assigned = await context.ProjectComponentAssignments
            .Where(a => a.ProjectId == result.Value!.Id)
            .Select(a => a.ComponentId)
            .OrderBy(id => id)
            .ToListAsync();

        Assert.Equal(new[] { 1, 3 }, assigned);
    }

    [Fact]
    public async Task Create_rejects_a_component_that_does_not_exist()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        context.ProjectComponents.Add(Component(1, "DM"));
        await context.SaveChangesAsync();

        var handler = new CreateProject.Handler(context);
        var result = await handler.Handle(
            new CreateProject.Command { Project = NewProjectRequest("Apollo", "APL-001", 1, 99) },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("component", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await context.Projects.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Update_replaces_the_assignment_set()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        context.ProjectComponents.AddRange(
            Component(1, "DM"),
            Component(2, "Lasernet"),
            Component(3, "jDocs"));
        await context.SaveChangesAsync();

        var created = await new CreateProject.Handler(context).Handle(
            new CreateProject.Command { Project = NewProjectRequest("Apollo", "APL-001", 1, 2) },
            CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error);

        // Keeps 2, drops 1, adds 3.
        var updated = await new UpdateProject.Handler(context).Handle(
            new UpdateProject.Command
            {
                Id = created.Value!.Id,
                Project = NewProjectRequest("Apollo", "APL-001", 2, 3),
            },
            CancellationToken.None);

        Assert.True(updated.IsSuccess, updated.Error);

        var assigned = await context.ProjectComponentAssignments
            .AsNoTracking()
            .Where(a => a.ProjectId == created.Value!.Id)
            .Select(a => a.ComponentId)
            .OrderBy(id => id)
            .ToListAsync();

        Assert.Equal(new[] { 2, 3 }, assigned);
    }

    [Fact]
    public async Task Update_clears_every_component_when_none_are_selected()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        context.ProjectComponents.AddRange(Component(1, "DM"), Component(2, "Lasernet"));
        await context.SaveChangesAsync();

        var created = await new CreateProject.Handler(context).Handle(
            new CreateProject.Command { Project = NewProjectRequest("Apollo", "APL-001", 1, 2) },
            CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error);

        // An empty selection means the project declares no components, so it has
        // to actually empty the set rather than be read as "leave it alone".
        var updated = await new UpdateProject.Handler(context).Handle(
            new UpdateProject.Command
            {
                Id = created.Value!.Id,
                Project = NewProjectRequest("Apollo", "APL-001"),
            },
            CancellationToken.None);

        Assert.True(updated.IsSuccess, updated.Error);
        Assert.Empty(updated.Value!.Components);
        Assert.Empty(await context.ProjectComponentAssignments.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Deleting_a_component_releases_it_from_its_projects()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        context.ProjectComponents.AddRange(Component(1, "DM"), Component(2, "Lasernet"));
        await context.SaveChangesAsync();

        var created = await new CreateProject.Handler(context).Handle(
            new CreateProject.Command { Project = NewProjectRequest("Apollo", "APL-001", 1, 2) },
            CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error);

        var deleted = await new Application.ProjectComponents.Commands.DeleteProjectComponent.Handler(context)
            .Handle(new Application.ProjectComponents.Commands.DeleteProjectComponent.Command { Id = 1 }, CancellationToken.None);
        Assert.True(deleted.IsSuccess, deleted.Error);

        var assigned = await context.ProjectComponentAssignments
            .AsNoTracking()
            .Where(a => a.ProjectId == created.Value!.Id)
            .Select(a => a.ComponentId)
            .ToListAsync();

        Assert.Equal(new[] { 2 }, assigned);
    }

    [Fact]
    public async Task Created_project_is_returned_with_its_components()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        context.ProjectComponents.AddRange(Component(1, "DM"), Component(2, "Lasernet"));
        await context.SaveChangesAsync();

        var created = await new CreateProject.Handler(context).Handle(
            new CreateProject.Command { Project = NewProjectRequest("Apollo", "APL-001", 2) },
            CancellationToken.None);

        Assert.True(created.IsSuccess, created.Error);
        var component = Assert.Single(created.Value!.Components);
        Assert.Equal(2, component.Id);
        Assert.Equal("Lasernet", component.Name);
    }

    [Fact]
    public async Task Project_list_returns_the_components_of_each_project()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        context.ProjectComponents.AddRange(Component(1, "DM"), Component(2, "Lasernet"));
        await context.SaveChangesAsync();

        var create = new CreateProject.Handler(context);
        await create.Handle(new CreateProject.Command { Project = NewProjectRequest("Apollo", "APL-001", 1, 2) }, CancellationToken.None);
        await create.Handle(new CreateProject.Command { Project = NewProjectRequest("Borealis", "BOR-001") }, CancellationToken.None);

        // Listed as an admin so department scoping stays out of the way: these
        // projects are built without departments, and what is under test here is
        // the components they report. See ProjectDepartmentScopeTests for the
        // scoping itself.
        var projects = await new GetProjectList.Handler(context).Handle(
            new GetProjectList.Query { IsAdmin = true }, CancellationToken.None);

        var apollo = projects.Single(p => p.Code == "APL-001");
        Assert.Equal(new[] { "DM", "Lasernet" }, apollo.Components.Select(c => c.Name).OrderBy(n => n));

        Assert.Empty(projects.Single(p => p.Code == "BOR-001").Components);
    }

    [Fact]
    public async Task Component_list_counts_the_projects_that_use_it()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        context.ProjectComponents.AddRange(Component(1, "DM"), Component(2, "Lasernet"));
        await context.SaveChangesAsync();

        var create = new CreateProject.Handler(context);
        await create.Handle(new CreateProject.Command { Project = NewProjectRequest("Apollo", "APL-001", 1) }, CancellationToken.None);
        await create.Handle(new CreateProject.Command { Project = NewProjectRequest("Borealis", "BOR-001", 1, 2) }, CancellationToken.None);

        var components = await new GetProjectComponentList.Handler(context)
            .Handle(new GetProjectComponentList.Query(), CancellationToken.None);

        Assert.Equal(2, components.Single(c => c.Name == "DM").UsedInProjects);
        Assert.Equal(1, components.Single(c => c.Name == "Lasernet").UsedInProjects);
    }

    [Fact]
    public async Task A_deleted_project_stops_counting_towards_component_usage()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        context.ProjectComponents.Add(Component(1, "DM"));
        await context.SaveChangesAsync();

        var created = await new CreateProject.Handler(context).Handle(
            new CreateProject.Command { Project = NewProjectRequest("Apollo", "APL-001", 1) },
            CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error);

        // Projects are soft-deleted, so the assignment row survives. It must still
        // drop out of the count, or a deleted project keeps its component looking
        // in use when nothing visible to the admin references it.
        var project = await context.Projects.FindAsync(created.Value!.Id);
        project!.IsDeleted = true;
        await context.SaveChangesAsync();

        var components = await new GetProjectComponentList.Handler(context)
            .Handle(new GetProjectComponentList.Query(), CancellationToken.None);

        Assert.Equal(0, components.Single(c => c.Name == "DM").UsedInProjects);
    }
}
