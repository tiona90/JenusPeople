using Application.Core;
using Application.ProjectComponents.Commands;
using Application.ProjectComponents.DTOs;
using Application.ProjectComponents.Queries;
using Application.ProjectComponents.Validators;
using AutoMapper;
using Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// A project component is the deliverable a project is made of — DM, Lasernet,
/// jDocs — kept as an org-wide catalogue that admins curate, exactly as
/// <see cref="ProjectActivityType"/> is. Nothing references a component yet, so
/// unlike an activity type it can always be deleted; what has to hold is that the
/// catalogue stays free of duplicate names, since the name is what identifies a
/// component to everyone reading it.
///
/// The duplicate-name tests come in a pair on purpose: the validator produces the
/// message a user sees, and the unique index is what actually holds when two
/// requests race past the validator together. Only the second needs a database
/// that enforces constraints, which is why these use
/// <see cref="TransactionalTestDb"/> rather than the in-memory provider.
/// </summary>
public class ProjectComponentTests
{
    private static IMapper BuildMapper() =>
        new MapperConfiguration(
            cfg => cfg.AddProfile<MappingProfiles>(),
            NullLoggerFactory.Instance).CreateMapper();

    private static UpsertProjectComponentRequest Payload(
        string name = "Lasernet",
        string description = "Document output and distribution.",
        string icon = "🖨️",
        string colorKey = "blue",
        bool isActive = true) => new()
        {
            Name = name,
            Description = description,
            Icon = icon,
            ColorKey = colorKey,
            IsActive = isActive,
        };

    private static Task<Result<ProjectComponentDto>> Create(AppDbContext db, UpsertProjectComponentRequest payload) =>
        new CreateProjectComponent.Handler(db, BuildMapper()).Handle(
            new CreateProjectComponent.Command { Component = payload }, CancellationToken.None);

    private static Task<Result<ProjectComponentDto>> Update(AppDbContext db, int id, UpsertProjectComponentRequest payload) =>
        new UpdateProjectComponent.Handler(db, BuildMapper()).Handle(
            new UpdateProjectComponent.Command { Id = id, Component = payload }, CancellationToken.None);

    private static Task<Result<MediatR.Unit>> Delete(AppDbContext db, int id) =>
        new DeleteProjectComponent.Handler(db).Handle(
            new DeleteProjectComponent.Command { Id = id }, CancellationToken.None);

    private static Task<List<ProjectComponentDto>> List(AppDbContext db) =>
        new GetProjectComponentList.Handler(db).Handle(
            new GetProjectComponentList.Query(), CancellationToken.None);

    /* ── Create ─────────────────────────────────────────────────────────────── */

    [Fact]
    public async Task Create_persists_the_component()
    {
        await using var context = await TransactionalTestDb.CreateAsync();

        var result = await Create(context, Payload());

        Assert.True(result.IsSuccess, result.Error);

        var stored = await context.ProjectComponents.SingleAsync();
        Assert.Equal("Lasernet", stored.Name);
        Assert.Equal("Document output and distribution.", stored.Description);
        Assert.Equal("🖨️", stored.Icon);
        Assert.Equal("blue", stored.ColorKey);
        Assert.True(stored.IsActive);
        Assert.Equal(stored.Id, result.Value!.Id);
    }

    /// <summary>
    /// The name is the catalogue identity and is compared for uniqueness after
    /// trimming, so a stored " DM " would be a duplicate the validator cannot see.
    /// </summary>
    [Fact]
    public async Task Create_trims_the_name()
    {
        await using var context = await TransactionalTestDb.CreateAsync();

        await Create(context, Payload(name: "  jDocs  "));

        Assert.Equal("jDocs", (await context.ProjectComponents.SingleAsync()).Name);
    }

    [Fact]
    public async Task Create_falls_back_to_the_default_icon_and_colour_when_none_is_given()
    {
        await using var context = await TransactionalTestDb.CreateAsync();

        await Create(context, Payload(icon: "", colorKey: " "));

        var stored = await context.ProjectComponents.SingleAsync();
        Assert.Equal("🧩", stored.Icon);
        Assert.Equal("default", stored.ColorKey);
    }

    /* ── Duplicate names ────────────────────────────────────────────────────── */

    [Fact]
    public async Task A_name_already_in_the_catalogue_is_rejected_regardless_of_case()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        await Create(context, Payload(name: "DM"));

        var result = await new CreateProjectComponentRequestValidator(context)
            .ValidateAsync(new CreateProjectComponent.Command { Component = Payload(name: "dm") });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "A component with that name already exists.");
    }

    /// <summary>
    /// What holds when two creates pass the validator at the same moment: the
    /// second write fails rather than landing a second "DM" in the catalogue.
    /// </summary>
    [Fact]
    public async Task The_database_refuses_a_duplicate_name_the_validator_did_not_catch()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        await Create(context, Payload(name: "DM"));

        await Assert.ThrowsAsync<DbUpdateException>(() => Create(context, Payload(name: "DM")));
    }

    [Fact]
    public async Task Renaming_a_component_onto_a_name_another_one_holds_is_rejected()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        await Create(context, Payload(name: "DM"));
        var lasernet = await Create(context, Payload(name: "Lasernet"));

        var result = await new UpdateProjectComponentRequestValidator(context)
            .ValidateAsync(new UpdateProjectComponent.Command
            {
                Id = lasernet.Value!.Id,
                Component = Payload(name: "DM"),
            });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "A component with that name already exists.");
    }

    /// <summary>
    /// Saving a component name back unchanged is an edit, not a collision — the
    /// uniqueness check has to exclude the row being updated.
    /// </summary>
    [Fact]
    public async Task Keeping_the_existing_name_while_editing_a_component_is_allowed()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        var dm = await Create(context, Payload(name: "DM"));

        var result = await new UpdateProjectComponentRequestValidator(context)
            .ValidateAsync(new UpdateProjectComponent.Command
            {
                Id = dm.Value!.Id,
                Component = Payload(name: "DM", description: "Renamed description."),
            });

        Assert.True(result.IsValid);
    }

    /* ── Update ─────────────────────────────────────────────────────────────── */

    [Fact]
    public async Task Update_overwrites_the_stored_component()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        var created = await Create(context, Payload(name: "DM", isActive: true));

        var result = await Update(context, created.Value!.Id, Payload(
            name: "DM", description: "Data management.", icon: "🗄️", colorKey: "green", isActive: false));

        Assert.True(result.IsSuccess, result.Error);

        var stored = await context.ProjectComponents.SingleAsync();
        Assert.Equal("Data management.", stored.Description);
        Assert.Equal("🗄️", stored.Icon);
        Assert.Equal("green", stored.ColorKey);
        Assert.False(stored.IsActive);
    }

    [Fact]
    public async Task Update_reports_a_component_that_does_not_exist()
    {
        await using var context = await TransactionalTestDb.CreateAsync();

        var result = await Update(context, 404, Payload());

        Assert.False(result.IsSuccess);
        Assert.Equal("Component not found.", result.Error);
    }

    /* ── Delete ─────────────────────────────────────────────────────────────── */

    [Fact]
    public async Task Delete_removes_the_component()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        var created = await Create(context, Payload());

        var result = await Delete(context, created.Value!.Id);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(await context.ProjectComponents.ToListAsync());
    }

    [Fact]
    public async Task Delete_reports_a_component_that_does_not_exist()
    {
        await using var context = await TransactionalTestDb.CreateAsync();

        var result = await Delete(context, 404);

        Assert.False(result.IsSuccess);
        Assert.Equal("Component not found.", result.Error);
    }

    /* ── List ───────────────────────────────────────────────────────────────── */

    /// <summary>
    /// The panel renders the catalogue in the order the query returns it, and
    /// creation order is not an order a reader can scan — by name is.
    /// </summary>
    [Fact]
    public async Task The_catalogue_is_listed_by_name()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        await Create(context, Payload(name: "Lasernet"));
        await Create(context, Payload(name: "DM"));
        await Create(context, Payload(name: "jDocs"));

        var listed = await List(context);

        Assert.Equal(["DM", "jDocs", "Lasernet"], listed.Select(c => c.Name));
    }

    /// <summary>
    /// Disabled components stay in the admin catalogue — the status filter on the
    /// panel is what hides them, so the query must not do it first.
    /// </summary>
    [Fact]
    public async Task Disabled_components_are_still_listed()
    {
        await using var context = await TransactionalTestDb.CreateAsync();
        await Create(context, Payload(name: "DM", isActive: false));

        var listed = await List(context);

        Assert.Equal("DM", Assert.Single(listed).Name);
        Assert.False(listed[0].IsActive);
    }
}
