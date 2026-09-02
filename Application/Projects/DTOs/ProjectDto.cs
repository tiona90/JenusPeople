using Domain;

namespace Application.Projects.DTOs;

public class ProjectDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public ProjectStatus Status { get; set; }
    /// <summary>
    /// The departments this project belongs to, and therefore who can see it.
    /// Only an admin is ever handed a project with none.
    /// </summary>
    public List<ProjectDepartmentDto> Departments { get; set; } = new();
    public string? OwnerId { get; set; }
    public string? OwnerName { get; set; }
    public string ColorKey { get; set; } = "p1";
    public int TargetWeeklyHours { get; set; }
    public int TargetMonthlyHours { get; set; }
    public DateTime CreatedAt { get; set; }

    public decimal HoursThisWeek { get; set; }
    public decimal HoursThisMonth { get; set; }
    public decimal HoursYTD { get; set; }
    public int TeamSize { get; set; }
    public List<ProjectTeamMemberDto> Team { get; set; } = new();

    /// <summary>
    /// The activity types this project logs time against. Empty means the project
    /// has not narrowed the catalogue, and every active activity type applies.
    /// </summary>
    public List<ProjectActivityDto> Activities { get; set; } = new();
}

public class ProjectDepartmentDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class ProjectActivityDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "🏷️";
    public string ColorKey { get; set; } = "default";
}

public class ProjectTeamMemberDto
{
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public decimal HoursThisWeek { get; set; }
}
