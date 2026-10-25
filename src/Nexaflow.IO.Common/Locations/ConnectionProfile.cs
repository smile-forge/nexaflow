namespace Nexaflow.IO.Common.Locations;

/// <summary>
/// A saved, named endpoint: which provider reaches it, where, as whom, and where browsing starts. It holds no
/// secret — the password or key passphrase lives in Windows Credential Manager under <see cref="CredentialTargetFor"/>,
/// so a profile can be saved, listed and shown to the assistant by label without carrying one.
/// </summary>
/// <param name="Id">Stable, path-segment-safe key (<see cref="LocationPath.IsSegmentSafeId"/>) forming the virtual
/// root <c>::{Id}</c>. Never the label: it is baked into saved tab state, so renaming must not change it.</param>
/// <param name="Kind">The <see cref="ILocationProvider.Kind"/> that reaches it — <c>"sftp"</c>, <c>"smb"</c>, <c>"wsl"</c>.</param>
/// <param name="Label">What people see: the breadcrumb root and the This PC row.</param>
/// <param name="Host">The host name or address; for WSL the distribution's name.</param>
public sealed record ConnectionProfile(string Id, string Kind, string Label, string Host)
{
    /// <summary>The prefix every connection's Credential Manager target starts with.</summary>
    public const string CredentialTargetPrefix = "nexaflow:";

    private static readonly IReadOnlyDictionary<string, string> NoOptions = new Dictionary<string, string>();

    /// <summary>The port, or null for the provider's default.</summary>
    public int? Port { get; init; }

    /// <summary>The account to sign in as, or null where the provider signs in by other means (WSL, an SMB share
    /// Windows already reaches).</summary>
    public string? User { get; init; }

    /// <summary>The provider path browsing starts at (<c>/home/adam</c>), or null for the location's root.</summary>
    public string? Root { get; init; }

    /// <summary>Provider-specific settings by name — a key file, a share, a passive-mode flag.</summary>
    public IReadOnlyDictionary<string, string> Options { get; init; } = NoOptions;

    /// <summary>The Windows Credential Manager target holding this connection's secret of <paramref name="kind"/>:
    /// <c>nexaflow:{Id}</c> for its password, the kind after it for any other (<c>nexaflow:{Id}:keypassphrase</c>).</summary>
    public string CredentialTargetFor(SecretKind kind)
        => kind == SecretKind.Password
            ? CredentialTargetPrefix + Id
            : $"{CredentialTargetPrefix}{Id}:{kind.ToString().ToLowerInvariant()}";

    // Value equality over Options too: two profiles that differ only in a key file are different endpoints, and
    // the record's generated equality would compare the dictionaries by reference.
    public bool Equals(ConnectionProfile? other)
        => other is not null
           && string.Equals(Id, other.Id, System.StringComparison.Ordinal)
           && string.Equals(Kind, other.Kind, System.StringComparison.Ordinal)
           && string.Equals(Label, other.Label, System.StringComparison.Ordinal)
           && string.Equals(Host, other.Host, System.StringComparison.Ordinal)
           && Port == other.Port
           && string.Equals(User, other.User, System.StringComparison.Ordinal)
           && string.Equals(Root, other.Root, System.StringComparison.Ordinal)
           && Options.Count == other.Options.Count
           && Options.All(o => other.Options.TryGetValue(o.Key, out var v) && string.Equals(o.Value, v, System.StringComparison.Ordinal));

    public override int GetHashCode()
    {
        var hash = new System.HashCode();
        hash.Add(Id, System.StringComparer.Ordinal);
        hash.Add(Kind, System.StringComparer.Ordinal);
        hash.Add(Host, System.StringComparer.Ordinal);
        hash.Add(Port);
        hash.Add(User, System.StringComparer.Ordinal);
        hash.Add(Root, System.StringComparer.Ordinal);
        hash.Add(Options.Count);
        return hash.ToHashCode();
    }
}
