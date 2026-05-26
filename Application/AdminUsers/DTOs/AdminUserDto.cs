namespace Application.AdminUsers.DTOs;

public class AdminUserDto
{
    public string Id { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
}