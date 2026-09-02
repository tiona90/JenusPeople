namespace Application.ProjectTypes.DTOs;

public class ProjectTypeDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = "🗂️";
    public string ColorKey { get; set; } = "default";
    public bool IsActive { get; set; }

    // How many projects are classified as this type. Also what makes a type
    // undeletable — see DeleteProjectType.
    public int UsedInProjects { get; set; }
}
