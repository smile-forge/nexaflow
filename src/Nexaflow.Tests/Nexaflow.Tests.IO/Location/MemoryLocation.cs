using Nexaflow.IO.Common.Locations;

namespace Nexaflow.Tests.IO.Location;

/// <summary>Hands out one connected session, connecting on first use — the shape the session pool takes.</summary>
internal sealed class MemoryLocationSessionSource(ILocationProvider provider, ConnectionProfile profile, ICredentialSource credentials)
    : ILocationSessionSource
{
    private ILocationSession? _session;

    public async ValueTask<ILocationSession> GetSessionAsync(CancellationToken ct)
        => _session ??= await provider.ConnectAsync(profile, credentials, ct);
}

/// <summary>Credentials that answer with one stored password and refuse to prompt.</summary>
internal sealed class StoredCredentials(string? password) : ICredentialSource
{
    public ValueTask<string?> GetSecretAsync(ConnectionProfile connection, SecretKind kind, CancellationToken ct)
        => ValueTask.FromResult(kind == SecretKind.Password ? password : null);

    public ValueTask<string?> PromptAsync(ConnectionProfile connection, string prompt, bool echo, CancellationToken ct)
        => ValueTask.FromResult<string?>(null);

    public ValueTask<bool> ConfirmHostKeyAsync(ConnectionProfile connection, string algorithm, string fingerprint, CancellationToken ct)
        => ValueTask.FromResult(false);
}
