using Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace API.Security;

/// <summary>
/// Teaches Identity that a deactivated account may not sign in.
///
/// The check lives here rather than in <c>AccountController.Login</c> so that it
/// cannot be bypassed: <c>CanSignInAsync</c> is consulted by every sign-in path
/// Identity offers, and it runs before any cookie is issued, so a deactivated
/// user is refused rather than signed in and then signed back out.
///
/// A refusal surfaces to the caller as <c>SignInResult.NotAllowed</c> — the same
/// result an unverified email produces — which is why Login has to look at
/// <c>IsActive</c> again to pick the right message.
/// </summary>
public class ActiveUserSignInManager(
    UserManager<User> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<User> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<User>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<User> confirmation)
    : SignInManager<User>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    public override async Task<bool> CanSignInAsync(User user)
    {
        if (!user.IsActive)
        {
            Logger.LogWarning(
                "User {UserId} cannot sign in: the account is deactivated.",
                await UserManager.GetUserIdAsync(user));
            return false;
        }

        return await base.CanSignInAsync(user);
    }
}
