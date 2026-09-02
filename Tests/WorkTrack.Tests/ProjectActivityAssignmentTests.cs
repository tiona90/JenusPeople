using Application.ProjectActivityTypes.Queries;
using Application.Projects.Commands;
using Application.Projects.DTOs;
using Application.Projects.Queries;
using Domain;
using Microsoft.EntityFrameworkCore;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Activity types are an org-wide catalogue, and every project used to see all of
/// them. A project now selects the subset that applies to it, through the
/// <see cref="ProjectActivityAssignment"/> join.
///
/// These run against <c>TransactionalTestDb</c> rather than the in-memory provider
/// because most of what is worth pinning here is constraint behaviour the
/// in-memory provider does not enforce: the composite key, the foreign keys, and
/// the cascade that clears assignments when an activity type is deleted.
/// </summary>
public class ProjectActivityAssignmentTests
{
    private static ProjectActivityType ActivityType(int id, string name, bool isActive = true) => new()
    {
        Id = id,
        Name = name,
        Description = string.Empty,
        Icon = "🏷️",
        ColorKey = "default",
        IsActive = isActive,
    };

    private static UpsertProjectRequest NewProjectRequest(string name, string code, params int[] activityTypeIds) => new()
    {
        Name = name,
        Code = code,
        Description = string.Empty,
        Status = ProjectStatus.Active,
        IsActive = true,
        ColorKey = "p1",
        ActivityTypeIds = activityTypeIds.ToList(),
    };

    [Fact]
    public async Task Create_persists_the_selected_activities()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        context.ProjectActivityTypes.AddRange(
            ActivityType(1, "Development"),
            ActivityType(2, "Testing"),
            ActivityType(3, "Design"));
        await context.SaveChangesAsync();

        var handler = new CreateProject.Handler(context);
        var result = await handler.Handle(
            new CreateProject.Command { Project = NewProjectRequest("Apollo", "APL-001", 1, 3) },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);

        var assigned = await context.ProjectActivityAssignments
            .Where(a => a.ProjectId == result.Value!.Id)
            .Select(a => a.ActivityTypeId)
            .OrderBy(id => id)
            .ToListAsync();

        Assert.Equal(new[] { 1, 3 }, assigned);
    }

    [Fact]
    public async Task Create_rejects_an_activity_type_that_does_not_exist()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        context.ProjectActivityTypes.Add(ActivityType(1, "Development"));
        await context.SaveChangesAsync();

        var handler = new CreateProject.Handler(context);
        var result = await handler.Handle(
            new CreateProject.Command { Project = NewProjectRequest("Apollo", "APL-001", 1, 99) },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("activity", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await context.Projects.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Update_replaces_the_assignment_set()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        context.ProjectActivityTypes.AddRange(
            ActivityType(1, "Development"),
            ActivityType(2, "Testing"),
            ActivityType(3, "Design"));
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

        var assigned = await context.ProjectActivityAssignments
            .AsNoTracking()
            .Where(a => a.ProjectId == created.Value!.Id)
            .Select(a => a.ActivityTypeId)
            .OrderBy(id => id)
            .ToListAsync();

        Assert.Equal(new[] { 2, 3 }, assigned);
    }

    [Fact]
    public async Task Deleting_an_activity_type_releases_it_from_its_projects()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        context.ProjectActivityTypes.AddRange(
            ActivityType(1, "Development"),
            ActivityType(2, "Testing"));
        await context.SaveChangesAsync();

        var created = await new CreateProject.Handler(context).Handle(
            new CreateProject.Command { Project = NewProjectRequest("Apollo", "APL-001", 1, 2) },
            CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error);

        var deleted = await new Application.ProjectActivityTypes.Commands.DeleteProjectActivityType.Handler(context)
            .Handle(new Application.ProjectActivityTypes.Commands.DeleteProjectActivityType.Command { Id = 1 }, CancellationToken.None);
        Assert.True(deleted.IsSuccess, deleted.Error);

        var assigned = await context.ProjectActivityAssignments
            .AsNoTracking()
            .Where(a => a.ProjectId == created.Value!.Id)
            .Select(a => a.ActivityTypeId)
            .ToListAsync();

        Assert.Equal(new[] { 2 }, assigned);
    }

    [Fact]
    public async Task Created_project_is_returned_with_its_activities()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        context.ProjectActivityTypes.AddRange(
            ActivityType(1, "Development"),
            ActivityType(2, "Testing"));
        await context.SaveChangesAsync();

        var created = await new CreateProject.Handler(context).Handle(
            new CreateProject.Command { Project = NewProjectRequest("Apollo", "APL-001", 2) },
            CancellationToken.None);

        Assert.True(created.IsSuccess, created.Error);
        var activity = Assert.Single(created.Value!.Activities);
        Assert.Equal(2, activity.Id);
        Assert.Equal("Testing", activity.Name);
    }

    [Fact]
    public async Task Project_list_returns_each_projects_activities()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        context.ProjectActivityTypes.AddRange(
            ActivityType(1, "Development"),
            ActivityType(2, "Testing"));
        await context.SaveChangesAsync();

        var create = new CreateProject.Handler(context);
        await create.Handle(new CreateProject.Command { Project = NewProjectRequest("Apollo", "APL-001", 1, 2) }, CancellationToken.None);
        await create.Handle(new CreateProject.Command { Project = NewProjectRequest("Borealis", "BOR-001") }, CancellationToken.None);

        var projects = await new GetProjectList.Handler(context).Handle(new GetProjectList.Query(), CancellationToken.None);

        var apollo = projects.Single(p => p.Code == "APL-001");
        Assert.Equal(new[] { "Development", "Testing" }, apollo.Activities.Select(a => a.Name).OrderBy(n => n));

        // A project that has narrowed nothing reports no activities, which is what
        // tells the timesheet UI to offer the whole catalogue.
        Assert.Empty(projects.Single(p => p.Code == "BOR-001").Activities);
    }

    [Fact]
    public async Task Activity_type_list_counts_the_projects_that_use_it()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        context.ProjectActivityTypes.AddRange(
            ActivityType(1, "Development"),
            ActivityType(2, "Testing"));
        await context.SaveChangesAsync();

        var create = new CreateProject.Handler(context);
        await create.Handle(new CreateProject.Command { Project = NewProjectRequest("Apollo", "APL-001", 1) }, CancellationToken.None);
        await create.Handle(new CreateProject.Command { Project = NewProjectRequest("Borealis", "BOR-001", 1, 2) }, CancellationToken.None);

        var types = await new GetProjectActivityTypeList.Handler(context)
            .Handle(new GetProjectActivityTypeList.Query(), CancellationToken.None);

        Assert.Equal(2, types.Single(t => t.Name == "Development").UsedInProjects);
        Assert.Equal(1, types.Single(t => t.Name == "Testing").UsedInProjects);
    }

    [Fact]
    public async Task A_deleted_project_stops_counting_towards_activity_usage()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        context.ProjectActivityTypes.Add(ActivityType(1, "Development"));
        await context.SaveChangesAsync();

        var created = await new CreateProject.Handler(context).Handle(
            new CreateProject.Command { Project = NewProjectRequest("Apollo", "APL-001", 1) },
            CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error);

        // Projects are soft-deleted, so the assignment row survives. It must still
        // drop out of the count, or a deleted project keeps its activity looking
        // in use and blocks nothing visible to the admin.
        var project = await context.Projects.FindAsync(created.Value!.Id);
        project!.IsDeleted = true;
        await context.SaveChangesAsync();

        var types = await new GetProjectActivityTypeList.Handler(context)
            .Handle(new GetProjectActivityTypeList.Query(), CancellationToken.None);

        Assert.Equal(0, types.Single(t => t.Name == "Development").UsedInProjects);
    }
}
