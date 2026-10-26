namespace Nexaflow.IO.Common.Locations;

/// <summary>
/// Where connections' secrets are kept, by target name (<see cref="ConnectionProfile.CredentialTargetFor"/>) — Windows
/// Credential Manager in the app (<see cref="CredentialManagerStore"/>). Never a file the app writes, never a config,
/// never anything the assistant can read.
/// </summary>
public interface ISecretStore
{
    /// <summary>The secret stored under <paramref name="target"/>, or null when none is.</summary>
    string? Read(string target);

    /// <summary>Stores <paramref name="secret"/> under <paramref name="target"/>, replacing what was there.</summary>
    /// <param name="userName">The account the secret signs in as, shown beside it in Credential Manager; null for none.</param>
    void Write(string target, string? userName, string secret);

    /// <summary>Forgets the secret under <paramref name="target"/>. False when none was stored.</summary>
    bool Delete(string target);
}
