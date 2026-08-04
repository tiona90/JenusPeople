using System.Text;
using Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Persistence;
using Xunit;

namespace WorkTrack.Tests;

/// <summary>
/// Exercises the password-reset token round-trip against a real Identity
/// UserManager and DataProtection token provider: the value emailed to the user
/// is base64url(utf8(token)), and the reset endpoint must decode it back to a
/// token that <c>ResetPasswordAsync</c> accepts.
///
/// A mismatch here surfaces to the user as Identity's "Invalid token." — the same
/// wording appears when the token is stale or the security stamp has moved, so
/// these tests separate a genuine encoding bug from an environmental one.
/// </summary>
public class PasswordResetTokenTests : IDisposable
{
    private readonly ServiceProvider _services;

    public PasswordResetTokenTests()
    {
        var collection = new ServiceCollection();

        collection.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        collection.AddDataProtection();
        collection.AddDbContext<AppDbContext>(options => options
            .UseInMemoryDatabase($"reset-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

        collection.AddIdentityCore<User>(options =>
            {
                options.Password.RequiredLength = 6;
                options.Password.RequireNonAlphanumeric = false;
                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        _services = collection.BuildServiceProvider();
    }

    public void Dispose() => _services.Dispose();

    private UserManager<User> UserManager => _services.GetRequiredService<UserManager<User>>();

    private async Task<User> CreateUserAsync(UserManager<User> userManager)
    {
        var user = new User
        {
            UserName = "person@example.test",
            Email = "person@example.test",
            DisplayName = "Person",
            EmailConfirmed = true,
        };

        var created = await userManager.CreateAsync(user, "OldPassw0rd!");
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));

        return user;
    }

    /// <summary>How AccountController encodes the token into the emailed link.</summary>
    private static string Encode(string token) =>
        WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    /// <summary>How the reset endpoint decodes it again.</summary>
    private static string Decode(string encoded) =>
        Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encoded));

    [Fact]
    public async Task An_emailed_token_survives_encoding_and_resets_the_password()
    {
        var userManager = UserManager;
        var user = await CreateUserAsync(userManager);

        var emailed = Encode(await userManager.GeneratePasswordResetTokenAsync(user));

        var result = await userManager.ResetPasswordAsync(user, Decode(emailed), "BrandNewPassw0rd!");

        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        Assert.True(await userManager.CheckPasswordAsync(user, "BrandNewPassw0rd!"));
    }

    /// <summary>
    /// The encoded form must be URL-safe, or the token would arrive mangled after
    /// a round trip through the query string.
    /// </summary>
    [Fact]
    public async Task The_encoded_token_is_url_safe()
    {
        var userManager = UserManager;
        var user = await CreateUserAsync(userManager);

        var emailed = Encode(await userManager.GeneratePasswordResetTokenAsync(user));

        Assert.DoesNotContain('+', emailed);
        Assert.DoesNotContain('/', emailed);
        Assert.DoesNotContain('=', emailed);
        Assert.Equal(emailed, Uri.EscapeDataString(emailed));
    }

    /// <summary>
    /// Documents the most likely cause of a real-world "Invalid token.": anything
    /// that moves the security stamp — including the seeder's EnsurePassword —
    /// invalidates every outstanding reset link for that user.
    /// </summary>
    [Fact]
    public async Task A_password_change_invalidates_an_outstanding_reset_link()
    {
        var userManager = UserManager;
        var user = await CreateUserAsync(userManager);

        var emailed = Encode(await userManager.GeneratePasswordResetTokenAsync(user));

        // Something else changes the password first (e.g. an admin, or startup seeding).
        await userManager.RemovePasswordAsync(user);
        await userManager.AddPasswordAsync(user, "InterveningPassw0rd!");

        var result = await userManager.ResetPasswordAsync(user, Decode(emailed), "BrandNewPassw0rd!");

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Code == "InvalidToken");
    }
}
