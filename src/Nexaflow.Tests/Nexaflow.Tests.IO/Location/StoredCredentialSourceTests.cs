using Nexaflow.IO.Common.Locations;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Location;

/// <summary>
/// Signing in from what the person stored, by the connection's target for each kind of secret, and asking the person only
/// through a prompt — with none, a sign-in that has to ask fails saying nobody is there, rather than as a refusal.
/// </summary>
[TestClass]
[CoversNode("io-locations-connections")]
public sealed class StoredCredentialSourceTests
{
    private static readonly ConnectionProfile Nas = new("sftp-nas", "sftp", "NAS", "nas") { User = "adam" };

    private sealed class Secrets : ISecretStore
    {
        public readonly Dictionary<string, string> Stored = [];
        public string? Read(string target) => Stored.GetValueOrDefault(target);
        public void Write(string target, string? userName, string secret) => Stored[target] = secret;
        public bool Delete(string target) => Stored.Remove(target);
    }

    private sealed class Asking : ICredentialPrompt
    {
        public ValueTask<string?> PromptAsync(ConnectionProfile connection, string prompt, bool echo, CancellationToken ct)
            => ValueTask.FromResult<string?>("123456");

        public ValueTask<bool> ConfirmHostKeyAsync(ConnectionProfile connection, string algorithm, string fingerprint, CancellationToken ct)
            => ValueTask.FromResult(true);
    }

    [TestMethod]
    public async Task EachKindOfSecretIsReadFromItsOwnTarget()
    {
        var secrets = new Secrets();
        secrets.Write(Nas.CredentialTargetFor(SecretKind.Password), "adam", "hunter2");
        secrets.Write(Nas.CredentialTargetFor(SecretKind.KeyPassphrase), "adam", "open sesame");
        var source = new StoredCredentialSource(secrets);

        Assert.AreEqual("hunter2", await source.GetSecretAsync(Nas, SecretKind.Password, default));
        Assert.AreEqual("open sesame", await source.GetSecretAsync(Nas, SecretKind.KeyPassphrase, default));
        Assert.IsNull(await source.GetSecretAsync(Nas with { Id = "sftp-other" }, SecretKind.Password, default));
    }

    [TestMethod]
    public async Task AQuestionGoesToThePrompt()
    {
        var source = new StoredCredentialSource(new Secrets(), new Asking());

        Assert.AreEqual("123456", await source.PromptAsync(Nas, "Verification code:", echo: true, default));
        Assert.IsTrue(await source.ConfirmHostKeyAsync(Nas, "ssh-ed25519", "SHA256:abc", default));
    }

    [TestMethod]
    public async Task WithNoPromptASignInThatHasToAskFailsSayingNobodyIsThere()
    {
        var source = new StoredCredentialSource(new Secrets());

        var asked = await Assert.ThrowsExactlyAsync<LocationException>(async () => await source.PromptAsync(Nas, "Password:", echo: false, default));
        Assert.AreEqual(LocationError.AuthFailed, asked.Error);
        StringAssert.Contains(asked.Message, "nobody is here to ask");

        var trusted = await Assert.ThrowsExactlyAsync<LocationException>(async () => await source.ConfirmHostKeyAsync(Nas, "ssh-ed25519", "SHA256:abc", default));
        Assert.AreEqual(LocationError.AuthFailed, trusted.Error);
    }
}
