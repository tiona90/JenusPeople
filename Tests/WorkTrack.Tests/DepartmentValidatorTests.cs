using Application.Departments.Commands;
using Application.Departments.DTOs;
using Application.Departments.Validators;
using FluentValidation.Results;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// The department commands had no validators, so an over-long name or a blank
/// code reached the handler and then the database, where a 100-character column
/// decides the outcome — a 500 for what is a malformed request.
///
/// These pin the caps against the DataAnnotations they mirror, so the two cannot
/// drift: if <see cref="UpsertDepartmentRequest"/> changes a StringLength, the
/// length tests here read the attribute and follow it.
/// </summary>
public class DepartmentValidatorTests
{
    private const int NameCap = 100;
    private const int CodeCap = 10;

    private static UpsertDepartmentRequest Payload(string? name = "Engineering", string? code = "ENG") => new()
    {
        Name = name!,
        Code = code!,
        IsActive = true,
    };

    private static ValidationResult ValidateCreate(UpsertDepartmentRequest? payload) =>
        new CreateDepartmentRequestValidator().Validate(new CreateDepartment.Command { Department = payload! });

    private static ValidationResult ValidateUpdate(UpsertDepartmentRequest? payload, int id = 1) =>
        new UpdateDepartmentRequestValidator().Validate(new UpdateDepartment.Command { Id = id, Department = payload! });

    private static ValidationResult ValidateDelete(int id) =>
        new DeleteDepartmentRequestValidator().Validate(new DeleteDepartment.Command { Id = id });

    private static void AssertFailsOn(ValidationResult result, string propertySuffix)
    {
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.EndsWith(propertySuffix, StringComparison.Ordinal));
    }

    /* ── The caps match the DTO ─────────────────────────────────────────────── */

    /// <summary>
    /// The validator exists to enforce what the attributes already declare, so the
    /// numbers have to agree. Read from the attribute rather than restated, or this
    /// file becomes the place the two quietly diverge.
    /// </summary>
    [Fact]
    public void The_caps_match_the_StringLength_attributes_they_mirror()
    {
        Assert.Equal(NameCap, MaxLengthOf(nameof(UpsertDepartmentRequest.Name)));
        Assert.Equal(CodeCap, MaxLengthOf(nameof(UpsertDepartmentRequest.Code)));
    }

    private static int MaxLengthOf(string propertyName)
    {
        var attribute = typeof(UpsertDepartmentRequest)
            .GetProperty(propertyName)!
            .GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.StringLengthAttribute), inherit: false)
            .Cast<System.ComponentModel.DataAnnotations.StringLengthAttribute>()
            .Single();

        return attribute.MaximumLength;
    }

    /* ── CreateDepartment ───────────────────────────────────────────────────── */

    [Fact]
    public void A_valid_create_payload_passes()
    {
        Assert.True(ValidateCreate(Payload()).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_blank_name(string name)
    {
        AssertFailsOn(ValidateCreate(Payload(name: name)), nameof(UpsertDepartmentRequest.Name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_blank_code(string code)
    {
        AssertFailsOn(ValidateCreate(Payload(code: code)), nameof(UpsertDepartmentRequest.Code));
    }

    [Fact]
    public void Create_rejects_a_name_over_the_cap()
    {
        AssertFailsOn(
            ValidateCreate(Payload(name: new string('a', NameCap + 1))),
            nameof(UpsertDepartmentRequest.Name));

        // And accepts one exactly at it, so the boundary is not off by one.
        Assert.True(ValidateCreate(Payload(name: new string('a', NameCap))).IsValid);
    }

    [Fact]
    public void Create_rejects_a_code_over_the_cap()
    {
        AssertFailsOn(
            ValidateCreate(Payload(code: new string('C', CodeCap + 1))),
            nameof(UpsertDepartmentRequest.Code));

        Assert.True(ValidateCreate(Payload(code: new string('C', CodeCap))).IsValid);
    }

    [Fact]
    public void Create_rejects_a_missing_payload()
    {
        AssertFailsOn(ValidateCreate(null), nameof(CreateDepartment.Command.Department));
    }

    /* ── UpdateDepartment ───────────────────────────────────────────────────── */

    [Fact]
    public void A_valid_update_payload_passes()
    {
        Assert.True(ValidateUpdate(Payload()).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Update_rejects_a_blank_name(string name)
    {
        AssertFailsOn(ValidateUpdate(Payload(name: name)), nameof(UpsertDepartmentRequest.Name));
    }

    [Fact]
    public void Update_rejects_a_name_over_the_cap()
    {
        AssertFailsOn(
            ValidateUpdate(Payload(name: new string('a', NameCap + 1))),
            nameof(UpsertDepartmentRequest.Name));
    }

    [Fact]
    public void Update_rejects_a_code_over_the_cap()
    {
        AssertFailsOn(
            ValidateUpdate(Payload(code: new string('C', CodeCap + 1))),
            nameof(UpsertDepartmentRequest.Code));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Update_rejects_a_non_positive_id(int id)
    {
        AssertFailsOn(ValidateUpdate(Payload(), id), nameof(UpdateDepartment.Command.Id));
    }

    /* ── DeleteDepartment ───────────────────────────────────────────────────── */

    [Fact]
    public void A_valid_delete_id_passes()
    {
        Assert.True(ValidateDelete(1).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Delete_rejects_a_non_positive_id(int id)
    {
        AssertFailsOn(ValidateDelete(id), nameof(DeleteDepartment.Command.Id));
    }

    /* ── Registration ───────────────────────────────────────────────────────── */

    /// <summary>
    /// A validator that is not discovered never runs, and nothing else in the suite
    /// would notice. Program.cs registers them with
    /// AddValidatorsFromAssemblyContaining&lt;MappingProfiles&gt;, so each one has to be
    /// a public, concrete IValidator in that assembly to reach the MediatR pipeline.
    /// </summary>
    [Fact]
    public void Each_department_validator_is_discoverable_by_the_scan_Program_uses()
    {
        var scannedAssembly = typeof(Application.Core.MappingProfiles).Assembly;

        Type[] commands =
        [
            typeof(CreateDepartment.Command),
            typeof(UpdateDepartment.Command),
            typeof(DeleteDepartment.Command),
        ];

        foreach (var command in commands)
        {
            var validatorInterface = typeof(FluentValidation.IValidator<>).MakeGenericType(command);

            Assert.Contains(
                scannedAssembly.GetTypes(),
                type => type is { IsClass: true, IsAbstract: false, IsPublic: true }
                    && validatorInterface.IsAssignableFrom(type));
        }
    }
}
