using System.ComponentModel.DataAnnotations;

namespace Application.AdminUsers.DTOs;

public class AdminSetUserRolesDto
{
    [MinLength(1)]
    public List<string> Roles { get; set; } = new();
}