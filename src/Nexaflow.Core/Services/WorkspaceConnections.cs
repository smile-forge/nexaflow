using Nexaflow.Core.Models;
using Nexaflow.IO.Common.Locations;

namespace Nexaflow.Core.Services;

/// <summary>
/// The connections a workspace keeps. They are saved with it under <c>connections\</c>, beside its provider configs, through
/// the config store's versioned files — so they carry over an update as every config does — and their secrets live in
/// Windows Credential Manager under each connection's target, never in the file. A connection's id never changes once
/// saved: it is baked into tab state as <c>::{id}</c>, so editing a connection, its label included, keeps it.
/// </summary>
public sealed class WorkspaceConnections
{
    private readonly string _dir;
    private readonly ISecretStore _secrets;
    private readonly ConnectionsConfig _config = new();
    private readonly Lock _gate = new();

    /// <param name="dir">The workspace's folder.</param>
    /// <param name="secrets">Where the connections' secrets are kept.</param>
    public WorkspaceConnections(string dir, ISecretStore secrets)
    {
        _dir     = dir;
        _secrets = secrets;
        ConfigManager.Instance.LoadFrom(dir, _config, ConnectionsConfig.Name);
    }

    /// <summary>Every saved connection, in the order they were added.</summary>
    public IReadOnlyList<ConnectionProfile> All
    {
        get { lock (_gate) return [.. _config.Connections]; }
    }

    /// <summary>Raised after a connection is saved or removed, on the thread that did it.</summary>
    public event Action? Changed;

    /// <summary>Saves <paramref name="connection"/>: a new one is added, and one whose id is saved is replaced in place.</summary>
    /// <exception cref="ArgumentException">Its id is not one path segment, or it names no kind or no host.</exception>
    public void Save(ConnectionProfile connection)
    {
        if (!LocationPath.IsSegmentSafeId(connection.Id))
            throw new ArgumentException($"A connection's id '{connection.Id}' must be one path segment: not empty, with no \\, / or :.",
                                        nameof(connection));
        if (string.IsNullOrWhiteSpace(connection.Kind))
            throw new ArgumentException("A connection names the kind of location it reaches.", nameof(connection));
        if (string.IsNullOrWhiteSpace(connection.Host))
            throw new ArgumentException("A connection names the host it reaches.", nameof(connection));

        lock (_gate)
        {
            int at = _config.Connections.FindIndex(c => SameId(c.Id, connection.Id));
            if (at >= 0) _config.Connections[at] = connection;
            else         _config.Connections.Add(connection);
            Persist();
        }
        Changed?.Invoke();
    }

    /// <summary>Stores the secret of <paramref name="kind"/> for the connection saved as <paramref name="id"/>, replacing
    /// one stored before.</summary>
    /// <exception cref="ArgumentException">No connection is saved as <paramref name="id"/>.</exception>
    public void SetSecret(string id, SecretKind kind, string secret)
    {
        var connection = Find(id) ?? throw new ArgumentException($"No connection is saved as '{id}'.", nameof(id));
        _secrets.Write(connection.CredentialTargetFor(kind), connection.User, secret);
    }

    /// <summary>Removes the connection saved as <paramref name="id"/>, and forgets every secret stored for it. False when
    /// none is saved as that.</summary>
    public bool Remove(string id)
    {
        if (Find(id) is not { } connection) return false;

        // The secrets go first: a connection left saved without them asks again, where secrets left behind outlive it.
        foreach (var kind in Enum.GetValues<SecretKind>()) _secrets.Delete(connection.CredentialTargetFor(kind));
        lock (_gate)
        {
            _config.Connections.RemoveAll(c => SameId(c.Id, id));
            Persist();
        }
        Changed?.Invoke();
        return true;
    }

    private ConnectionProfile? Find(string id)
    {
        lock (_gate) return _config.Connections.Find(c => SameId(c.Id, id));
    }

    /// <summary>Ids compare as the file system's location roots do, ignoring case.</summary>
    private static bool SameId(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private void Persist() => ConfigManager.Instance.SaveTo(_dir, _config, ConnectionsConfig.Name);
}
