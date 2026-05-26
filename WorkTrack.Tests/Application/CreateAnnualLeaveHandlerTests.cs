using Application.AnnualLeaves.Commands;
using Application.AnnualLeaves.DTOs;
using AutoMapper;
using Domain;
using Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Persistence;
using Xunit;

namespace WorkTrack.Tests.Application;

public class CreateAnnualLeaveHandlerTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static IMapper CreatePassthroughMapper()
    {
        var mapper = Substitute.For<IMapper>();
        mapper.Map<AnnualLeave>(Arg.Any<CreateAnnualLeaveRequest>())
            .Returns(ci =>
            {
                var req = (CreateAnnualLeaveRequest)ci[0]!;
                return new AnnualLeave
                {
                    Id = Guid.NewGuid().ToString(),
                    EmployeeId = req.EmployeeId,
                    StartDate = req.StartDate,
                    EndDate = req.EndDate,
                    LeaveTypeId = req.LeaveTypeId,
                    Reason = req.Reason,
                    EvidenceUrl = req.EvidenceUrl,
                    Status = AnnualLeaveStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                };
            });
        return mapper;
    }

    private static CreateAnnualLeaveRequest BuildRequest(
        string employeeId,
        int leaveTypeId,
        DateTime start,
        DateTime end) => new()
        {
            EmployeeId = employeeId,
            LeaveTypeId = leaveTypeId,
            StartDate = start,
            EndDate = end,
            Reason = "vacation",
        };

    [Fact]
    public async Task Handle_Throws_WhenEmployeeProfileMissing()
    {
        using var ctx = CreateContext();
        ctx.LeaveTypes.Add(new LeaveType
        {
            Id = 1,
            Name = "Annual",
            IsActive = true,
            RequiresApproval = true,
            AffectsBalance = true,
        });
        await ctx.SaveChangesAsync();

        var handler = new CreateAnnualLeave.Handler(ctx, CreatePassthroughMapper(), Substitute.For<IEmailService>());
        var command = new CreateAnnualLeave.Command
        {
            AnnualLeave = BuildRequest("ghost-user", 1, new DateTime(2026, 6, 1), new DateTime(2026, 6, 5)),
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(command, CancellationToken.None));
        Assert.Contains("Employee profile not found", ex.Message);
    }

    [Fact]
    public async Task Handle_Throws_WhenLeaveTypeMissingOrInactive()
    {
        using var ctx = CreateContext();
        ctx.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = Guid.NewGuid().ToString(),
            UserId = "user-1",
            DepartmentId = 1,
            AnnualLeaveEntitlement = 20,
        });
        ctx.LeaveTypes.Add(new LeaveType
        {
            Id = 1,
            Name = "Disabled Type",
            IsActive = false, // inactive — handler should treat as missing
            RequiresApproval = true,
            AffectsBalance = true,
        });
        await ctx.SaveChangesAsync();

        var handler = new CreateAnnualLeave.Handler(ctx, CreatePassthroughMapper(), Substitute.For<IEmailService>());
        var command = new CreateAnnualLeave.Command
        {
            AnnualLeave = BuildRequest("user-1", 1, new DateTime(2026, 6, 1), new DateTime(2026, 6, 5)),
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(command, CancellationToken.None));
        Assert.Contains("leave type is not available", ex.Message);
    }

    [Fact]
    public async Task Handle_PersistsPendingLeave_WhenLeaveTypeRequiresApproval()
    {
        using var ctx = CreateContext();
        ctx.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = "profile-1",
            UserId = "user-1",
            DepartmentId = 1,
            AnnualLeaveEntitlement = 20,
        });
        ctx.LeaveTypes.Add(new LeaveType
        {
            Id = 1,
            Name = "Annual",
            IsActive = true,
            RequiresApproval = true,
            AffectsBalance = true,
        });
        await ctx.SaveChangesAsync();

        var emailService = Substitute.For<IEmailService>();
        var handler = new CreateAnnualLeave.Handler(ctx, CreatePassthroughMapper(), emailService);
        var command = new CreateAnnualLeave.Command
        {
            AnnualLeave = BuildRequest("user-1", 1, new DateTime(2026, 6, 1), new DateTime(2026, 6, 5)),
        };

        var leaveId = await handler.Handle(command, CancellationToken.None);

        var saved = await ctx.AnnualLeaves.FindAsync(leaveId);
        Assert.NotNull(saved);
        Assert.Equal(AnnualLeaveStatus.Pending, saved!.Status);
        Assert.Equal("profile-1", saved.EmployeeProfileId);
        Assert.Equal(1, saved.DepartmentId);
        Assert.Null(saved.ApprovedAt);
        // No manager → no email.
        await emailService.DidNotReceive().SendEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AutoApprovesAndSyncsBalance_WhenLeaveTypeDoesNotRequireApproval()
    {
        using var ctx = CreateContext();
        ctx.AppSettings.Add(new AppSettings { LeaveYearStartMonth = 1 });
        var profile = new EmployeeProfile
        {
            Id = "profile-1",
            UserId = "user-1",
            DepartmentId = 1,
            AnnualLeaveEntitlement = 20,
            LeaveBalance = 20,
        };
        ctx.EmployeeProfiles.Add(profile);
        ctx.LeaveTypes.Add(new LeaveType
        {
            Id = 1,
            Name = "Annual",
            IsActive = true,
            RequiresApproval = false, // auto-approve path
            AffectsBalance = true,
        });
        await ctx.SaveChangesAsync();

        // Pick a deterministic Mon–Wed window inside the current leave year (= this calendar year).
        var today = DateTime.UtcNow.Date;
        var monday = today.AddDays(-(int)today.DayOfWeek + (int)DayOfWeek.Monday);
        if (monday > today) monday = monday.AddDays(-7);
        var start = monday;
        var end = monday.AddDays(2); // 3 business days

        var handler = new CreateAnnualLeave.Handler(ctx, CreatePassthroughMapper(), Substitute.For<IEmailService>());
        var leaveId = await handler.Handle(
            new CreateAnnualLeave.Command { AnnualLeave = BuildRequest("user-1", 1, start, end) },
            CancellationToken.None);

        var saved = await ctx.AnnualLeaves.FindAsync(leaveId);
        Assert.NotNull(saved);
        Assert.Equal(AnnualLeaveStatus.Approved, saved!.Status);
        Assert.NotNull(saved.ApprovedAt);

        // History entry was created.
        var history = await ctx.LeaveStatusHistories.FirstOrDefaultAsync(h => h.AnnualLeaveId == leaveId);
        Assert.NotNull(history);
        Assert.Equal(AnnualLeaveStatus.Approved, history!.NewStatus);

        // Balance synced down by 3 business days.
        var reloaded = await ctx.EmployeeProfiles.FindAsync("profile-1");
        Assert.Equal(17, reloaded!.LeaveBalance);
    }

    [Fact]
    public async Task Handle_AutoApprovePath_Throws_WhenBalanceInsufficient()
    {
        using var ctx = CreateContext();
        ctx.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = "profile-1",
            UserId = "user-1",
            DepartmentId = 1,
            AnnualLeaveEntitlement = 2,
        });
        ctx.LeaveTypes.Add(new LeaveType
        {
            Id = 1,
            Name = "Annual",
            IsActive = true,
            RequiresApproval = false,
            AffectsBalance = true,
        });
        await ctx.SaveChangesAsync();

        // 5 business days, entitlement is 2.
        var handler = new CreateAnnualLeave.Handler(ctx, CreatePassthroughMapper(), Substitute.For<IEmailService>());
        var command = new CreateAnnualLeave.Command
        {
            AnnualLeave = BuildRequest("user-1", 1, new DateTime(2026, 6, 1), new DateTime(2026, 6, 5)),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(command, CancellationToken.None));

        // Nothing was persisted (the exception aborts before SaveChanges).
        Assert.False(await ctx.AnnualLeaves.AnyAsync());
    }

    [Fact]
    public async Task Handle_SendsEmailToManager_WhenPendingRequestHasManager()
    {
        using var ctx = CreateContext();

        var managerUser = new User
        {
            Id = "manager-user-id",
            UserName = "manager@example.com",
            Email = "manager@example.com",
            DisplayName = "Manager McManagerface",
        };
        var employeeUser = new User
        {
            Id = "user-1",
            UserName = "emp@example.com",
            Email = "emp@example.com",
            DisplayName = "Emma Employee",
        };
        ctx.Users.Add(managerUser);
        ctx.Users.Add(employeeUser);

        ctx.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = "manager-profile-id",
            UserId = "manager-user-id",
            DepartmentId = 1,
        });
        ctx.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = "profile-1",
            UserId = "user-1",
            DepartmentId = 1,
            ManagerId = "manager-profile-id",
            AnnualLeaveEntitlement = 20,
        });
        ctx.LeaveTypes.Add(new LeaveType
        {
            Id = 1,
            Name = "Annual",
            IsActive = true,
            RequiresApproval = true,
            AffectsBalance = true,
        });
        await ctx.SaveChangesAsync();

        var emailService = Substitute.For<IEmailService>();
        var handler = new CreateAnnualLeave.Handler(ctx, CreatePassthroughMapper(), emailService);
        var command = new CreateAnnualLeave.Command
        {
            AnnualLeave = BuildRequest("user-1", 1, new DateTime(2026, 6, 1), new DateTime(2026, 6, 5)),
        };

        await handler.Handle(command, CancellationToken.None);

        await emailService.Received(1).SendEmailAsync(
            "manager@example.com",
            Arg.Is<string>(s => s.Contains("New leave request")),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }
}
