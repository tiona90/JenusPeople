using System.Text.Json;
using Application.AdminUsers.DTOs;
using Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// The wire contract for <see cref="User.Gender"/>, asserted against the options the
/// running application actually configures rather than a set built here.
///
/// This boundary had no coverage, and it is the one where a gender goes missing
/// without anything failing. The handler tests call the handler directly and the
/// panel tests mock the API module, so both jump straight over serialisation —
/// yet the field only works because <c>Program.cs</c> registers
/// <c>JsonStringEnumConverter</c>. Take that away and nothing above would notice:
/// the client types gender as the string union <c>'Male' | 'Female'</c>, so it
/// would start sending a name the server no longer parses and reading back a
/// number it does not recognise, and every gender would silently read as
/// "Not specified".
///
/// The null cases are here for the same reason. <see cref="AdminUpdateUserDto"/> is
/// a full replace, so the dialog's "Not specified" clears the column by sending an
/// explicit null — a contract that only holds if null survives the trip.
/// </summary>
[Collection(ApiRouteTableCollection.Name)]
public class AdminUserJsonContractTests(ApiRouteTableFixture fixture)
{
    /// <summary>
    /// The live application's MVC serializer options — the ones its controllers
    /// bind and render with.
    /// </summary>
    private JsonSerializerOptions Options => fixture.Services
        .GetRequiredService<IOptions<JsonOptions>>()
        .Value
        .JsonSerializerOptions;

    [Theory]
    [InlineData("Male", Gender.Male)]
    [InlineData("Female", Gender.Female)]
    public void A_gender_sent_as_a_name_binds_to_the_enum(string sent, Gender expected)
    {
        var dto = JsonSerializer.Deserialize<AdminUpdateUserDto>(
            $$"""{"email":"a@b.test","displayName":"A B","gender":"{{sent}}"}""", Options);

        Assert.Equal(expected, dto!.Gender);
    }

    [Theory]
    [InlineData(Gender.Male, "Male")]
    [InlineData(Gender.Female, "Female")]
    public void A_gender_comes_back_as_a_name_not_a_number(Gender stored, string expected)
    {
        var json = JsonSerializer.Serialize(new AdminUserDto { Gender = stored }, Options);

        // The property name matters as much as the value: the client reads
        // `gender`, so a PascalCase key would be as invisible to it as a number.
        Assert.Contains($"\"gender\":\"{expected}\"", json);
    }

    /// <summary>
    /// What the dialog's "Not specified" option sends. A null has to arrive as a
    /// null, because the handler assigns it straight onto the user and that is
    /// what clears a value set by mistake.
    /// </summary>
    [Fact]
    public void An_explicit_null_gender_binds_as_null()
    {
        var dto = JsonSerializer.Deserialize<AdminUpdateUserDto>(
            """{"email":"a@b.test","displayName":"A B","gender":null}""", Options);

        Assert.Null(dto!.Gender);
    }

    /// <summary>
    /// An older client that has never heard of the field must not be read as having
    /// chosen either answer.
    /// </summary>
    [Fact]
    public void An_omitted_gender_binds_as_null()
    {
        var dto = JsonSerializer.Deserialize<AdminUpdateUserDto>(
            """{"email":"a@b.test","displayName":"A B"}""", Options);

        Assert.Null(dto!.Gender);
    }

    /// <summary>
    /// A number is what the enum would serialise as with no converter registered,
    /// so this is the shape the *previous* contract had. Accepting it is harmless
    /// and keeps a stale caller working; the test exists to record that the
    /// name-based spelling above is the one the client relies on.
    /// </summary>
    [Fact]
    public void A_gender_that_is_not_a_known_name_is_refused_rather_than_guessed()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AdminUpdateUserDto>(
            """{"email":"a@b.test","displayName":"A B","gender":"Other"}""", Options));
    }
}
