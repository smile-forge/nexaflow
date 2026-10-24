namespace Nexaflow.IO.Common.Locations;

/// <summary>
/// One protocol — SFTP, SMB, WSL. A backend assembly (<c>Nexaflow.IO.Location.&lt;Kind&gt;</c>, referencing
/// <c>IO.Common</c> only) implements it with a parameterless constructor and is discovered by reflection, so adding
/// the next protocol touches no feature. Stateless: every live connection is an <see cref="ILocationSession"/>.
/// </summary>
public interface ILocationProvider
{
    /// <summary>The <see cref="ConnectionProfile.Kind"/> this provider reaches, lower-case — <c>"sftp"</c>. Also the
    /// scheme of the location's display form (<c>sftp://adam@nas/…</c>).</summary>
    string Kind { get; }

    /// <summary>Everything a session of this provider can do. A server may offer less (no hash, no watch); a
    /// session never offers more.</summary>
    LocationCapabilities Capabilities { get; }

    /// <summary>Opens a session on the profile's host, asking <paramref name="credentials"/> for what it needs to
    /// sign in. Fails with <see cref="LocationException"/> — <see cref="LocationError.AuthFailed"/>,
    /// <see cref="LocationError.HostKeyChanged"/>, <see cref="LocationError.Disconnected"/> when the host cannot be reached.</summary>
    ValueTask<ILocationSession> ConnectAsync(ConnectionProfile profile, ICredentialSource credentials, CancellationToken ct);
}
