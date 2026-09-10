using Application.Children.Commands;
using Application.Children.DTOs;
using Application.Children.Queries;
using Application.Children.Support;
using Application.Core;
using Domain;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// A child's row carries the ledger that proves how much per-child leave was taken
/// for them, so it cannot be deleted out from under approved leave. A child who has
/// aged out is kept, not deleted — their history still matters, and they simply read
/// as ineligible.
///
/// TransactionalTestDb, not TestDb: the EF in-memory provider enforces no foreign
/// keys at all, so an assertion about one passes whether or not the model says
/// anything.
/// </summary>
public class ChildCrudTests
{
    private static async Task<EmployeeProfile> SeedProfileAsync(AppDbContext db, string userId = "user-1", int? departmentId = null)
    {
        var user = new User { Id = userId, UserName = $"{userId}@example.com", Email = $"{userId}@example.com", DisplayName = "Andreas Georgiou" };
        var profile = new EmployeeProfile { Id = $"profile-{userId}", UserId = userId, DepartmentId = departmentId };
        db.Users.Add(user);
        db.EmployeeProfiles.Add(profile);
        await db.SaveChangesAsync();
        return profile;
    }

    /// <summary>
    /// The one leave type <see cref="ChildProjection.ResolveEligibleUntilAgeAsync"/>
    /// looks for: active and <c>PerChildEntitlement</c>. Without one, every child
    /// resolves as ineligible (age cap 0) — which is correct, but not what these
    /// tests are demonstrating, so they seed a realistic paternity-leave-shaped type.
    /// </summary>
    private static async Task SeedPerChildLeaveTypeAsync(AppDbContext db, int eligibleUntilAge = 15)
    {
        db.LeaveTypes.Add(new LeaveType
        {
            Id = 1,
            Name = "Paternity",
            IsActive = true,
            PerChildEntitlement = true,
            ChildEligibleUntilAge = eligibleUntilAge,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Leave_holds_its_child_row_in_place()
    {
        await using var db = await TransactionalTestDb.CreateAsync();
        var profile = await SeedProfileAsync(db);

        var child = new Child { EmployeeProfileId = profile.Id, Name = "Andreas", DateOfBirth = new DateOnly(2019, 3, 4) };
        db.Children.Add(child);
        await db.SaveChangesAsync();

        db.AnnualLeaves.Add(new AnnualLeave
        {
            EmployeeId = profile.UserId,
            EmployeeProfileId = profile.Id,
            ChildId = child.Id,
            StartDate = new DateTime(2026, 3, 2),
            EndDate = new DateTime(2026, 3, 6),
            Reason = "Paternity",
            Status = AnnualLeaveStatus.Approved,
        });
        await db.SaveChangesAsync();

        // A production delete runs on a fresh request with an empty change tracker.
        // Reusing the tracked `child` reference here would let EF's own navigation
        // fixup (child.LeaveRequests was wired up to the leave above by tracking,
        // not by anything this test did) null the foreign key on the leave before
        // the delete reaches the database — hiding whether the restrict constraint
        // holds at all. Clearing the tracker and removing a bare stub, like the
        // sweep in DeleteAdminUser is tested, forces the delete straight to SQLite.
        var childId = child.Id;
        db.ChangeTracker.Clear();
        db.Children.Remove(new Child { Id = childId });

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    /// <summary>
    /// Production never hard-deletes a profile: <c>AuditingSaveChangesInterceptor</c>
    /// rewrites <c>Remove</c> of an <c>ISoftDeletable</c> to <c>IsDeleted = true</c>,
    /// so the Cascade configured on <c>Child.EmployeeProfile</c> never actually fires.
    /// A soft-deleted owner's children are therefore orphaned in place — still
    /// carrying their <c>EmployeeProfileId</c> even though the owning profile no
    /// longer appears in any filtered query. This replaces a prior version of this
    /// test that asserted the opposite (that removal cascades), which only passed
    /// because the test context registers no auditing interceptor and so exercised
    /// a hard delete that production code never performs. This orphaning is exactly
    /// the state <c>ChildAccessResolver</c> has to be defended against — see
    /// <see cref="A_soft_deleted_owner_cannot_be_reached_through_someone_elses_call()"/>.
    /// </summary>
    [Fact]
    public async Task Soft_deleting_a_profile_leaves_its_children_in_place()
    {
        await using var db = TestDb.Create();
        var profile = await SeedProfileAsync(db);

        db.Children.Add(new Child { EmployeeProfileId = profile.Id, Name = "Maria", DateOfBirth = new DateOnly(2022, 9, 12) });
        await db.SaveChangesAsync();

        profile.IsDeleted = true;
        await db.SaveChangesAsync();

        Assert.Single(await db.Children.ToListAsync());
    }

    private static Task<Result<ChildDto>> Create(AppDbContext db, string callerUserId, UpsertChildRequest child, string? employeeId = null, bool isAdmin = false) =>
        new CreateChild.Handler(db).Handle(new CreateChild.Command
        {
            EmployeeId = employeeId,
            Child = child,
            CallerUserId = callerUserId,
            IsAdmin = isAdmin,
        }, CancellationToken.None);

    private static UpsertChildRequest Andreas() => new() { Name = "Andreas", DateOfBirth = new DateOnly(2019, 3, 4) };

    [Fact]
    public async Task Adding_a_child_declares_that_the_employee_has_children()
    {
        await using var db = TestDb.Create();
        var profile = await SeedProfileAsync(db);
        await SeedPerChildLeaveTypeAsync(db);
        Assert.Null(profile.HasChildren);

        var result = await Create(db, profile.UserId, Andreas());

        Assert.True(result.IsSuccess);
        Assert.Equal("Andreas", result.Value!.Name);
        // Computed, never stored: the age moves on its own as the date passes.
        Assert.Equal(new DateOnly(2034, 3, 3), result.Value.LastEligibleDate);
        Assert.True(result.Value.IsEligible);

        var reloaded = await db.EmployeeProfiles.SingleAsync(ep => ep.Id == profile.Id);
        Assert.True(reloaded.HasChildren);
    }

    /// <summary>
    /// The child list is personal data. One employee may not read or write another's,
    /// whatever employee id they put on the request.
    /// </summary>
    [Fact]
    public async Task An_employee_cannot_add_a_child_to_someone_else()
    {
        await using var db = TestDb.Create();
        await SeedProfileAsync(db, "user-1");
        await SeedProfileAsync(db, "user-2");

        var result = await Create(db, "user-1", Andreas(), employeeId: "user-2");

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.Forbidden, result.ErrorKind);
    }

    [Fact]
    public async Task An_admin_can_add_a_child_for_anyone()
    {
        await using var db = TestDb.Create();
        await SeedProfileAsync(db, "admin-1");
        var employee = await SeedProfileAsync(db, "user-2");

        var result = await Create(db, "admin-1", Andreas(), employeeId: "user-2", isAdmin: true);

        Assert.True(result.IsSuccess);
        var stored = await db.Children.SingleAsync();
        Assert.Equal(employee.Id, stored.EmployeeProfileId);
    }

    [Fact]
    public async Task Deleting_a_child_with_leave_against_it_is_refused()
    {
        await using var db = TestDb.Create();
        var profile = await SeedProfileAsync(db);
        var created = await Create(db, profile.UserId, Andreas());

        db.AnnualLeaves.Add(new AnnualLeave
        {
            EmployeeId = profile.UserId,
            EmployeeProfileId = profile.Id,
            ChildId = created.Value!.Id,
            StartDate = new DateTime(2026, 3, 2),
            EndDate = new DateTime(2026, 3, 6),
            Reason = "Paternity",
            Status = AnnualLeaveStatus.Approved,
        });
        await db.SaveChangesAsync();

        var result = await new DeleteChild.Handler(db).Handle(new DeleteChild.Command
        {
            Id = created.Value.Id,
            CallerUserId = profile.UserId,
        }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.Conflict, result.ErrorKind);
        Assert.Single(await db.Children.ToListAsync());
    }

    [Fact]
    public async Task A_child_with_no_leave_can_be_removed()
    {
        await using var db = TestDb.Create();
        var profile = await SeedProfileAsync(db);
        var created = await Create(db, profile.UserId, Andreas());

        var result = await new DeleteChild.Handler(db).Handle(new DeleteChild.Command
        {
            Id = created.Value!.Id,
            CallerUserId = profile.UserId,
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(await db.Children.ToListAsync());
    }

    /// <summary>
    /// A child who has passed the eligibility age is still listed — the UI has to
    /// explain why they are ineligible rather than silently dropping them, and their
    /// approved leave is still history.
    /// </summary>
    [Fact]
    public async Task An_aged_out_child_is_listed_as_ineligible()
    {
        await using var db = TestDb.Create();
        var profile = await SeedProfileAsync(db);
        await SeedPerChildLeaveTypeAsync(db);
        var bornLongAgo = new UpsertChildRequest { Name = "Petros", DateOfBirth = new DateOnly(2005, 1, 20) };

        var result = await Create(db, profile.UserId, bornLongAgo);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsEligible);

        // AgeYears is asserted separately, against a fixed onDate rather than
        // whatever DateTime.UtcNow happens to be when the suite runs, so this
        // holds forever instead of quietly breaking the day after the next
        // birthday (2027-01-20) and reading as a bug in AgeOn.
        var child = new Child { DateOfBirth = bornLongAgo.DateOfBirth };
        var dto = ChildProjection.ToDto(child, eligibleUntilAge: 15, onDate: new DateOnly(2026, 6, 15));
        Assert.Equal(21, dto.AgeYears);
    }

    /// <summary>
    /// child.EmployeeProfile comes back null for a soft-deleted owner (the
    /// !IsDeleted query filter on EmployeeProfile does not propagate to Child), and
    /// null is ChildAccessResolver's sentinel for "the caller themselves". Deriving
    /// the access target from the navigation would let this pass as a self-access
    /// and hand a departed colleague's child record to whoever asked.
    /// </summary>
    [Fact]
    public async Task A_soft_deleted_owner_cannot_be_reached_through_someone_elses_call()
    {
        await using var db = TestDb.Create();
        var owner = await SeedProfileAsync(db, "user-1");
        await SeedProfileAsync(db, "user-2");
        var created = await Create(db, owner.UserId, Andreas());

        owner.IsDeleted = true;
        await db.SaveChangesAsync();

        var updateResult = await new UpdateChild.Handler(db).Handle(new UpdateChild.Command
        {
            Id = created.Value!.Id,
            Child = Andreas(),
            CallerUserId = "user-2",
        }, CancellationToken.None);

        Assert.False(updateResult.IsSuccess);

        var deleteResult = await new DeleteChild.Handler(db).Handle(new DeleteChild.Command
        {
            Id = created.Value.Id,
            CallerUserId = "user-2",
        }, CancellationToken.None);

        Assert.False(deleteResult.IsSuccess);
    }

    /// <summary>
    /// A date-of-birth change moves the eligibility window an already-approved
    /// leave was measured against, so it gets the same guard DeleteChild has:
    /// refused once leave is recorded, unless the caller is an Admin (matching
    /// EditAnnualLeave's admin override for approved requests).
    /// </summary>
    [Fact]
    public async Task Changing_date_of_birth_with_leave_recorded_is_refused_for_the_employee()
    {
        await using var db = TestDb.Create();
        var profile = await SeedProfileAsync(db);
        var created = await Create(db, profile.UserId, Andreas());

        db.AnnualLeaves.Add(new AnnualLeave
        {
            EmployeeId = profile.UserId,
            EmployeeProfileId = profile.Id,
            ChildId = created.Value!.Id,
            StartDate = new DateTime(2026, 3, 2),
            EndDate = new DateTime(2026, 3, 6),
            Reason = "Paternity",
            Status = AnnualLeaveStatus.Approved,
        });
        await db.SaveChangesAsync();

        var result = await new UpdateChild.Handler(db).Handle(new UpdateChild.Command
        {
            Id = created.Value.Id,
            Child = new UpsertChildRequest { Name = "Andreas", DateOfBirth = new DateOnly(2020, 3, 4) },
            CallerUserId = profile.UserId,
        }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.Conflict, result.ErrorKind);
    }

    [Fact]
    public async Task An_admin_can_change_date_of_birth_even_with_leave_recorded()
    {
        await using var db = TestDb.Create();
        var profile = await SeedProfileAsync(db);
        var created = await Create(db, profile.UserId, Andreas());

        db.AnnualLeaves.Add(new AnnualLeave
        {
            EmployeeId = profile.UserId,
            EmployeeProfileId = profile.Id,
            ChildId = created.Value!.Id,
            StartDate = new DateTime(2026, 3, 2),
            EndDate = new DateTime(2026, 3, 6),
            Reason = "Paternity",
            Status = AnnualLeaveStatus.Approved,
        });
        await db.SaveChangesAsync();

        var result = await new UpdateChild.Handler(db).Handle(new UpdateChild.Command
        {
            Id = created.Value.Id,
            Child = new UpsertChildRequest { Name = "Andreas", DateOfBirth = new DateOnly(2020, 3, 4) },
            CallerUserId = "admin-1",
            IsAdmin = true,
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateOnly(2020, 3, 4), result.Value!.DateOfBirth);
    }

    [Fact]
    public async Task A_manager_can_read_a_direct_reports_children_in_their_department()
    {
        await using var db = TestDb.Create();
        var manager = await SeedProfileAsync(db, "manager-1", departmentId: 1);
        var employee = await SeedProfileAsync(db, "user-2", departmentId: 1);
        await Create(db, employee.UserId, Andreas());

        var result = await new GetChildList.Handler(db).Handle(new GetChildList.Query
        {
            EmployeeId = employee.UserId,
            CallerUserId = manager.UserId,
            IsManager = true,
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
    }

    [Fact]
    public async Task A_manager_outside_the_department_cannot_read_another_employees_children()
    {
        await using var db = TestDb.Create();
        var manager = await SeedProfileAsync(db, "manager-1", departmentId: 1);
        var employee = await SeedProfileAsync(db, "user-2", departmentId: 2);
        await Create(db, employee.UserId, Andreas());

        var result = await new GetChildList.Handler(db).Handle(new GetChildList.Query
        {
            EmployeeId = employee.UserId,
            CallerUserId = manager.UserId,
            IsManager = true,
        }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.Forbidden, result.ErrorKind);
    }

    /// <summary>
    /// The manager branch of ChildAccessResolver only ever returns Success when
    /// forWrite is false. A single boolean whose inversion would silently hand
    /// managers write access to their reports' family records needs a test that
    /// fails if that inversion ever happens — read-scope alone proves nothing here.
    /// </summary>
    [Fact]
    public async Task A_manager_cannot_write_a_direct_reports_child_even_inside_their_department()
    {
        await using var db = TestDb.Create();
        var manager = await SeedProfileAsync(db, "manager-1", departmentId: 1);
        var employee = await SeedProfileAsync(db, "user-2", departmentId: 1);
        var created = await Create(db, employee.UserId, Andreas());

        var result = await new UpdateChild.Handler(db).Handle(new UpdateChild.Command
        {
            Id = created.Value!.Id,
            Child = new UpsertChildRequest { Name = "Andreas K", DateOfBirth = created.Value.DateOfBirth },
            CallerUserId = manager.UserId,
            IsManager = true,
        }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.Forbidden, result.ErrorKind);
    }

    [Fact]
    public async Task An_employee_cannot_read_someone_elses_child_list()
    {
        await using var db = TestDb.Create();
        await SeedProfileAsync(db, "user-1");
        var other = await SeedProfileAsync(db, "user-2");
        await Create(db, other.UserId, Andreas());

        var result = await new GetChildList.Handler(db).Handle(new GetChildList.Query
        {
            EmployeeId = other.UserId,
            CallerUserId = "user-1",
        }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.Forbidden, result.ErrorKind);
    }

    /// <summary>
    /// Proves the wiring, not just the rule: UpsertChildRequestValidator alone is
    /// never resolved by MediatR's ValidationBehavior, which asks the container for
    /// IValidator&lt;TRequest&gt; where TRequest is the *command* type. Without
    /// CreateChildRequestValidator/UpdateChildRequestValidator wrapping it, a future
    /// date of birth would reach the database uncaught. Mirrors
    /// TimesheetValidationPipelineTests' DI shape (AddMediatR +
    /// AddValidatorsFromAssemblyContaining + ValidationBehavior), not Program.cs
    /// itself, so it fails if the wrapper validator is ever removed regardless of
    /// how Program.cs is wired.
    /// </summary>
    private static ServiceProvider BuildChildValidationProvider()
    {
        var services = new ServiceCollection();

        services.AddDbContext<AppDbContext>(o => o
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

        services.AddMediatR(c => c.RegisterServicesFromAssemblyContaining<CreateChild.Handler>());
        services.AddValidatorsFromAssemblyContaining<CreateChild>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddLogging();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task CreateChild_future_date_of_birth_is_rejected_by_the_pipeline()
    {
        using var provider = BuildChildValidationProvider();
        var db = provider.GetRequiredService<AppDbContext>();
        var profile = await SeedProfileAsync(db);

        var mediator = provider.GetRequiredService<IMediator>();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            mediator.Send(new CreateChild.Command
            {
                Child = new UpsertChildRequest
                {
                    Name = "Future Kid",
                    DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
                },
                CallerUserId = profile.UserId,
            }));

        Assert.NotEmpty(ex.Errors);
    }

    [Fact]
    public async Task UpdateChild_future_date_of_birth_is_rejected_by_the_pipeline()
    {
        using var provider = BuildChildValidationProvider();
        var db = provider.GetRequiredService<AppDbContext>();
        var profile = await SeedProfileAsync(db);
        var created = await Create(db, profile.UserId, Andreas());

        var mediator = provider.GetRequiredService<IMediator>();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            mediator.Send(new UpdateChild.Command
            {
                Id = created.Value!.Id,
                Child = new UpsertChildRequest
                {
                    Name = "Andreas",
                    DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
                },
                CallerUserId = profile.UserId,
            }));

        Assert.NotEmpty(ex.Errors);
    }

    /// <summary>
    /// A client that does not send the field must not silently retract a
    /// declaration, so null means "leave it alone" rather than "false" — checked
    /// against a profile already declared <c>true</c>. This is the case the null
    /// short-circuit exists for: it fails if that short-circuit is ever removed
    /// (the count query would still find nothing to object to, but a careless
    /// rewrite that unconditionally assigned <c>requested.Value</c> would crash on
    /// the null here rather than silently misbehaving, which is exactly why cell 2
    /// below is needed too).
    /// </summary>
    [Fact]
    public async Task An_absent_declaration_leaves_a_true_declaration_alone()
    {
        await using var db = TestDb.Create();
        var profile = await SeedProfileAsync(db);
        profile.HasChildren = true;
        await db.SaveChangesAsync();

        var error = await HasChildrenDeclaration.ApplyAsync(
            db, profile, requested: null, CancellationToken.None);

        Assert.Null(error);
        Assert.True((await db.EmployeeProfiles.SingleAsync(ep => ep.Id == profile.Id)).HasChildren);
    }

    /// <summary>
    /// Same as above but against a profile declared <c>false</c> — proves the
    /// null short-circuit does not quietly flip a stored <c>false</c> to
    /// <c>true</c> (or anything else) on its way through.
    /// </summary>
    [Fact]
    public async Task An_absent_declaration_leaves_a_false_declaration_alone()
    {
        await using var db = TestDb.Create();
        var profile = await SeedProfileAsync(db);
        profile.HasChildren = false;
        await db.SaveChangesAsync();

        var error = await HasChildrenDeclaration.ApplyAsync(
            db, profile, requested: null, CancellationToken.None);

        Assert.Null(error);
        Assert.False((await db.EmployeeProfiles.SingleAsync(ep => ep.Id == profile.Id)).HasChildren);
    }

    /// <summary>
    /// The declaration and the list must not be able to disagree. Saying "no
    /// children" while children are on the profile is refused rather than silently
    /// ignored — a flag that contradicts the rows beside it is the failure mode
    /// CLAUDE.md documents for every duplicated leave figure. The refusal must
    /// also leave the stored value exactly where it was, not just report an error.
    /// </summary>
    [Fact]
    public async Task Declaring_no_children_while_children_exist_is_refused_and_leaves_the_value_unchanged()
    {
        await using var db = TestDb.Create();
        var profile = await SeedProfileAsync(db);
        await Create(db, profile.UserId, Andreas());

        var error = await HasChildrenDeclaration.ApplyAsync(
            db, profile, requested: false, CancellationToken.None);

        Assert.Equal("Remove the 1 child(ren) on your profile before saying you have none.", error);
        Assert.True((await db.EmployeeProfiles.SingleAsync(ep => ep.Id == profile.Id)).HasChildren);
    }

    [Fact]
    public async Task Declaring_no_children_is_saved_when_there_are_none()
    {
        await using var db = TestDb.Create();
        var profile = await SeedProfileAsync(db);
        Assert.Null(profile.HasChildren);

        var error = await HasChildrenDeclaration.ApplyAsync(
            db, profile, requested: false, CancellationToken.None);

        Assert.Null(error);
        Assert.False((await db.EmployeeProfiles.SingleAsync(ep => ep.Id == profile.Id)).HasChildren);
    }

    [Fact]
    public async Task Declaring_has_children_is_saved()
    {
        await using var db = TestDb.Create();
        var profile = await SeedProfileAsync(db);

        var error = await HasChildrenDeclaration.ApplyAsync(
            db, profile, requested: true, CancellationToken.None);

        Assert.Null(error);
        Assert.True((await db.EmployeeProfiles.SingleAsync(ep => ep.Id == profile.Id)).HasChildren);
    }

    [Fact]
    public async Task Removing_the_last_child_leaves_the_declaration_alone()
    {
        await using var db = TestDb.Create();
        var profile = await SeedProfileAsync(db);
        var created = await Create(db, profile.UserId, Andreas());

        await new DeleteChild.Handler(db).Handle(new DeleteChild.Command
        {
            Id = created.Value!.Id,
            CallerUserId = profile.UserId,
        }, CancellationToken.None);

        // Still true: they told us they have children, and removing a row is not a
        // retraction. They can uncheck the box themselves.
        Assert.True((await db.EmployeeProfiles.SingleAsync(ep => ep.Id == profile.Id)).HasChildren);
    }
}
