using Nexaflow.Core.AI;
using Nexaflow.Core.Services;

namespace Nexaflow.Core.Models;

/// <summary>
/// A runtime workspace: a group of one-or-more window frames (<see cref="MainWindow"/>) all running
/// ONE <see cref="Profile"/>. Owns the per-Workspace live services — the window/tab registry
/// (<see cref="ShellServices"/>), the agent loop + conversation session (<see cref="AIService"/>),
/// and an acquired set of provider instances (deduplicated process-wide by
/// <see cref="ProviderManager"/>). Created fresh on every app/IPC launch; disposed when its last
/// window closes. Switching the dropdown reconfigures this object in place (see WorkspaceManager).
/// </summary>
public sealed class Workspace
{
    public Profile Profile { get; private set; }

    public string Name  => Profile.Name;
    public string Color => Profile.Color;
    public string Icon  => Profile.Icon;

    /// <summary>The profile's shared ability grid (delegated for code that reads <c>ws.AiConfig</c>).</summary>
    public AiConfig AiConfig => Profile.AiConfig;

    /// <summary>The profile's assistant persona (name + system prompt).</summary>
    public AiPersonaConfig Persona => Profile.Persona;

    /// <summary>Provider instances acquired from the global pool; released on dispose/reconfigure.</summary>
    public ProviderSet?   Providers     { get; internal set; }
    public AIService?     AiService     { get; internal set; }
    public ShellServices? ShellServices { get; internal set; }

    public Workspace(Profile profile) => Profile = profile;

    /// <summary>Convenience constructor for tests and defaults.</summary>
    public Workspace() : this(new Profile()) { }

    internal void RepointProfile(Profile profile) => Profile = profile;
}
