using Application.AnnualLeaves.Commands;
using Application.AnnualLeaves.DTOs;
using Application.Core;
using AutoMapper;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Maternity and Paternity Leave are offered to an employee only when their
/// recorded <see cref="Gender"/> matches the type and they have a child young
/// enough to qualify. The client hides the cards, but hiding is not enforcing:
/// these tests pin the handler behaviour a crafted request meets.
///
/// Two rules the tests exist to hold still:
///
/// <list type="bullet">
/// <item><description>
/// <c>null</c> gender — "not specified", which is what every account predating the
/// column has — passes both types. Failing closed would strip parental leave from
/// the whole company until an administrator filled the field in one person at a time.
/// </description></item>
/// <item><description>
/// The eligible-child rule is not re-stated for a type that keeps its own per-child
/// ledger. Paternity Leave already refuses a request naming no child, an unknown
/// child or a child who has aged out, each with a message naming the child and date
/// — a blunter pre-check in front of it would only make those messages worse.
/// </description></item>
/// </list>
/// </summary>
public class ParentalLeaveEligibilityTests
{
    private const string UserId = "employee-1";
    private const string ProfileId = "profile-1";
    private const int MaternityTypeId = 1;
    private const int PaternityTypeId = 2;
    private const int AnnualTypeId = 3;

    private static async Task<AppDbContext> WorldAsync(Gender? gender)
    {
        var db = TestDb.Create();

        db.AppSettings.Add(new AppSettings { Id = 1, LeaveYearStartMonth = 1 });

        db.Users.Add(new User
        {
            Id = UserId,
            UserName = "employee-1@example.com",
            Email = "employee-1@example.com",
            DisplayName = "Andreas Georgiou",
            Gender = gender,
        });

        db.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = ProfileId,
            UserId = UserId,
            HasChildren = true,
            AnnualLeaveEntitlement = 25,
            LeaveBalance = 25,
        });

        // Seeded as the application seeds it: no per-child ledger of its own, so
        // nothing else in the request path checks a child for it.
        db.LeaveTypes.Add(new LeaveType
        {
            Id = MaternityTypeId,
            Name = "Maternity Leave",
            IsActive = true,
            RequiresApproval = true,
            AffectsBalance = false,
            DefaultAllowance = 90,
        });

        db.LeaveTypes.Add(new LeaveType
        {
            Id = PaternityTypeId,
            Name = "Paternity Leave",
            IsActive = true,
            RequiresApproval = true,
            AffectsBalance = false,
            DefaultAllowance = 0,
            PerChildEntitlement = true,
            PerChildTotalWeeks = 18,
            PerChildWeeksPerYear = 5,
            ChildEligibleUntilAge = 15,
        });

        db.LeaveTypes.Add(new LeaveType
        {
            Id = AnnualTypeId,
            Name = "Annual Leave",
            IsActive = true,
            RequiresApproval = true,
            AffectsBalance = true,
            DefaultAllowance = 25,
        });

        await db.SaveChangesAsync();
        return db;
    }

    private static async Task<Child> AddChildAsync(AppDbContext db, string name, DateOnly dateOfBirth)
    {
        var child = new Child { EmployeeProfileId = ProfileId, Name = name, DateOfBirth = dateOfBirth };
        db.Children.Add(child);
        await db.SaveChangesAsync();
        return child;
    }

    /// <summary>A child comfortably under the configured cut-off whenever this runs.</summary>
    private static Task<Child> AddYoungChildAsync(AppDbContext db) =>
        AddChildAsync(db, "Maria", DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-4));

    private static IMapper BuildMapper() =>
        new MapperConfiguration(cfg => cfg.AddProfile<MappingProfiles>(), NullLoggerFactory.Instance).CreateMapper();

    private static Task<Result<string>> Create(AppDbContext db, int leaveTypeId, string? childId = null) =>
        new CreateAnnualLeave.Handler(db, BuildMapper(), new FakeEmailService())
            .Handle(new CreateAnnualLeave.Command
            {
                AnnualLeave = new CreateAnnualLeaveRequest
                {
                    EmployeeId = UserId,
                    LeaveTypeId = leaveTypeId,
                    ChildId = childId,
                    StartDate = new DateTime(2026, 6, 1),
                    EndDate = new DateTime(2026, 6, 5),
                    Reason = "Parental leave",
                },
            }, CancellationToken.None);

    [Fact]
    public async Task Maternity_leave_is_refused_for_a_male_employee()
    {
        await using var db = await WorldAsync(Gender.Male);
        await AddYoungChildAsync(db);

        var result = await Create(db, MaternityTypeId);

        Assert.False(result.IsSuccess);
        Assert.Equal("Maternity Leave is not available to you.", result.Error);
        Assert.Empty(await db.AnnualLeaves.ToListAsync());
    }

    [Fact]
    public async Task Paternity_leave_is_refused_for_a_female_employee()
    {
        await using var db = await WorldAsync(Gender.Female);
        var child = await AddYoungChildAsync(db);

        var result = await Create(db, PaternityTypeId, child.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal("Paternity Leave is not available to you.", result.Error);
        Assert.Empty(await db.AnnualLeaves.ToListAsync());
    }

    [Fact]
    public async Task Maternity_leave_is_allowed_to_a_female_employee_with_an_eligible_child()
    {
        await using var db = await WorldAsync(Gender.Female);
        await AddYoungChildAsync(db);

        var result = await Create(db, MaternityTypeId);

        Assert.True(result.IsSuccess);
        Assert.Single(await db.AnnualLeaves.ToListAsync());
    }

    /// <summary>
    /// The fail-open rule. Every account predating the Gender column reads null,
    /// and a rule that hid parental leave from all of them would be a regression
    /// dressed as a feature.
    /// </summary>
    [Fact]
    public async Task An_unspecified_gender_is_offered_both_parental_types()
    {
        await using var db = await WorldAsync(gender: null);
        var child = await AddYoungChildAsync(db);

        var maternity = await Create(db, MaternityTypeId);
        var paternity = await Create(db, PaternityTypeId, child.Id);

        Assert.True(maternity.IsSuccess);
        Assert.True(paternity.IsSuccess);
    }

    [Fact]
    public async Task Maternity_leave_is_refused_when_no_children_are_on_file()
    {
        await using var db = await WorldAsync(Gender.Female);

        var result = await Create(db, MaternityTypeId);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "Maternity Leave is available only while you have a child under 15.",
            result.Error);
        Assert.Empty(await db.AnnualLeaves.ToListAsync());
    }

    [Fact]
    public async Task Maternity_leave_is_refused_when_the_only_child_has_aged_out()
    {
        await using var db = await WorldAsync(Gender.Female);
        await AddChildAsync(db, "Petros", DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-16));

        var result = await Create(db, MaternityTypeId);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "Maternity Leave is available only while you have a child under 15.",
            result.Error);
    }

    /// <summary>
    /// The rule reaches exactly two leave types. Annual Leave is filed by an
    /// employee of any gender with no children at all.
    /// </summary>
    [Fact]
    public async Task Annual_leave_is_untouched_by_gender_or_children()
    {
        await using var db = await WorldAsync(Gender.Male);

        var result = await Create(db, AnnualTypeId);

        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// Paternity Leave's own per-child ledger already refuses a request that names
    /// no child, and says so far more usefully than "you have no eligible child"
    /// would. The new rule must not get in front of it.
    /// </summary>
    [Fact]
    public async Task Paternity_leave_with_no_child_named_keeps_its_own_message()
    {
        await using var db = await WorldAsync(Gender.Male);

        var result = await Create(db, PaternityTypeId, childId: null);

        Assert.False(result.IsSuccess);
        Assert.Equal("Select the child this Paternity Leave is for.", result.Error);
    }

    [Fact]
    public async Task Editing_a_request_onto_a_mismatched_parental_type_is_refused()
    {
        await using var db = await WorldAsync(Gender.Male);
        await AddYoungChildAsync(db);

        var created = await Create(db, AnnualTypeId);
        Assert.True(created.IsSuccess);
        var leaveId = await db.AnnualLeaves.Select(leave => leave.Id).SingleAsync();

        var result = await new EditAnnualLeave.Handler(db).Handle(new EditAnnualLeave.Command
        {
            AnnualLeave = new EditAnnualLeaveRequest
            {
                Id = leaveId,
                LeaveTypeId = MaternityTypeId,
                StartDate = new DateTime(2026, 6, 1),
                EndDate = new DateTime(2026, 6, 5),
                Reason = "Parental leave",
            },
            ChangedByUserId = UserId,
        }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Maternity Leave is not available to you.", result.Error);
        // AsNoTracking so this reads the store rather than the change tracker: the
        // handler assigns the new type onto the tracked entity before validating,
        // and a tracked read would report the edit it never saved.
        Assert.Equal(
            AnnualTypeId,
            await db.AnnualLeaves.AsNoTracking().Select(leave => leave.LeaveTypeId).SingleAsync());
    }
}
