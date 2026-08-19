using Application.AnnualLeaves.Queries;
using Application.Core;
using Application.LeaveTypes.Commands;
using Application.LeaveTypes.DTOs;
using Application.ProjectActivityTypes.Commands;
using Application.ProjectActivityTypes.DTOs;
using Domain;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Program.cs carried six hand-written handler registrations — the three LeaveType
/// commands and the three ProjectActivityType commands — under a comment blaming
/// "handler resolution issues when running under watch/hot-reload in development".
///
/// There was nothing wrong with those six handlers. They are public nested types
/// implementing IRequestHandler&lt;,&gt; in the Application assembly, exactly like the
/// other fifty-four, and
/// <c>RegisterServicesFromAssemblyContaining&lt;GetAnnualLeaveList.Handler&gt;()</c>
/// scans that whole assembly.
///
/// The symptom was real, but it was a property of hot reload, not of scanning:
/// assembly scanning runs once, while the service collection is being built at
/// startup. Hot reload can introduce a new type into a running process, but it
/// cannot re-run <c>AddMediatR</c> or add descriptors to a ServiceProvider that has
/// already been built — so a handler written during a watch session is missing from
/// the container until the process restarts. Adding an explicit registration
/// appeared to fix it because editing Program.cs is a rude edit that forces exactly
/// that restart. A restart alone would have done it.
///
/// These tests are what makes the deletion safe, and what stops the workaround
/// coming back: the first covers every handler in the assembly rather than the six
/// that were singled out.
/// </summary>
[Collection(ApiRouteTableCollection.Name)]
public class MediatRHandlerRegistrationTests(ApiRouteTableFixture routeTable)
{
    /// <summary>
    /// The conclusive check: resolve the six out of the real application's
    /// container, booted from the real Program.cs now that the hand-written
    /// registrations are gone. The tests above prove an equivalent container
    /// resolves them; this one proves the shipped one does.
    /// </summary>
    [Theory]
    [InlineData(typeof(IRequestHandler<CreateLeaveType.Command, Result<LeaveTypeDto>>), typeof(CreateLeaveType.Handler))]
    [InlineData(typeof(IRequestHandler<UpdateLeaveType.Command, Result<LeaveTypeDto>>), typeof(UpdateLeaveType.Handler))]
    [InlineData(typeof(IRequestHandler<DeleteLeaveType.Command, Result<Unit>>), typeof(DeleteLeaveType.Handler))]
    [InlineData(typeof(IRequestHandler<CreateProjectActivityType.Command, Result<ProjectActivityTypeDto>>), typeof(CreateProjectActivityType.Handler))]
    [InlineData(typeof(IRequestHandler<UpdateProjectActivityType.Command, Result<ProjectActivityTypeDto>>), typeof(UpdateProjectActivityType.Handler))]
    [InlineData(typeof(IRequestHandler<DeleteProjectActivityType.Command, Result<Unit>>), typeof(DeleteProjectActivityType.Handler))]
    public void The_running_application_resolves_the_six(Type serviceType, Type expectedHandler)
    {
        using var scope = routeTable.Services.CreateScope();

        var handler = scope.ServiceProvider.GetService(serviceType);

        Assert.NotNull(handler);
        Assert.IsType(expectedHandler, handler);
    }

    /// <summary>
    /// The MediatR setup from Program.cs, and nothing else — no explicit handler
    /// registrations of any kind.
    /// </summary>
    private static ServiceCollection MediatRAsConfiguredInProgram()
    {
        var services = new ServiceCollection();
        services.AddMediatR(x => x.RegisterServicesFromAssemblyContaining<GetAnnualLeaveList.Handler>());
        return services;
    }

    /// <summary>
    /// Every non-abstract closed implementation of IRequestHandler&lt;,&gt; or
    /// IRequestHandler&lt;&gt; in the Application assembly, paired with the interface
    /// it satisfies.
    /// </summary>
    public static TheoryData<string, Type, Type> AllHandlers()
    {
        var data = new TheoryData<string, Type, Type>();

        foreach (var (service, implementation) in HandlerPairs())
        {
            // The declaring type disambiguates the display name: every handler in
            // this codebase is a nested class literally called "Handler".
            data.Add($"{implementation.DeclaringType?.Name}.{implementation.Name}", service, implementation);
        }

        return data;
    }

    private static List<(Type Service, Type Implementation)> HandlerPairs() =>
    [
        .. typeof(GetAnnualLeaveList.Handler).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false })
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType
                    && (i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)
                        || i.GetGenericTypeDefinition() == typeof(IRequestHandler<>)))
                .Select(i => (Service: i, Implementation: t))),
    ];

    [Theory]
    [MemberData(nameof(AllHandlers))]
    public void Assembly_scanning_registers_every_handler(string name, Type serviceType, Type implementationType)
    {
        var services = MediatRAsConfiguredInProgram();

        var registered = services.Any(d =>
            d.ServiceType == serviceType && d.ImplementationType == implementationType);

        Assert.True(registered, $"{name} was not registered by assembly scanning ({serviceType}).");
    }

    /// <summary>
    /// The same sweep against the real application's container rather than an
    /// equivalent one, so it also fails if Program.cs ever scans the wrong assembly
    /// — which the check above, holding its own copy of the configuration, could not
    /// notice. Resolving rather than inspecting descriptors means each handler's
    /// dependencies have to be satisfiable too.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllHandlers))]
    public void The_running_application_resolves_every_handler(string name, Type serviceType, Type implementationType)
    {
        using var scope = routeTable.Services.CreateScope();

        var handler = scope.ServiceProvider.GetService(serviceType);

        Assert.NotNull(handler);
        Assert.IsType(implementationType, handler);
        _ = name;
    }

    /// <summary>
    /// Anti-vacuity guard: if the reflection above found nothing, the theory would
    /// pass by running zero cases.
    /// </summary>
    [Fact]
    public void The_scan_finds_a_realistic_number_of_handlers()
    {
        Assert.True(HandlerPairs().Count >= 50, $"Only found {HandlerPairs().Count} handlers to check.");
    }

    /// <summary>
    /// The six that were registered by hand, named explicitly. Covered by the
    /// theory above too, but spelled out so a failure points straight at the
    /// workaround rather than at a list of sixty.
    /// </summary>
    [Theory]
    [InlineData(typeof(CreateLeaveType.Command), typeof(CreateLeaveType.Handler))]
    [InlineData(typeof(UpdateLeaveType.Command), typeof(UpdateLeaveType.Handler))]
    [InlineData(typeof(DeleteLeaveType.Command), typeof(DeleteLeaveType.Handler))]
    [InlineData(typeof(CreateProjectActivityType.Command), typeof(CreateProjectActivityType.Handler))]
    [InlineData(typeof(UpdateProjectActivityType.Command), typeof(UpdateProjectActivityType.Handler))]
    [InlineData(typeof(DeleteProjectActivityType.Command), typeof(DeleteProjectActivityType.Handler))]
    public void The_six_formerly_hand_registered_handlers_come_from_the_scan(Type commandType, Type handlerType)
    {
        var services = MediatRAsConfiguredInProgram();

        var registered = services.Any(d =>
            d.ImplementationType == handlerType
            && d.ServiceType.IsGenericType
            && d.ServiceType.GetGenericArguments()[0] == commandType);

        Assert.True(registered, $"{handlerType.Name} for {commandType.Name} was not registered by assembly scanning.");
    }

    /// <summary>
    /// End-to-end: resolve IMediator from a container built the way Program.cs
    /// builds it and dispatch one of the six. This is the assertion the workaround
    /// was really about — "no handler registered for request type" would surface
    /// here as an InvalidOperationException from Send.
    /// </summary>
    [Fact]
    public async Task Mediator_dispatches_one_of_the_six_without_a_hand_written_registration()
    {
        using var db = TestDb.Create();
        db.LeaveTypes.Add(new LeaveType { Id = 7, Name = "Study", IsActive = true });
        await db.SaveChangesAsync();

        var services = MediatRAsConfiguredInProgram();
        services.AddSingleton(db);
        services.TryAddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new DeleteLeaveType.Command { Id = 7 });

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(db.LeaveTypes);
    }

    /// <summary>
    /// And the same handler resolves as a plain service, which is what the deleted
    /// AddTransient lines were asserting by hand.
    /// </summary>
    [Fact]
    public void The_handler_interface_resolves_directly()
    {
        using var db = TestDb.Create();

        var services = MediatRAsConfiguredInProgram();
        services.AddSingleton(db);
        services.TryAddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);

        using var provider = services.BuildServiceProvider();

        var handler = provider.GetService<IRequestHandler<DeleteLeaveType.Command, Result<Unit>>>();

        Assert.IsType<DeleteLeaveType.Handler>(handler);
    }

    /// <summary>
    /// No handler may be registered twice for the same request. MediatR resolves a
    /// single handler per request, so a duplicate — which is what a hand-written
    /// registration alongside the scan creates — is how a codebase ends up with two
    /// answers to the same command and no way to tell which one ran.
    /// </summary>
    [Fact]
    public void No_request_type_has_more_than_one_handler()
    {
        var duplicates = HandlerPairs()
            .GroupBy(p => p.Service)
            .Where(g => g.Select(p => p.Implementation).Distinct().Count() > 1)
            .Select(g => $"{g.Key.Name}: {string.Join(", ", g.Select(p => p.Implementation.DeclaringType?.Name))}")
            .ToList();

        Assert.True(duplicates.Count == 0, $"Requests with multiple handlers: {string.Join(" | ", duplicates)}");
    }
}
