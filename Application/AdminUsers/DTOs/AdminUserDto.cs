namespace Application.AdminUsers.DTOs;

public class AdminUserDto
{
    public string Id { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public bool EmailConfirmed { get; set; }
    public List<string> Roles { get; set; } = new();

    /// <summary>
    /// Set only on the create response: whether the welcome email carrying the
    /// set-your-password link actually left the building. Null elsewhere, since
    /// there is nothing to report when simply reading a user.
    /// </summary>
    public bool? InviteEmailSent { get; set; }
}