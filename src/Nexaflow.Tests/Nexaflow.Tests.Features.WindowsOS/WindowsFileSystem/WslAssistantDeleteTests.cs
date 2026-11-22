using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.WindowsFileSystem.ClientTools;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Location.Wsl;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// The assistant deletes in a real distro as the person does there: a file and a folder, each approved as for good, gone
/// from Linux, and a name that differs only by case left alone. The files are the test's own, under a folder it makes in
/// the distro's <c>/tmp</c> and removes.
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("Wsl")]
[CoversNode("winfs-act-delete")]
public sealed class WslAssistantDeleteTests
{
    private sealed class NoSecrets : ISecretStore
    {
        public string? Read(string target) => null;
        public void Write(string target, string? userName, string secret) { }
        public bool Delete(string target) => false;
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task TheAssistantDeletesAFileAndAFolderInADistro_ForGood_AsApproved()
    {
        var provider = new WslLocationProvider();
        var distros  = await provider.DiscoverAsync(CancellationToken.None);
        if (distros.Count == 0) Assert.Inconclusive("WSL has no distro installed on this machine.");

        var registry = new LocationProviderRegistry();
        registry.Register(provider);
        await using var pool = new LocationSessionPool(registry, new StoredCredentialSource(new NoSecrets()));
        var set   = pool.Acquire([distros[0]]);
        var files = new LocationFileSystem(VirtualFileSystem.Instance);
        files.RegisterLocation(set.Mounts[0]);

        var folder = $@"{set.Mounts[0].Root}\tmp\nexaflow-assistant-delete-{Guid.NewGuid():N}";
        try
        {
            await files.WriteAllTextAsync($@"{folder}\notes.txt", "delete me");
            await files.WriteAllTextAsync($@"{folder}\Notes.txt", "keep me");
            await files.WriteAllTextAsync($@"{folder}\old\inside.txt", "delete me too");

            var shell = Substitute.For<IShellServices>().Runs();
            shell.DiscoverImplementations<IFileAction>().Returns(Array.Empty<Type>());
            shell.DiscoverImplementations<IFolderAction>().Returns(Array.Empty<Type>());
            shell.DiscoverImplementations<IFileCreateAction>().Returns(Array.Empty<Type>());
            shell.DiscoverImplementations<IFolderViewlet>().Returns(Array.Empty<Type>());
            var vm = new FileSystemViewModel(folder, shell, Substitute.For<IAIService>(), new Dictionary<Type, IFeatureConfig>(), files);
            await vm.Browsing;
            await vm.EntriesLoaded;
            var tool = new DeleteTool(vm);

            foreach (var name in new[] { "notes.txt", "old" })
            {
                var asked = new JsonObject { ["name"] = name };
                StringAssert.EndsWith(tool.Announce(asked)!.Target, $"{name} for good");
                var result = await tool.InvokeAsync(asked, CancellationToken.None);
                Assert.IsTrue(result.Success, result.ToString());
            }

            Assert.IsFalse(await files.ExistsAsync($@"{folder}\notes.txt"));
            Assert.IsFalse(await files.ExistsAsync($@"{folder}\old"));
            Assert.AreEqual("keep me", await files.ReadAllTextAsync($@"{folder}\Notes.txt"));
        }
        finally
        {
            await files.DeleteAsync(folder, recursive: true);
            pool.Release(set);
        }
    }
}
