namespace Domain;

// A deliverable a project is made of (DM, Lasernet, jDocs, …). An org-wide
// catalogue admins curate, mirroring how ProjectActivityType is configured —
// the difference being that an activity type says what kind of work was done,
// while a component says which part of the product it was done on.
//
// Nothing references a component yet: projects do not narrow the catalogue and
// timesheet entries do not log against one, so a component can always be
// deleted.
public class ProjectComponent
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public string Icon { get; set; } = "🧩";
    public string ColorKey { get; set; } = "default";

    // Enabled components are the ones offered elsewhere; disabled ones stay in
    // the catalogue but are hidden from pickers.
    public bool IsActive { get; set; } = true;
}
