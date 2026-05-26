using System.Text;
using Application.Core;
using Domain;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace Application.Accounts.Commands;

public class VerifyEmail
{
    public class Command : IRequest<Result<VerifyEmailResultDto>>
    {
        public string? UserId { get; set; }
        public string? Token { get; set; }
    }

    public class VerifyEmailResultDto
    {
        public bool IsConfirmed { get; init; }
        public string Title { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
    }

    public class Handler(UserManager<User> userManager)
        : IRequestHandler<Command, Result<VerifyEmailResultDto>>
    {
        public async Task<Result<VerifyEmailResultDto>> Handle(Command request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.Token))
            {
                return Outcome(false,
                    "Verification link invalid",
                    "We could not verify your email because the link is incomplete or invalid.");
            }

            var user = await userManager.FindByIdAsync(request.UserId);
            if (user is null)
            {
                return Outcome(false,
                    "Verification failed",
                    "This verification link is no longer valid or the account could not be found.");
            }

            if (user.EmailConfirmed)
            {
                return Outcome(true,
                    "Email already confirmed",
                    "Your email address has already been verified. You can sign in to your account.");
            }

            string decodedToken;
            try
            {
                decodedToken = Encoding.UTF8.GetString(Base64UrlDecode(request.Token));
            }
            catch
            {
                return Outcome(false,
                    "Verification token invalid",
                    "The confirmation token could not be read. Please request a new verification email.");
            }

            var result = await userManager.ConfirmEmailAsync(user, decodedToken);
            if (!result.Succeeded)
            {
                var errorMessage = result.Errors.Select(e => e.Description).FirstOrDefault()
                    ?? "Email verification failed.";

                return Outcome(false, "Verification failed", errorMessage);
            }

            return Outcome(true,
                "Email confirmed",
                "Thanks for confirming your email address. Your account is now active and you can log in.");
        }

        private static Result<VerifyEmailResultDto> Outcome(bool isConfirmed, string title, string message) =>
            Result<VerifyEmailResultDto>.Success(new VerifyEmailResultDto
            {
                IsConfirmed = isConfirmed,
                Title = title,
                Message = message
            });

        private static byte[] Base64UrlDecode(string input)
        {
            var padded = input.Replace('-', '+').Replace('_', '/');
            var padding = padded.Length % 4;
            if (padding > 0)
            {
                padded = padded.PadRight(padded.Length + (4 - padding), '=');
            }
            return Convert.FromBase64String(padded);
        }
    }
}
