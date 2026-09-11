using System.ComponentModel.DataAnnotations;
using Application.Accounts.DTOs;
using Application.AdminUsers.Commands;
using Application.AdminUsers.DTOs;
using Application.AdminUsers.Validators;
using Application.Core;
using Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// The identity fields an admin records about a person were barely checked.
/// A phone number was capped at 30 characters and nothing else, so "cvbcvb" was
/// a 200 and then somebody's phone number; a date of birth was not checked at
/// all, so today's date made a new hire a newborn. The admin panel offered a
/// plain text field for each and the API agreed with it.
///
/// The employee's own Edit profile had refused letters in the browser all along
/// (<c>profileSchema</c> in <c>client/src/components/layout/Sidebar.tsx</c>),
/// which is the rule these bring to the server and to the two admin dialogs.
/// <c>client/src/lib/validation/person.ts</c> is the client's copy of all three;
/// keep them in step, or a field saves in the browser and then 400s.
///
/// The phone pattern allows what a written number carries — a leading <c>+</c>,
/// spaces, dashes, and parentheses around a dialling code — because
/// "+357 99 123456" is how people write one. What it refuses is letters.
/// </summary>
public class PersonFieldValidationTests : IDisposable
{
    private readonly ServiceProvider _services;

    /// <summary>
    /// The validator reads its own clock, so the tests read the same one rather
    /// than pinning a date that would go stale tomorrow.
    /// </summary>
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public PersonFieldValidationTests()
    {
        var collection = new ServiceCollection();

        collection.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        collection.AddDbContext<AppDbContext>(options => options
            .UseInMemoryDatabase($"person-fields-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

        collection.AddIdentityCore<User>(options => options.User.RequireUniqueEmail = true)
            .AddRoles<Role>()
            .AddEntityFrameworkStores<AppDbContext>();

        _services = collection.BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        GC.SuppressFinalize(this);
    }

    private AppDbContext Db => _services.GetRequiredService<AppDbContext>();
    private RoleManager<Role> Roles => _services.GetRequiredService<RoleManager<Role>>();

    /// <summary>A department to satisfy the rule that has nothing to do with these fields.</summary>
    private async Task<int> GivenDepartmentAsync()
    {
        var department = new Department { Name = "Engineering", Code = "ENG", IsActive = true };
        Db.Departments.Add(department);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        return department.Id;
    }

    private async Task GivenRolesAsync()
    {
        foreach (var role in new[] { AppRoles.Admin, AppRoles.Manager, AppRoles.Employee })
        {
            if (!await Roles.RoleExistsAsync(role))
            {
                Assert.True((await Roles.CreateAsync(new Role { Name = role })).Succeeded);
            }
        }
    }

    private async Task<FluentValidation.Results.ValidationResult> ValidateCreateAsync(
        string? phoneNumber = null, DateOnly? dateOfBirth = null)
    {
        await GivenRolesAsync();

        var payload = new AdminCreateUserDto
        {
            Email = "new.joiner@test.local",
            DisplayName = "New Joiner",
            DepartmentId = await GivenDepartmentAsync(),
            Roles = [AppRoles.Employee],
            PhoneNumber = phoneNumber,
            DateOfBirth = dateOfBirth,
        };

        return await new CreateAdminUserValidator(Db, Roles)
            .ValidateAsync(new CreateAdminUser.Command { User = payload });
    }

    private static FluentValidation.Results.ValidationResult ValidateUpdate(
        string? phoneNumber = null, DateOnly? dateOfBirth = null) =>
        new UpdateAdminUserValidator().Validate(new UpdateAdminUser.Command
        {
            Id = "u-1",
            User = new AdminUpdateUserDto
            {
                Email = "theodoros@test.local",
                DisplayName = "Theodoros Iona",
                PhoneNumber = phoneNumber,
                DateOfBirth = dateOfBirth,
            },
        });

    private static void AssertRefused(FluentValidation.Results.ValidationResult result, string property)
    {
        Assert.False(result.IsValid, $"Expected {property} to be refused.");
        Assert.Contains(result.Errors, e => e.PropertyName.EndsWith(property, StringComparison.Ordinal));
    }

    private static void AssertAccepted(FluentValidation.Results.ValidationResult result, string property) =>
        Assert.DoesNotContain(result.Errors, e => e.PropertyName.EndsWith(property, StringComparison.Ordinal));

    [Theory]
    [InlineData("cvbcvb")]
    [InlineData("99123456 ext 4")]
    [InlineData("not a phone")]
    public async Task Create_refuses_a_phone_number_containing_letters(string phoneNumber) =>
        AssertRefused(await ValidateCreateAsync(phoneNumber: phoneNumber), "PhoneNumber");

    [Theory]
    [InlineData("99123456")]
    [InlineData("+357 99 123456")]
    [InlineData("(00357) 22-123456")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Create_accepts_a_number_however_it_is_written(string? phoneNumber) =>
        AssertAccepted(await ValidateCreateAsync(phoneNumber: phoneNumber), "PhoneNumber");

    [Theory]
    [InlineData("cvbcvb")]
    [InlineData("99123456 ext 4")]
    public void Update_refuses_a_phone_number_containing_letters(string phoneNumber) =>
        AssertRefused(ValidateUpdate(phoneNumber: phoneNumber), "PhoneNumber");

    [Theory]
    [InlineData("99123456")]
    [InlineData("+357 99 123456")]
    [InlineData("(00357) 22-123456")]
    [InlineData("")]
    [InlineData(null)]
    public void Update_accepts_a_number_however_it_is_written(string? phoneNumber) =>
        AssertAccepted(ValidateUpdate(phoneNumber: phoneNumber), "PhoneNumber");

    /// <summary>Somebody who turned the minimum age today, who is old enough.</summary>
    private static DateOnly ExactlyMinimumAge => Today.AddYears(-PersonFieldRules.MinimumAgeYears);

    [Fact]
    public async Task Create_refuses_a_date_of_birth_under_the_minimum_age() =>
        AssertRefused(
            await ValidateCreateAsync(dateOfBirth: ExactlyMinimumAge.AddDays(1)),
            nameof(AdminCreateUserDto.DateOfBirth));

    [Fact]
    public async Task Create_refuses_a_date_of_birth_in_the_future() =>
        AssertRefused(
            await ValidateCreateAsync(dateOfBirth: Today.AddYears(1)),
            nameof(AdminCreateUserDto.DateOfBirth));

    [Fact]
    public async Task Create_accepts_somebody_who_has_just_reached_the_minimum_age() =>
        AssertAccepted(
            await ValidateCreateAsync(dateOfBirth: ExactlyMinimumAge),
            nameof(AdminCreateUserDto.DateOfBirth));

    [Fact]
    public async Task Create_refuses_no_date_of_birth_at_all() =>
        AssertRefused(await ValidateCreateAsync(dateOfBirth: null), nameof(AdminCreateUserDto.DateOfBirth));

    [Fact]
    public void Update_refuses_a_date_of_birth_under_the_minimum_age() =>
        AssertRefused(
            ValidateUpdate(dateOfBirth: ExactlyMinimumAge.AddDays(1)),
            nameof(AdminUpdateUserDto.DateOfBirth));

    [Fact]
    public void Update_accepts_somebody_comfortably_over_the_minimum_age() =>
        AssertAccepted(ValidateUpdate(dateOfBirth: new DateOnly(1990, 3, 4)), nameof(AdminUpdateUserDto.DateOfBirth));

    [Fact]
    public void Update_refuses_no_date_of_birth_at_all() =>
        AssertRefused(ValidateUpdate(dateOfBirth: null), nameof(AdminUpdateUserDto.DateOfBirth));

    /* The employee's own profile update is a plain DTO bound by MVC rather than a
       MediatR command, so its rules are DataAnnotations. It already carried
       [Phone] and [EmailAddress]; the age rule was missing there too, which would
       have left an employee able to set a date of birth through the API that an
       admin cannot set for them. */

    private static List<ValidationResult> ValidateOwnProfile(DateOnly? dateOfBirth)
    {
        var dto = new UpdateProfileDto
        {
            DisplayName = "Theodoros Iona",
            Email = "theodoros@test.local",
            DateOfBirth = dateOfBirth,
        };

        var results = new List<ValidationResult>();
        Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);
        return results;
    }

    private static bool ComplainedAboutDateOfBirth(List<ValidationResult> results) =>
        results.Exists(r => r.MemberNames.Contains(nameof(UpdateProfileDto.DateOfBirth)));

    [Fact]
    public void Own_profile_refuses_a_date_of_birth_under_the_minimum_age() =>
        Assert.True(ComplainedAboutDateOfBirth(ValidateOwnProfile(ExactlyMinimumAge.AddDays(1))));

    [Fact]
    public void Own_profile_accepts_somebody_who_has_just_reached_the_minimum_age() =>
        Assert.False(ComplainedAboutDateOfBirth(ValidateOwnProfile(ExactlyMinimumAge)));

    [Fact]
    public void Own_profile_refuses_no_date_of_birth_at_all() =>
        Assert.True(ComplainedAboutDateOfBirth(ValidateOwnProfile(null)));
}
