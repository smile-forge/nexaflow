namespace Nexaflow.IO.Common.Locations;

/// <summary>Which stored secret a provider asks for.</summary>
public enum SecretKind
{
    Password,
    KeyPassphrase,
}

/// <summary>
/// Where a provider gets what it needs to sign in, without ever holding a secret longer than the connect that asked
/// for it. Stored secrets come from Windows Credential Manager under <see cref="ConnectionProfile.CredentialTarget"/>;
/// anything else is asked of the person. None of it reaches the assistant.
/// </summary>
public interface ICredentialSource
{
    /// <summary>The stored secret of that kind for the connection, or null when none is stored.</summary>
    ValueTask<string?> GetSecretAsync(ConnectionProfile connection, SecretKind kind, CancellationToken ct);

    /// <summary>Asks the person one question the server put — a password, a 2FA code. Null when they cancel.</summary>
    /// <param name="echo">False when the answer is a secret and must not show as it is typed.</param>
    ValueTask<string?> PromptAsync(ConnectionProfile connection, string prompt, bool echo, CancellationToken ct);

    /// <summary>
    /// Asks the person, once, whether to trust a host key never seen before, showing its fingerprint. A key that
    /// <b>changed</b> never reaches here: the provider refuses it with <see cref="LocationError.HostKeyChanged"/>,
    /// so no dialog can offer to accept it.
    /// </summary>
    ValueTask<bool> ConfirmHostKeyAsync(ConnectionProfile connection, string algorithm, string fingerprint, CancellationToken ct);
}
