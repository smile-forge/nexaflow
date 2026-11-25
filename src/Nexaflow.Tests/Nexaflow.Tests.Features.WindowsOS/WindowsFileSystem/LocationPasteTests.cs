using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.WindowsFileSystem.Operations;
using Nexaflow.Features.WindowsFileSystem.Services;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Location.Memory;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// A location's file on the Windows clipboard, or dragged in from Explorer, is named by the Windows folder that shows it.
/// Pasted or dropped here it is still moved and copied through the location, which knows its exact names, links and
/// permissions. The Windows folder here is a decoy: anything done there is a change made behind the location's back.
/// </summary>
[TestClass]
[CoversNode("winfs-drag-drop")]
public sealed class LocationPasteTests
{
    private const string Root = "::paste-probe";

    private LocationSessionPool _pool = null!;
    private LocationFileSystem _vfs = null!;
    private string _decoy = string.Empty;

    private sealed class NoSecrets : ISecretStore
    {
        public string? Read(string target) => null;
        public void Write(string target, string? userName, string secret) { }
        public bool Delete(string target) => false;
    }

    private sealed class SameNames : ILocationNativePaths
    {
        public IReadOnlyList<string> GetNativeRoots(ConnectionProfile connection) => [];
        public string ToNativeName(string name) => name;
        public string FromNativeName(string nativeName) => nativeName;
    }

    [TestInitialize]
    public async Task Setup()
    {
        _decoy = TestTemp.CreateSubdirectory("nexa-paste-decoy-").FullName;
        Directory.CreateDirectory(Path.Combine(_decoy, "dest"));
        File.WriteAllText(Path.Combine(_decoy, "file.txt"), "decoy");

        var host     = new MemoryLocationHost("paste-" + Guid.NewGuid().ToString("N")[..8], TimeSpan.Zero);
        var registry = new LocationProviderRegistry();
        registry.Register(new MemoryLocationProvider(host));
        _pool = new LocationSessionPool(registry, new StoredCredentialSource(new NoSecrets()));
        var set = _pool.Acquire([new ConnectionProfile("paste-probe", "memory", "Probe", host.Name)]);
        _vfs = new LocationFileSystem(new VirtualFileSystem(TestTemp.CreateSubdirectory("nexa-vfstemp-").FullName));
        _vfs.RegisterLocation(set.Mounts[0] with { NativeRoots = [_decoy], NativePaths = new SameNames() });
        await _vfs.WriteAllTextAsync($@"{Root}\file.txt", "real");
        await _vfs.CreateDirectoryAsync($@"{Root}\dest");
    }

    [TestCleanup]
    public async Task Release() => await _pool.DisposeAsync();

    private static IShellServices Shell()
    {
        var shell = Substitute.For<IShellServices>().Runs();
        shell.DiscoverImplementations<IFileAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFolderAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFileCreateAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFolderViewlet>().Returns(Array.Empty<Type>());
        return shell;
    }

    [TestMethod]
    [DataRow(Root + @"\dest", DisplayName = "into the location's folder")]
    [DataRow("decoy", DisplayName = "into the Windows folder that shows it")]
    public async Task ACutPastedByItsWindowsFolder_MovesThroughTheLocation(string destination)
    {
        if (destination == "decoy") destination = Path.Combine(_decoy, "dest");
        var shell = Shell();
        var queue = new FileOperationQueue(shell, _vfs, new FileCompanions(shell));

        var op = queue.EnqueuePaste([Path.Combine(_decoy, "file.txt")], destination, isCut: true);
        await op!.Completion;

        Assert.AreEqual("real", await _vfs.ReadAllTextAsync($@"{Root}\dest\file.txt"));
        Assert.IsFalse(await _vfs.ExistsAsync($@"{Root}\file.txt"), "the location's own file moved");
        Assert.AreEqual("decoy", File.ReadAllText(Path.Combine(_decoy, "file.txt")), "nothing moved through the Windows folder");
        Assert.IsFalse(File.Exists(Path.Combine(_decoy, "dest", "file.txt")));
    }
}
