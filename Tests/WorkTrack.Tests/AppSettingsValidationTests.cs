using Application.Core;
using Application.Settings.Commands;
using Application.Settings.Validators;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// UpdateAppSettings checked its own input and reported every failure with
/// Result.Failure, which maps to 404. An admin who typed month 13 got Not Found,
/// with nothing naming the field — indistinguishable from a broken route.
///
/// The rules now live in a validator, so the same input answers 400 with the field
/// named. The handler keeps its copy as a backstop for a direct call, and these
/// cover both paths: the validator for what the API sees, the handler for what a
/// caller bypassing the pipeline sees.
/// </summary>
public class AppSettingsValidationTests
{
    /// <summary>
    /// A settings payload that passes. Worth noting the command's own defaults do
    /// not: LeaveYearStartMonth and DefaultAnnualEntitlement default to 0, which
    /// both rules reject.
    /// </summary>
    private static UpdateAppSettings.Command Valid() => new()
    {
        LeaveYearStartMonth = 1,
        MaxCarryoverDays = 5,
        DefaultAnnualEntitlement = 25,
        FinancialYearStartMonth = 1,
        WorkingHoursStart = "09:00",
        WorkingHoursEnd = "18:00",
        WeeklyHoursTarget = 40,
        WorkingDays = "mon-fri",
        WorkingDaysCustom = "mon,tue,wed,thu,fri",
        TimesheetSubmissionDeadlineDay = "fri",
        TimesheetSubmissionDeadlineTime = "18:00",
    };

    private static ValidationResult Validate(UpdateAppSettings.Command command) =>
        new UpdateAppSettingsValidator().Validate(command);

    private static void AssertRejects(UpdateAppSettings.Command command, string property)
    {
        var result = Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == property);
    }

    [Fact]
    public void A_valid_payload_passes()
    {
        var result = Validate(Valid());

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    /* ── Numeric ranges ─────────────────────────────────────────────────────── */

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(-1)]
    public void The_leave_year_start_month_must_be_a_month(int month)
    {
        var command = Valid();
        command.LeaveYearStartMonth = month;

        AssertRejects(command, nameof(command.LeaveYearStartMonth));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void The_financial_year_start_month_must_be_a_month(int month)
    {
        var command = Valid();
        command.FinancialYearStartMonth = month;

        AssertRejects(command, nameof(command.FinancialYearStartMonth));
    }

    [Fact]
    public void Carryover_days_cannot_be_negative()
    {
        var command = Valid();
        command.MaxCarryoverDays = -1;

        AssertRejects(command, nameof(command.MaxCarryoverDays));

        // Zero carryover is a real policy, not an error.
        command.MaxCarryoverDays = 0;
        Assert.True(Validate(command).IsValid);
    }

    [Fact]
    public void The_default_entitlement_must_be_at_least_one_day()
    {
        var command = Valid();
        command.DefaultAnnualEntitlement = 0;

        AssertRejects(command, nameof(command.DefaultAnnualEntitlement));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(169)]
    public void The_weekly_hours_target_must_fit_in_a_week(int hours)
    {
        var command = Valid();
        command.WeeklyHoursTarget = hours;

        AssertRejects(command, nameof(command.WeeklyHoursTarget));
    }

    /* ── Times and day names ────────────────────────────────────────────────── */

    [Theory]
    [InlineData("")]
    [InlineData("half past nine")]
    [InlineData("25:00")]
    public void Working_hours_must_be_times(string value)
    {
        var start = Valid();
        start.WorkingHoursStart = value;
        AssertRejects(start, nameof(start.WorkingHoursStart));

        var end = Valid();
        end.WorkingHoursEnd = value;
        AssertRejects(end, nameof(end.WorkingHoursEnd));
    }

    /// <summary>A single-digit hour is accepted and canonicalised, as before.</summary>
    [Fact]
    public void A_single_digit_hour_is_still_a_valid_time()
    {
        var command = Valid();
        command.WorkingHoursStart = "9:00";

        Assert.True(Validate(command).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("friday")]
    [InlineData("xyz")]
    public void The_deadline_day_must_be_a_weekday_token(string day)
    {
        var command = Valid();
        command.TimesheetSubmissionDeadlineDay = day;

        AssertRejects(command, nameof(command.TimesheetSubmissionDeadlineDay));
    }

    [Theory]
    [InlineData("mon")]
    [InlineData("SUN")]
    [InlineData(" fri ")]
    public void The_deadline_day_accepts_any_case_or_padding(string day)
    {
        var command = Valid();
        command.TimesheetSubmissionDeadlineDay = day;

        Assert.True(Validate(command).IsValid);
    }

    [Fact]
    public void The_deadline_time_must_be_a_time()
    {
        var command = Valid();
        command.TimesheetSubmissionDeadlineTime = "not-a-time";

        AssertRejects(command, nameof(command.TimesheetSubmissionDeadlineTime));
    }

    /* ── The custom schedule ────────────────────────────────────────────────── */

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("someday,whenever")]
    public void A_custom_schedule_needs_at_least_one_real_day(string custom)
    {
        var command = Valid();
        command.WorkingDays = "custom";
        command.WorkingDaysCustom = custom;

        AssertRejects(command, nameof(command.WorkingDaysCustom));
    }

    /// <summary>
    /// The rule is scoped to the custom schedule: on any other setting the list is
    /// unused, so an empty one is not worth refusing a save over.
    /// </summary>
    [Fact]
    public void An_empty_custom_list_is_ignored_on_a_fixed_schedule()
    {
        var command = Valid();
        command.WorkingDays = "mon-fri";
        command.WorkingDaysCustom = "";

        Assert.True(Validate(command).IsValid);
    }

    /* ── The handler backstop ───────────────────────────────────────────────── */

    /// <summary>
    /// Called directly, the handler must still answer 400 rather than the 404 that
    /// Result.Failure produced. HandleResult keys that off ValidationErrors, so the
    /// dictionary has to be populated and carry the field name.
    /// </summary>
    [Fact]
    public async Task The_handler_reports_bad_input_as_a_validation_failure_not_a_missing_resource()
    {
        using var db = TestDb.Create();
        var command = Valid();
        command.LeaveYearStartMonth = 13;

        var result = await new UpdateAppSettings.Handler(db).Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ValidationErrors);
        Assert.True(result.ValidationErrors!.ContainsKey(nameof(command.LeaveYearStartMonth)));
        Assert.Contains("between 1 and 12", result.Error);

        // Nothing was written on the way out.
        Assert.False(db.AppSettings.Any());
    }

    [Fact]
    public async Task A_valid_payload_is_saved_and_canonicalised()
    {
        using var db = TestDb.Create();
        var command = Valid();
        command.WorkingHoursStart = "9:00";

        var result = await new UpdateAppSettings.Handler(db).Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);

        var saved = db.AppSettings.Single();
        Assert.Equal("09:00", saved.WorkingHoursStart);
        Assert.Equal(1, saved.LeaveYearStartMonth);
    }

    /* ── The pipeline ───────────────────────────────────────────────────────── */

    /// <summary>
    /// The one that matters: a validator that is never discovered never runs, and
    /// every test above would still pass. This registers validators and the
    /// behaviour exactly as Program.cs does, then sends a bad payload through
    /// MediatR and asserts it was stopped before the handler wrote anything.
    /// </summary>
    [Fact]
    public async Task The_validator_stops_a_bad_payload_before_the_handler_runs()
    {
        using var db = TestDb.Create();

        await using var provider = new ServiceCollection()
            .AddSingleton(db)
            // MediatR 13 resolves ILoggerFactory while constructing its licence accessor.
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddValidatorsFromAssemblyContaining<Application.Core.MappingProfiles>()
            .AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<UpdateAppSettings>())
            .AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>))
            .BuildServiceProvider();

        var command = Valid();
        command.LeaveYearStartMonth = 13;

        var thrown = await Assert.ThrowsAsync<ValidationException>(
            () => provider.GetRequiredService<IMediator>().Send(command));

        Assert.Contains(thrown.Errors, e => e.PropertyName == nameof(command.LeaveYearStartMonth));
        // GlobalExceptionMiddleware turns that into a 400 carrying the field name.
        Assert.False(db.AppSettings.Any());
    }
}
