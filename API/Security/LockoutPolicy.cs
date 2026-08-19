namespace API.Security;

/// <summary>
/// Account-lockout policy for failed sign-ins, applied to Identity in
/// <c>Program.cs</c>.
///
/// The numbers live here rather than inline so the values the application
/// configures are the values the tests assert. A test proving "5 failures locks
/// the account" is worthless if it carries its own copy of the 5 and keeps
/// passing after someone raises the real limit to 500.
///
/// Note that these only bite because <c>AccountController.Login</c> calls
/// <c>PasswordSignInAsync</c> with <c>lockoutOnFailure: true</c> — configured
/// limits count for nothing if the sign-in call does not opt into them.
/// </summary>
public static class LockoutPolicy
{
    /// <summary>Consecutive failed sign-ins before the account locks.</summary>
    public const int MaxFailedAccessAttempts = 5;

    /// <summary>
    /// How long a lockout lasts. Long enough to make online guessing
    /// impractical, short enough that a locked-out colleague is not raising a
    /// support ticket.
    /// </summary>
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
}
