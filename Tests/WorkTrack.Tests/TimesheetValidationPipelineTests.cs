using Application.Core;
using Application.Timesheets.Commands;
using Domain;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Proves the timesheet validators auto-register and execute through the MediatR
/// ValidationBehavior pipeline — the DI wiring mirrors API/Program.cs
/// (AddMediatR + AddValidatorsFromAssemblyContaining + ValidationBehavior). When a
/// command is invalid the pipeline throws <see cref="ValidationException"/> before
/// the handler runs; a valid command passes through to the handler.
/// </summary>
public class TimesheetValidationPipelineTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        services.AddDbContext<AppDbContext>(o => o
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

        // Same registration shape as Program.cs.
        services.AddMediatR(c => c.RegisterServicesFromAssemblyContaining<CreateTimesheet.Handler>());
        services.AddValidatorsFromAssemblyContaining<CreateTimesheet>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        // Dependencies the timesheet handlers (and MediatR itself) need so the
        // container can construct them. AddLogging supplies ILoggerFactory + ILogger<>.
        services.AddLogging();
        services.AddSingleton<Domain.Interfaces.IEmailService, FakeEmailService>();
        services.AddSingleton<Domain.Interfaces.IChatNotificationService, FakeChatNotificationService>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task CreateTimesheet_invalid_command_is_rejected_by_the_pipeline()
    {
        using var provider = BuildProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Empty user + default dates → fails RequestingUserId/PeriodStart/profile rules.
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            mediator.Send(new CreateTimesheet.Command { RequestingUserId = "" }));

        Assert.NotEmpty(ex.Errors);
    }

    [Fact]
    public async Task SubmitTimesheet_invalid_command_is_rejected_by_the_pipeline()
    {
        using var provider = BuildProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<ValidationException>(() =>
            mediator.Send(new SubmitTimesheet.Command { Id = "", RequestingUserId = "" }));
    }

    [Fact]
    public async Task UpdateTimesheetStatus_invalid_target_status_is_rejected_by_the_pipeline()
    {
        using var provider = BuildProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            mediator.Send(new UpdateTimesheetStatus.Command
            {
                Id = "t1",
                RequestingUserId = "admin",
                NewStatus = TimesheetStatus.Draft, // not Approved/Rejected
            }));

        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(UpdateTimesheetStatus.Command.NewStatus));
    }

    [Fact]
    public async Task Valid_command_passes_validation_and_reaches_the_handler()
    {
        using var provider = BuildProvider();
        var db = provider.GetRequiredService<AppDbContext>();
        db.Users.Add(new User { Id = "u1", UserName = "u1", Email = "u1@test.local", DisplayName = "U1" });
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = "p1", UserId = "u1", DepartmentId = 1 });
        await db.SaveChangesAsync();

        var mediator = provider.GetRequiredService<IMediator>();

        // Passes the validator → handler runs → creates a Draft timesheet.
        var result = await mediator.Send(new CreateTimesheet.Command
        {
            RequestingUserId = "u1",
            PeriodStart = new DateTime(2024, 1, 1),
            PeriodEnd = new DateTime(2024, 1, 7),
        });

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(TimesheetStatus.Draft.ToString(), result.Value!.Status);
    }
}
