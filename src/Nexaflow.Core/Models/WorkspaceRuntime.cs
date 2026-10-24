using Nexaflow.Core.AI;
using Nexaflow.Core.Services;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;

namespace Nexaflow.Core.Models;

/// <summary>
/// A runtime workspace: a group of one-or-more window frames (<see cref="MainWindow"/>) all running
/// ONE <see cref="Workspace"/>. Owns the per-Workspace live services — the window/tab registry
/// (<see cref="ShellServices"/>), the agent loop + conversation session (<see cref="AIService"/>),
/// and an acquired set of provider instances (deduplicated process-wide by
/// <see cref="ProviderManager"/>). Created fresh on every app/IPC launch; disposed when its last
/// window closes. Switching the dropdown reconfigures this object in place (see WorkspaceManager).
/// </summary>
public sealed class WorkspaceRuntime
{
    public Workspace Workspace { get; private set; }

    public string Name  => Workspace.Name;
    public string Color => Workspace.Color;
    public string Icon  => Workspace.Icon;

    /// <summary>The workspace's shared ability grid (delegated for code that reads <c>ws.AiConfig</c>).</summary>
    public AiConfig AiConfig => Workspace.AiConfig;

    /// <summary>The workspace's assistant persona (name + system prompt).</summary>
    public AiPersonaConfig Persona => Workspace.Persona;

    /// <summary>Provider instances acquired from the global pool; released on dispose/reconfigure.</summary>
    public ProviderSet?   Providers     { get; internal set; }
    public AIService?     AiService     { get; internal set; }
    public ShellServices? ShellServices { get; internal set; }

    /// <summary>
    /// This runtime's file system: the shared <see cref="VirtualFileSystem"/> plus the remote locations this runtime
    /// has connected, which the process-wide instance cannot see. Features are given it as
    /// <see cref="IVirtualFileSystem"/>.
    /// </summary>
    public LocationFileSystem Vfs { get; } = new(VirtualFileSystem.Instance);

    public WorkspaceRuntime(Workspace workspace) => Workspace = workspace;

    /// <summary>Convenience constructor for tests and defaults.</summary>
    public WorkspaceRuntime() : this(new Workspace()) { }

    internal void RepointWorkspace(Workspace workspace) => Workspace = workspace;

    /// <summary>
    /// Raised when the runtime's providers are registered anew, or one of them becomes available or stops being — a
    /// model finishing its download, say. What depends on a kind of model being there (dictation) listens. Raised on
    /// whichever thread made the change.
    /// </summary>
    public event EventHandler? ProvidersChanged;

    internal void RaiseProvidersChanged() => ProvidersChanged?.Invoke(this, EventArgs.Empty);
}
