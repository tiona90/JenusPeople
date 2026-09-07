namespace Application.AdminUsers.DTOs;

/// <summary>
/// The state to put the account into, rather than a verb like "deactivate", so
/// the request is idempotent: the panel sends the state it wants and a repeated
/// or racing call cannot flip the account back the other way.
/// </summary>
public class AdminSetUserActiveDto
{
    public bool IsActive { get; set; }
}
