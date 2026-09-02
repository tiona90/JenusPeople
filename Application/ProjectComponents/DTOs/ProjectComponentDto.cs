namespace Application.ProjectComponents.DTOs;

public class ProjectComponentDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = "🧩";
    public string ColorKey { get; set; } = "default";
    public bool IsActive { get; set; }

    // How many projects have declared this component.
    public int UsedInProjects { get; set; }
}
