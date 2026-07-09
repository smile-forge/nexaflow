namespace Nexaflow.Features.Projects.Model;

/// <summary>
/// The serialized <c>.project</c> model (v2). <see cref="Description"/> is markdown; objectives became
/// the <see cref="CompletionCriteria"/> list (each with a status); a backlog item's notes/plans collapsed
/// into its own markdown description. Old (pre-2.0) files are mapped forward on load by
/// <see cref="ProjectMigration"/>; see <see cref="SchemaVersion"/>.
/// </summary>
public class ProjectInfo
{
    /// <summary>The current on-disk schema version.</summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>On-disk schema version. Absent / &lt; 2 = a legacy file mapped in-memory (needs upgrade to
    /// persist); 2 = current.</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string Name { get; set; } = string.Empty;

    /// <summary>Markdown.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>The "should this be true?" acceptance list (was the flat <c>Objectives</c> string list).</summary>
    public List<CompletionCriterion> CompletionCriteria { get; set; } = [];

    public string? SolutionFile { get; set; }
    public DateTime? LastUpdate { get; set; }
    public List<BacklogItem> Backlog { get; set; } = [];
}
