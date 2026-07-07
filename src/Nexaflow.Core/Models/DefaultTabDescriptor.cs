using System.Collections.Generic;

namespace Nexaflow.Core.Models;

/// <summary>
/// One tab in a workspace's saved startup layout (<see cref="Workspace.DefaultTabs"/>). Carries just
/// enough to recreate the tab via <c>IShellServices.OpenTab</c> (<see cref="PageKind"/> +
/// <see cref="PageParams"/>) plus which pane it belongs to and a display <see cref="Title"/> for the
/// Configure panel's list. Serialized inline in the workspace list (workcontexts.json).
/// </summary>
public sealed class DefaultTabDescriptor
{
    /// <summary>The page kind to reopen (e.g. "FileSystem"). Empty descriptors are skipped on restore.</summary>
    public string PageKind { get; set; } = string.Empty;

    /// <summary>The params the tab was opened with (e.g. { "mode": "thispc" }); null for a type-identity tab.</summary>
    public Dictionary<string, string>? PageParams { get; set; }

    /// <summary>Which pane the tab lived in: 0 = primary/left, 1 = the right pane of a split.</summary>
    public int Pane { get; set; }

    /// <summary>Human-readable label shown in the Configure panel's default-tabs list (display only).</summary>
    public string? Title { get; set; }

    /// <summary>Whether this tab was the active one in its pane (reopened last so it ends active).</summary>
    public bool IsActive { get; set; }

    /// <summary>The out-of-the-box startup tab: the file explorer showing "This PC". Used to seed a
    /// workspace that has never had its tabset configured, so <see cref="Workspace.DefaultTabs"/> is never empty
    /// by accident (an empty list is an explicit user choice to start with no tabs).</summary>
    public static DefaultTabDescriptor ThisPc() => new()
    {
        PageKind   = "FileSystem",
        PageParams = new() { ["mode"] = "thispc" },
        Pane       = 0,
        Title      = "This PC",
        IsActive   = true,
    };
}
