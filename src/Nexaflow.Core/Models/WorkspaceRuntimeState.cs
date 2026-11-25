namespace Nexaflow.Core.Models;

/// <summary>Where a <see cref="WorkspaceRuntime"/> is in its life.</summary>
public enum WorkspaceRuntimeState
{
    /// <summary>Built and its providers acquired; no window has attached yet.</summary>
    Starting,

    /// <summary>A window shows it, or its background work still runs.</summary>
    Active,

    /// <summary>
    /// No window shows it and nothing of its runs: its providers are back in the pool, so local models nothing else uses
    /// unload, while its configuration, assistant, file system and feature instances stay — a window attaching resumes it at
    /// once, its tabs as they were.
    /// </summary>
    Dormant,

    /// <summary>Ended: it holds nothing, and nothing more is built for it.</summary>
    Ended,
}
