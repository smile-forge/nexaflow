using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Common.Terminal;
using Nexaflow.IO.Location.Wsl;
using Nexaflow.IO.Location.Memory;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Location.Wsl;

/// <summary>
/// A distro shell's variables as <c>env -0</c> prints them, and the startup file a variable is kept in for new shells: the
/// one the user's login shell reads, found in their home.
/// </summary>
[TestClass]
[CoversNode("wslshell-env-panel")]
public sealed class WslShellEnvironmentTests
{
    [TestMethod]
    public void WhatEnvPrints_IsReadNameByName_AValueHoldingAnythingButANul()
    {
        var read = WslShellEnvironment.Parse("A=1\0B=x=y\0C=two\nlines\0\0");

        Assert.AreEqual("1", read["A"]);
        Assert.AreEqual("x=y", read["B"], "only the first '=' ends the name");
        Assert.AreEqual("two\nlines", read["C"]);
        Assert.AreEqual(3, read.Count);
    }

    [TestMethod]
    public void WhatAProfilePrintsBeforeTheMark_IsNotAVariable()
    {
        var read = WslShellEnvironment.AfterMark("Welcome=to Ubuntu\n\n--nexaflow-environment--\0HOME=/home/adam\0");

        CollectionAssert.AreEqual(new[] { "HOME" }, read.Keys.ToArray());
        Assert.AreEqual(0, WslShellEnvironment.AfterMark("no mark at all").Count);
    }

    private sealed class NoSecrets : ISecretStore
    {
        public string? Read(string target) => null;
        public void Write(string target, string? userName, string secret) { }
        public bool Delete(string target) => false;
    }

    /// <summary>A home folder holding <paramref name="present"/>, on a location standing in for the distro.</summary>
    private static async Task<ILocationSession> HomeAsync(params string[] present)
    {
        var host     = "startup-" + Guid.NewGuid().ToString("N");
        var provider = new MemoryLocationProvider(new MemoryLocationHost(host, TimeSpan.Zero));
        var files    = await provider.ConnectAsync(new ConnectionProfile("startup", "memory", "Probe", host),
                                                   new StoredCredentialSource(new NoSecrets()), CancellationToken.None);
        await files.CreateDirectoryAsync("/home", CancellationToken.None);
        await files.CreateDirectoryAsync("/home/adam", CancellationToken.None);
        foreach (var path in present)
        {
            await using var write = await files.BeginWriteAsync(path, WriteMode.Replace, null, null, CancellationToken.None);
            await write.CommitAsync(CancellationToken.None);
        }
        return files;
    }

    private static async Task<string?> StartupFile(string shell, params string[] present)
        => await new WslShellEnvironment("Probe", shell, "/home/adam", await HomeAsync(present), null).FindStartupFileAsync(CancellationToken.None);

    [TestMethod]
    public async Task AVariableForNewShells_GoesInTheFileTheLoginShellReads()
    {
        Assert.AreEqual("/home/adam/.bash_profile", await StartupFile("/bin/bash", "/home/adam/.bash_profile", "/home/adam/.profile"));
        Assert.AreEqual("/home/adam/.profile", await StartupFile("/bin/bash", "/home/adam/.profile"));
        Assert.AreEqual("/home/adam/.profile", await StartupFile("/bin/bash"), "none yet: the one sh reads too is made");
        Assert.AreEqual("/home/adam/.zshenv", await StartupFile("/usr/bin/zsh"));
        Assert.AreEqual("/home/adam/.profile", await StartupFile("/bin/dash"));
        Assert.IsNull(await StartupFile("/usr/bin/fish"), "fish keeps its variables otherwise");
    }

    [TestMethod]
    public async Task AShellWithoutTheIntegration_OffersOnlyANewShellsVariables()
    {
        var without = new WslShellEnvironment("Probe", "/usr/bin/fish", "/home/adam", await HomeAsync(), null);
        var with    = new WslShellEnvironment("Probe", "/bin/bash", "/home/adam", await HomeAsync(), "/home/adam/.cache/nexaflow/t.env");

        CollectionAssert.AreEqual(new[] { ShellEnvironmentView.Startup }, without.Views.ToArray());
        CollectionAssert.AreEqual(new[] { ShellEnvironmentView.Live, ShellEnvironmentView.Startup }, with.Views.ToArray());
    }
}
