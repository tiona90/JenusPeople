using Application.Core;
using Application.LeaveTypes.Commands;
using Application.LeaveTypes.DTOs;
using Application.LeaveTypes.Validators;
using AutoMapper;
using Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Annual, Maternity and Paternity Leave are seeded and depended on by name, so they
/// cannot be renamed or deleted. Everything else about them stays editable.
///
/// Protecting Annual Leave used to be a client-side name check in LeaveTypesPanel
/// and nothing else: <c>DeleteLeaveType</c> had no such rule, so a DELETE straight to
/// the API removed it, and the app came up missing the type the enforced balance is a
/// budget for — with nothing offering to recreate it, since seeding does not restore
/// a deleted row. The rule lives on the server now, with the panel following the
/// <see cref="LeaveTypeDto.IsSystem"/> flag it derives.
/// </summary>
public class SystemLeaveTypesAreProtectedTests
{
    private static IMapper BuildMapper() =>
        new MapperConfiguration(
            cfg => cfg.AddProfile<MappingProfiles>(),
            NullLoggerFactory.Instance).CreateMapper();

    private static async Task<LeaveType> SeedAsync(AppDbContext db, string name)
    {
        var leaveType = new LeaveType
        {
            Name = name,
            IsActive = true,
            RequiresApproval = true,
            DefaultAllowance = 25,
            AffectsBalance = name == SystemLeaveTypes.AnnualLeave,
        };

        db.LeaveTypes.Add(leaveType);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        return leaveType;
    }

    private static UpsertLeaveTypeRequest Request(string name) => new()
    {
        Name = name,
        RequiresApproval = true,
        IsActive = true,
        DefaultAllowance = 25,
    };

    private static Task<Result<LeaveTypeDto>> Update(AppDbContext db, int id, UpsertLeaveTypeRequest request) =>
        new UpdateLeaveType.Handler(db, BuildMapper())
            .Handle(new UpdateLeaveType.Command { Id = id, LeaveType = request }, CancellationToken.None);

    private static Task<Result<Unit>> Delete(AppDbContext db, int id) =>
        new DeleteLeaveType.Handler(db).Handle(new DeleteLeaveType.Command { Id = id }, CancellationToken.None);

    /// <summary>Every protected name, so none of the three is covered by accident.</summary>
    public static TheoryData<string> SystemNames() =>
        new(SystemLeaveTypes.AnnualLeave, SystemLeaveTypes.MaternityLeave, SystemLeaveTypes.PaternityLeave);

    /* ── Deleting ───────────────────────────────────────────────────────────── */

    [Theory]
    [MemberData(nameof(SystemNames))]
    public async Task A_built_in_leave_type_cannot_be_deleted(string name)
    {
        using var db = TestDb.Create();
        var leaveType = await SeedAsync(db, name);

        var result = await Delete(db, leaveType.Id);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.Conflict, result.ErrorKind);
        Assert.Contains("cannot be deleted", result.Error);
        Assert.True(await db.LeaveTypes.AnyAsync(lt => lt.Id == leaveType.Id));
    }

    /// <summary>
    /// The refusal holds for a type nobody has requested leave against yet. That is
    /// the case the old "in use by leave requests" check let through, and the one
    /// that actually loses the row.
    /// </summary>
    [Fact]
    public async Task A_built_in_type_with_no_leave_against_it_is_still_protected()
    {
        using var db = TestDb.Create();
        var leaveType = await SeedAsync(db, SystemLeaveTypes.AnnualLeave);
        Assert.False(await db.AnnualLeaves.AnyAsync());

        Assert.False((await Delete(db, leaveType.Id)).IsSuccess);
    }

    [Theory]
    [MemberData(nameof(SystemNames))]
    public async Task The_validator_refuses_deleting_a_built_in_type(string name)
    {
        using var db = TestDb.Create();
        var leaveType = await SeedAsync(db, name);

        var result = await new DeleteLeaveTypeRequestValidator(db)
            .ValidateAsync(new DeleteLeaveType.Command { Id = leaveType.Id });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "That is a built-in leave type and cannot be deleted.");
    }

    // The rule must not spread to types an admin created, which stay deletable.
    [Fact]
    public async Task A_custom_leave_type_can_still_be_deleted()
    {
        using var db = TestDb.Create();
        var leaveType = await SeedAsync(db, "Study Leave");

        var result = await Delete(db, leaveType.Id);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(await db.LeaveTypes.AnyAsync(lt => lt.Id == leaveType.Id));
    }

    /* ── Renaming ───────────────────────────────────────────────────────────── */

    [Theory]
    [MemberData(nameof(SystemNames))]
    public async Task A_built_in_leave_type_cannot_be_renamed(string name)
    {
        using var db = TestDb.Create();
        var leaveType = await SeedAsync(db, name);

        var result = await Update(db, leaveType.Id, Request("Something Else"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultErrorKind.Conflict, result.ErrorKind);
        Assert.Contains("cannot be renamed", result.Error);

        // And the stored name is untouched — the map runs after the check, so a
        // refusal must not leave the new name written.
        db.ChangeTracker.Clear();
        Assert.Equal(name, (await db.LeaveTypes.SingleAsync(lt => lt.Id == leaveType.Id)).Name);
    }

    [Theory]
    [MemberData(nameof(SystemNames))]
    public async Task The_validator_refuses_renaming_a_built_in_type(string name)
    {
        using var db = TestDb.Create();
        var leaveType = await SeedAsync(db, name);

        var result = await new UpdateLeaveTypeRequestValidator(db).ValidateAsync(
            new UpdateLeaveType.Command { Id = leaveType.Id, LeaveType = Request("Something Else") });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "That is a built-in leave type and cannot be renamed.");
    }

    /// <summary>
    /// The read-only field submits the name back unchanged, so the ordinary save has
    /// to keep working — otherwise the type would become uneditable rather than
    /// unrenameable.
    /// </summary>
    [Theory]
    [MemberData(nameof(SystemNames))]
    public async Task Saving_a_built_in_type_under_its_own_name_still_works(string name)
    {
        using var db = TestDb.Create();
        var leaveType = await SeedAsync(db, name);

        var request = Request(name);
        request.MinNoticeDays = 14;
        request.Description = "Reconfigured";

        var result = await Update(db, leaveType.Id, request);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(14, result.Value!.MinNoticeDays);
        Assert.Equal("Reconfigured", result.Value!.Description);
    }

    /// <summary>
    /// Only the name is frozen. An admin can still retire one of these, change its
    /// allowance, or alter any other setting — Maternity and Paternity in particular
    /// can be switched off by an organisation that does not offer them.
    /// </summary>
    [Theory]
    [InlineData(SystemLeaveTypes.MaternityLeave)]
    [InlineData(SystemLeaveTypes.PaternityLeave)]
    public async Task A_built_in_type_can_still_be_deactivated(string name)
    {
        using var db = TestDb.Create();
        var leaveType = await SeedAsync(db, name);

        var request = Request(name);
        request.IsActive = false;

        var result = await Update(db, leaveType.Id, request);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(result.Value!.IsActive);
    }

    // Case and surrounding whitespace must not be a way around the rule.
    [Theory]
    [InlineData("annual leave")]
    [InlineData("  Annual Leave  ")]
    [InlineData("ANNUAL LEAVE")]
    public async Task A_differently_cased_or_padded_name_is_not_a_rename(string submitted)
    {
        using var db = TestDb.Create();
        var leaveType = await SeedAsync(db, SystemLeaveTypes.AnnualLeave);

        var result = await Update(db, leaveType.Id, Request(submitted));

        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public async Task A_custom_leave_type_can_still_be_renamed()
    {
        using var db = TestDb.Create();
        var leaveType = await SeedAsync(db, "Study Leave");

        var result = await Update(db, leaveType.Id, Request("Training Leave"));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Training Leave", result.Value!.Name);
    }

    /* ── The flag the client reads ──────────────────────────────────────────── */

    [Theory]
    [MemberData(nameof(SystemNames))]
    public void The_dto_reports_a_built_in_type_as_such(string name)
    {
        Assert.True(new LeaveTypeDto { Name = name }.IsSystem);
    }

    [Theory]
    [InlineData("Study Leave")]
    [InlineData("Sick Leave")]
    [InlineData("")]
    public void The_dto_reports_every_other_type_as_ordinary(string name)
    {
        Assert.False(new LeaveTypeDto { Name = name }.IsSystem);
    }
}
