using Nexaflow.IO.Common.Locations;

namespace Nexaflow.Core.Models;

/// <summary>The connections a workspace keeps, as their config file holds them: endpoints only, never a secret
/// (<see cref="Services.WorkspaceConnections"/>).</summary>
public sealed class ConnectionsConfig
{
    /// <summary>The config's folder under the workspace: <c>connections\config_&lt;version&gt;.json</c>.</summary>
    public const string Name = "connections";

    public List<ConnectionProfile> Connections { get; set; } = [];
}
