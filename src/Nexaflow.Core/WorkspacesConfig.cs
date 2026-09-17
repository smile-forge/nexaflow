using System.IO;
using Nexaflow.Core.Controls;
using Nexaflow.Core.Models;
using Nexaflow.Features.Common;

namespace Nexaflow.Core;

[CustomControl(typeof(WorkspaceConfigControl))]
public sealed class WorkspacesConfig : IFeatureConfig
{
    // Kept as "workcontexts" / "Contexts" so existing on-disk config (workcontexts.json) still loads.
    public string ConfigName   => "workcontexts";
    public string FriendlyName => "Workspaces";

    /// <summary>
    /// The saved workspaces, in the order the selector lists them. Every value that reaches this list —
    /// read off disk, carried forward by a migration, or rebuilt by
    /// <c>WorkspaceManager.SaveWorkspaces</c>, the only writer — is put through <see cref="Sanitize"/>, so
    /// a hand-edited or half-written file yields a usable list instead of taking the app down on a name
    /// that is not a folder name or an entry that is not there at all.
    /// </summary>
    private List<Workspace> _contexts = [new Workspace()];
    public List<Workspace> Contexts
    {
        get => _contexts;
        set => _contexts = Sanitize(value);
    }

    /// <summary>
    /// The list as the app can run on it: no null entries, at least one workspace (the startup window
    /// opens the first), every name a usable folder name, and no two workspaces sharing one — two that
    /// did would share a data folder, so deleting either would take the other's conversations with it.
    /// Tab lists lose their null entries, and a last session that holds nothing becomes no offer at all.
    /// </summary>
    private static List<Workspace> Sanitize(List<Workspace>? raw)
    {
        var clean = new List<Workspace>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var workspace in raw ?? [])
        {
            if (workspace is null) continue;

            workspace.Name = UniqueName(FolderSafeName(workspace.Name), taken);
            if (string.IsNullOrWhiteSpace(workspace.Color)) workspace.Color = new Workspace().Color;
            if (string.IsNullOrWhiteSpace(workspace.Icon))  workspace.Icon  = new Workspace().Icon;

            workspace.DefaultTabs     = [.. workspace.DefaultTabs.Where(t => t is not null)];
            workspace.LastSessionTabs = workspace.LastSessionTabs?.Where(t => t is not null).ToList() is { Count: > 0 } last
                ? last
                : null;

            clean.Add(workspace);
        }

        return clean.Count > 0 ? clean : [new Workspace()];
    }

    /// <summary>A name that is a legal folder name: the invalid characters dropped, trimmed, and falling
    /// back to the default when nothing usable is left (the name IS the data folder — see WorkspaceDir).</summary>
    private static string FolderSafeName(string? name)
    {
        var kept = new string((name ?? string.Empty)
            .Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Trim();
        return kept.Length > 0 ? kept : new Workspace().Name;
    }

    /// <summary>Records <paramref name="name"/> in <paramref name="taken"/>, appending " 2", " 3", … when
    /// an earlier workspace already claimed it (folder names collide case-insensitively).</summary>
    private static string UniqueName(string name, HashSet<string> taken)
    {
        if (taken.Add(name)) return name;
        for (int i = 2; ; i++)
        {
            var candidate = $"{name} {i}";
            if (taken.Add(candidate)) return candidate;
        }
    }
}
