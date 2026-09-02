using Application.Core;
using Application.ProjectTypes.Commands;
using Application.ProjectTypes.DTOs;
using Application.ProjectTypes.Queries;
using Application.ProjectTypes.Validators;
using AutoMapper;
using Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// A project type is the kind of engagement a project is — Task, Issue,
/// Inquiry, Support — kept as an org-wide catalogue that admins curate,
/// exactly as <see cref="ProjectComponent"/> is. A project holds a set of them
/// rather than one, so two things have to hold: the catalogue stays free of
/// duplicate names, since the name is what identifies a type to everyone reading
/// it, and a type projects still carry cannot be deleted out from under them.
///
/// The duplicate-name tests come in a pair on purpose: the validator produces the
/// message a user sees, and the unique index is what actually holds when two
/// requests race past the validator together. Only the second needs a database
/// that enforces constraints, which is why these use
/// <see cref="TransactionalTestDb"/> rather than the in-memory provider.
/// </summary>
public class ProjectTypeTests
{
    private static IMapper BuildMapper() =>
        new MapperConfiguration(
            cfg => cfg.AddProfile<MappingProfiles>(),
            NullLoggerFactory.Instance).CreateMapper();

    private static UpsertProjectTypeRequest Payload(
        string name = "Implementation",
        string description = "New delivery for a customer.",
        string icon = "🚀",
        string colorKey = "blue",
        bool isActive = true) => new()
        {
            Name = name,
            Description = description,
            Icon = icon,
            ColorKey = colorKey,
            IsActive = isActive,
        };

    private static Task<Result<ProjectTypeDto>> Create(AppDbContext db, UpsertProjectTypeRequest payload) =>
        new CreateProjectType.Handler(db, BuildMapper()).Handle(
            new CreateProjectType.Command { Type = payload }, CancellationToken.None);

    private static Task<Result<ProjectTypeDto>> Update(AppDbContext db, int id, UpsertProjectTypeRequest payload) =>
        new UpdateProjectType.Handler(db, BuildMapper()).Handle(
            new UpdateProjectType.Command { Id = id, Type = payload }, CancellationToken.None);

    private static Task<Result<MediatR.Unit>> Delete(AppDbContext db, int id) =>
        new DeleteProjectType.Handler(db).Handle(
            new DeleteProjectType.Command { Id = id }, CancellationToken.None);

    private static Task<List<ProjectTypeDto>> List(AppDbContext db) =>
        new GetProjectTypeList.Handler(db).Handle(
            new GetProjectTypeList.Query(), CancellationToken.None);

    /// <summary>
    /// A project classified as <paramref name="projectTypeIds"/>, added straight
    /// to the context rather than through CreateProject: these tests are about
    /// what the type catalogue does when projects point at it, not about project
    /// creation, and the command would drag departments and owners in with it.
    /// Passing no ids leaves the project unclassified.
    /// </summary>
    private static async Task<Project> AddProject(AppDbContext db, string name, string code, params int[] projectTypeIds)
    {
        var project = new Project { Name = name, Code = code };
        foreach (var id in projectTypeIds)
            project.TypeAssignments.Add(new ProjectTypeAssignment { ProjectTypeId = id });
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project;
    }

    /* ── Create ─────────────────────────────────────────────────────────────── */

    [Fact]
    public async Task Create_persists_the_project_type()
    {
        await using var context = await TransactionalTestDb.CreateAsync();

        var result = await Create(context, Payload());

        Assert.True(result.IsSuccess, result.Error);

        var stored = await context.ProjectTypes.SingleAsync();
        Assert.Equal("Implementation", stored.Name);
        Assert.Equal("New delivery for a customer.", stored.Description);
        Assert.Equal("🚀", stored.Icon);
        Assert.Equal("blue", stored.ColorKey);
        Assert.True(stored.IsActive);
        Assert.Equal(stored.Id, result.Value!.Id);
    }

    /// <summary>
    /// The name is the catalogue identity and is compared for uniqueness after
    /// trimming, so a stored " Support " would be a duplicate the validator
    /// cannot see.
    /// </summary>
    [Fact]
    public async Task Create_trims_the_name()
    {
        await using var context = await TransactionalTestDb.CreateAsync();

        await Create(context, Payload(name: "  Support  "));

        Assert.Equal("Support", (await context.ProjectTypes.SingleAsync()).Name);
    }

    [Fact]
    public async Task Create_falls_back_to_the_default_icon_and_colour_when_none_is_given()
    {
        await using var context = await TransactionalTestDb.CreateAsync();

        await Create(context, Payload(icon: "", colorKey: " "));

        var stored = await context.ProjectTypes.SingleAsync();
        Assert.Equal("🗂️", stored.Icon);
        Assert.Equal("default", stored.ColorKey);
    }

    /* ── Duplicate names ────────────────────────────────────────────────────── */

    [Fact]
    public async Task A_name_already_in_the_catalogue_is_rejected_regardless_of_case()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        await Create(context, Payload(name: "Support"));

        var result = await new CreateProjectTypeRequestValidator(context)
            .ValidateAsync(new CreateProjectType.Command { Type = Payload(name: "support") });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "A project type with that name already exists.");
    }

    /// <summary>
    /// What holds when two creates pass the validator at the same moment: the
    /// second write fails rather than landing a second "Support" in the catalogue.
    /// </summary>
    [Fact]
    public async Task The_database_refuses_a_duplicate_name_the_validator_did_not_catch()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        await Create(context, Payload(name: "Support"));

        await Assert.ThrowsAsync<DbUpdateException>(() => Create(context, Payload(name: "Support")));
    }

    [Fact]
    public async Task Renaming_a_type_onto_a_name_another_one_holds_is_rejected()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        await Create(context, Payload(name: "Support"));
        var internalWork = await Create(context, Payload(name: "Internal"));

        var result = await new UpdateProjectTypeRequestValidator(context)
            .ValidateAsync(new UpdateProjectType.Command
            {
                Id = internalWork.Value!.Id,
                Type = Payload(name: "Support"),
            });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "A project type with that name already exists.");
    }

    /// <summary>
    /// Saving a type name back unchanged is an edit, not a collision — the
    /// uniqueness check has to exclude the row being updated.
    /// </summary>
    [Fact]
    public async Task Keeping_the_existing_name_while_editing_a_type_is_allowed()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        var support = await Create(context, Payload(name: "Support"));

        var result = await new UpdateProjectTypeRequestValidator(context)
            .ValidateAsync(new UpdateProjectType.Command
            {
                Id = support.Value!.Id,
                Type = Payload(name: "Support", description: "Renamed description."),
            });

        Assert.True(result.IsValid);
    }

    /* ── Update ─────────────────────────────────────────────────────────────── */

    [Fact]
    public async Task Update_overwrites_the_stored_type()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        var created = await Create(context, Payload(name: "Support", isActive: true));

        var result = await Update(context, created.Value!.Id, Payload(
            name: "Support", description: "Incidents and small changes.", icon: "🛠️", colorKey: "amber", isActive: false));

        Assert.True(result.IsSuccess, result.Error);

        var stored = await context.ProjectTypes.SingleAsync();
        Assert.Equal("Incidents and small changes.", stored.Description);
        Assert.Equal("🛠️", stored.Icon);
        Assert.Equal("amber", stored.ColorKey);
        Assert.False(stored.IsActive);
    }

    [Fact]
    public async Task Update_reports_a_type_that_does_not_exist()
    {
        await using var context = await TransactionalTestDb.CreateAsync();

        var result = await Update(context, 404, Payload());

        Assert.False(result.IsSuccess);
        Assert.Equal("Project type not found.", result.Error);
    }

    /* ── Delete ─────────────────────────────────────────────────────────────── */

    [Fact]
    public async Task Delete_removes_the_type()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        var created = await Create(context, Payload());

        var result = await Delete(context, created.Value!.Id);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(await context.ProjectTypes.ToListAsync());
    }

    [Fact]
    public async Task Delete_reports_a_type_that_does_not_exist()
    {
        await using var context = await TransactionalTestDb.CreateAsync();

        var result = await Delete(context, 404);

        Assert.False(result.IsSuccess);
        Assert.Equal("Project type not found.", result.Error);
    }

    /// <summary>
    /// The opposite of how a component deletes. A component assignment cascades
    /// away harmlessly, but a type is a classification an admin chose — so
    /// tidying the catalogue must not silently reclassify projects. The count is
    /// in the message because knowing how many projects to reassign first is the
    /// point of the refusal.
    /// </summary>
    [Fact]
    public async Task Delete_is_refused_while_a_project_still_uses_the_type()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        var support = await Create(context, Payload(name: "Support"));
        await AddProject(context, "Helpdesk", "HELP-001", support.Value!.Id);

        var result = await Delete(context, support.Value!.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal("Cannot delete project type because 1 project uses it.", result.Error);
        Assert.Single(await context.ProjectTypes.ToListAsync());
    }

    [Fact]
    public async Task The_refusal_counts_every_project_using_the_type()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        var support = await Create(context, Payload(name: "Support"));
        await AddProject(context, "Helpdesk", "HELP-001", support.Value!.Id);
        await AddProject(context, "Retainer", "RET-001", support.Value!.Id);

        var result = await Delete(context, support.Value!.Id);

        Assert.Equal("Cannot delete project type because 2 projects use it.", result.Error);
    }

    /// <summary>
    /// Reclassifying the last project off a type has to actually release it, or
    /// the refusal would be a one-way door out of the catalogue.
    /// </summary>
    [Fact]
    public async Task Delete_succeeds_once_the_last_project_is_reclassified()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        var support = await Create(context, Payload(name: "Support"));
        var project = await AddProject(context, "Helpdesk", "HELP-001", support.Value!.Id);

        context.ProjectTypeAssignments.RemoveRange(project.TypeAssignments);
        await context.SaveChangesAsync();

        var result = await Delete(context, support.Value!.Id);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(await context.ProjectTypes.ToListAsync());
    }

    /* ── List ───────────────────────────────────────────────────────────────── */

    /// <summary>
    /// The panel renders the catalogue in the order the query returns it, and
    /// creation order is not an order a reader can scan — by name is. The
    /// lower-cased "internal" is the case that an ordinal collation would sort
    /// after every capitalised name instead of into the alphabet.
    /// </summary>
    [Fact]
    public async Task The_catalogue_is_listed_by_name()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        await Create(context, Payload(name: "Support"));
        await Create(context, Payload(name: "Implementation"));
        await Create(context, Payload(name: "internal"));

        var listed = await List(context);

        Assert.Equal(["Implementation", "internal", "Support"], listed.Select(t => t.Name));
    }

    /// <summary>
    /// Disabled types stay in the admin catalogue — the status filter on the panel
    /// is what hides them, so the query must not do it first.
    /// </summary>
    [Fact]
    public async Task Disabled_types_are_still_listed()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        await Create(context, Payload(name: "Support", isActive: false));

        var listed = await List(context);

        Assert.Equal("Support", Assert.Single(listed).Name);
        Assert.False(listed[0].IsActive);
    }

    /// <summary>
    /// The count the panel puts on each card, and the number that explains why a
    /// type in use cannot be deleted.
    /// </summary>
    [Fact]
    public async Task The_catalogue_reports_how_many_projects_use_each_type()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        var support = await Create(context, Payload(name: "Support"));
        await Create(context, Payload(name: "Implementation"));
        await AddProject(context, "Helpdesk", "HELP-001", support.Value!.Id);
        await AddProject(context, "Retainer", "RET-001", support.Value!.Id);
        await AddProject(context, "Unclassified", "UNC-001");

        var listed = await List(context);

        Assert.Equal(0, listed.Single(t => t.Name == "Implementation").UsedInProjects);
        Assert.Equal(2, listed.Single(t => t.Name == "Support").UsedInProjects);
    }

    /// <summary>
    /// A project can be several kinds of engagement at once — a Support project
    /// that also fields Inquiries is both — which is the whole reason the
    /// classification is a set. Each type it holds has to count it, or the delete
    /// refusal would let one of them be tidied away.
    /// </summary>
    [Fact]
    public async Task A_project_can_hold_several_types_and_counts_against_each()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        var support = await Create(context, Payload(name: "Support"));
        var inquiry = await Create(context, Payload(name: "Inquiry"));
        await AddProject(context, "Helpdesk", "HELP-001", support.Value!.Id, inquiry.Value!.Id);

        var listed = await List(context);

        Assert.Equal(1, listed.Single(t => t.Name == "Support").UsedInProjects);
        Assert.Equal(1, listed.Single(t => t.Name == "Inquiry").UsedInProjects);

        var refused = await Delete(context, inquiry.Value!.Id);
        Assert.False(refused.IsSuccess);
        Assert.Equal("Cannot delete project type because 1 project uses it.", refused.Error);
    }
}
