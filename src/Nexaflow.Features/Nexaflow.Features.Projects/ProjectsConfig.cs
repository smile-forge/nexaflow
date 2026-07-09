using Nexaflow.Features.Common;
using Nexaflow.Features.Projects.Controls;
using Nexaflow.Features.Projects.Model;

namespace Nexaflow.Features.Projects;

/// <summary>
/// Per-workspace Projects settings: whether the feature is on, the three project-folder locations
/// (active / shelf / archive), and the configurable backlog workflow states. Edited through
/// <see cref="ProjectsConfigEditorControl"/> (a <see cref="CustomControlAttribute"/> replaces the default
/// property grid so the status list can be added/relabelled/recoloured/reordered).
/// </summary>
[CustomControl(typeof(ProjectsConfigEditorControl))]
[WorkspaceScopedConfig]
[MandatorySetup]
public sealed class ProjectsConfig : IFeatureConfig
{
    public string ConfigName   => "projects";
    public string FriendlyName => "Projects";

    /// <summary>Whether the Projects feature is enabled for this workspace.</summary>
    public bool EnableProjects { get; set; }

    /// <summary>Root of the active projects.</summary>
    public string ProjectDirectory { get; set; } = @"C:\Projects";

    /// <summary>Where "Archive" moves a project to.</summary>
    public string ArchiveDirectory { get; set; } = @"C:\Projects\Archive";

    /// <summary>Where "Shelf" moves a project to.</summary>
    public string ShelfDirectory { get; set; } = @"C:\Projects\Shelf";

    /// <summary>The configurable backlog workflow states. Order = forward-progression order. Seeded to
    /// the built-in nine on first use; a backlog item references a status by <see cref="BacklogStatusDef.Key"/>.</summary>
    public List<BacklogStatusDef> BacklogStatuses { get; set; } = [];

    public ProjectsConfig() => EnsureSeeded();

    /// <summary>
    /// Resolves a stored status key to its live definition, or a synthesized <b>invalid</b> sentinel
    /// (grey, non-progressable, name preserved) when the key is no longer configured. This is what keeps
    /// a deleted status from silently remapping or losing a backlog item.
    /// </summary>
    public BacklogStatusDef Resolve(string? key)
        => BacklogStatuses.FirstOrDefault(s => s.Key == key)
           ?? new BacklogStatusDef
           {
               Key       = key ?? string.Empty,
               Label     = string.IsNullOrEmpty(key) ? "(none)" : key,
               SwatchKey = "Swatch.Slate",
               IsValid   = false,
           };

    /// <summary>The one terminal "cancelled" state (or null if the user removed it).</summary>
    public BacklogStatusDef? TerminalStatus => BacklogStatuses.FirstOrDefault(s => s.IsTerminalCancelled);

    private void EnsureSeeded()
    {
        if (BacklogStatuses.Count > 0) return;
        BacklogStatuses =
        [
            new() { Key = "NotStarted",                   Label = "Not Started",                    SwatchKey = "Swatch.Slate"  },
            new() { Key = "AwaitingDesign",               Label = "Awaiting Design",                SwatchKey = "Swatch.Blue"   },
            new() { Key = "AwaitingDesignReview",         Label = "Awaiting Design Review",         SwatchKey = "Swatch.Cyan"   },
            new() { Key = "AwaitingImplementation",       Label = "Awaiting Implementation",        SwatchKey = "Swatch.Purple" },
            new() { Key = "AwaitingImplementationReview", Label = "Awaiting Implementation Review", SwatchKey = "Swatch.Pink"   },
            new() { Key = "AwaitingTestImplementation",   Label = "Awaiting Test Implementation",   SwatchKey = "Swatch.Amber"  },
            new() { Key = "AwaitingTestReview",           Label = "Awaiting Test Review",           SwatchKey = "Swatch.Orange" },
            new() { Key = "AwaitingFinalisation",         Label = "Awaiting Finalisation",          SwatchKey = "Swatch.Green"  },
            new() { Key = "Cancelled",                    Label = "Cancelled",                      SwatchKey = "Swatch.Red", IsTerminalCancelled = true },
        ];
    }
}
