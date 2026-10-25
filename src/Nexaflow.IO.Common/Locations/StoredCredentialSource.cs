namespace Nexaflow.IO.Common.Locations;

/// <summary>
/// Signs a connection in from what the person stored for it — its password or key passphrase, in
/// <paramref name="store"/> under the connection's target — and asks the person, through <paramref name="prompts"/>, for
/// anything else. With no prompt nobody is there to ask, so a sign-in that has to ask fails with
/// <see cref="LocationError.AuthFailed"/> saying so, rather than as though the person had refused.
/// </summary>
public sealed class StoredCredentialSource(ISecretStore store, ICredentialPrompt? prompts = null) : ICredentialSource
{
    public ValueTask<string?> GetSecretAsync(ConnectionProfile connection, SecretKind kind, CancellationToken ct)
        => ValueTask.FromResult(store.Read(connection.CredentialTargetFor(kind)));

    public ValueTask<string?> PromptAsync(ConnectionProfile connection, string prompt, bool echo, CancellationToken ct)
        => prompts is { } asking
            ? asking.PromptAsync(connection, prompt, echo, ct)
            : ValueTask.FromException<string?>(NobodyToAsk(connection));

    public ValueTask<bool> ConfirmHostKeyAsync(ConnectionProfile connection, string algorithm, string fingerprint, CancellationToken ct)
        => prompts is { } asking
            ? asking.ConfirmHostKeyAsync(connection, algorithm, fingerprint, ct)
            : ValueTask.FromException<bool>(NobodyToAsk(connection));

    private static LocationException NobodyToAsk(ConnectionProfile connection)
        => new(LocationError.AuthFailed, null,
               $"Signing in to '{connection.Label}' needs an answer from the person, and nobody is here to ask.");
}
