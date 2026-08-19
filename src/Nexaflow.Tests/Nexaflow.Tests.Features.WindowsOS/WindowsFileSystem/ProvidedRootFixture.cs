using System;
using System.Collections.Generic;
using System.IO;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ThisPc;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.IO.Common;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// Builds a file browser whose "This PC" carries one provider-supplied location backed by a real temp
/// folder. Mounts are process-wide by design, so each fixture uses a unique id and withdraws it on
/// dispose — otherwise parallel tests would see each other's locations.
/// </summary>
internal sealed class ProvidedRootFixture : IDisposable
{
    public string RealRoot    { get; }
    public string MountId     { get; }
    public string VirtualRoot => VirtualMount.RootFor(MountId);
    public string Label       => "Test Cloud";

    private readonly IShellServices _shell;

    /// <summary>Set by the fake provider; lets a test make the location vanish mid-flight.</summary>
    public static readonly Dictionary<string, (string Path, string Label)> Registered = [];

    public ProvidedRootFixture(bool withProvider = true)
    {
        MountId  = "testcloud" + Guid.NewGuid().ToString("N")[..8];
        RealRoot = Path.Combine(Path.GetTempPath(), "nexa-provided-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(RealRoot, "Documents", "Nested"));
        File.WriteAllText(Path.Combine(RealRoot, "Documents", "notes.txt"), "hello");
        File.WriteAllText(Path.Combine(RealRoot, "top.txt"), "top");

        lock (Registered) Registered[MountId] = (RealRoot, Label);

        _shell = Substitute.For<IShellServices>();
        _shell.DiscoverImplementations<IFileAction>().Returns(Array.Empty<Type>());
        _shell.DiscoverImplementations<IFolderAction>().Returns(Array.Empty<Type>());
        _shell.DiscoverImplementations<IFileCreateAction>().Returns(Array.Empty<Type>());
        _shell.DiscoverImplementations<IFolderViewlet>().Returns(Array.Empty<Type>());
        _shell.DiscoverImplementations<IThisPcItemProvider>()
              .Returns(withProvider ? [typeof(FakeCloudProvider)] : Array.Empty<Type>());

        // RunOnUiAsync is the marshal the VM uses for provider change signals; run inline in tests.
        _shell.RunOnUiAsync(Arg.Any<Action>())
              .Returns(ci => { ci.Arg<Action>()(); return Task.CompletedTask; });
    }

    public FileSystemViewModel ThisPc()
        => FileSystemViewModel.CreateThisPc(_shell, Substitute.For<IAIService>(), new Dictionary<Type, IFeatureConfig>());

    /// <summary>A browser opened directly at a folder (the "rooted" mode), for the real-path journey.</summary>
    public FileSystemViewModel At(string folder)
        => new(folder, _shell, Substitute.For<IAIService>(), new Dictionary<Type, IFeatureConfig>());

    public void Dispose()
    {
        lock (Registered) Registered.Remove(MountId);
        try { VirtualFileSystem.Instance.UnregisterMount(MountId); } catch { }
        try { Directory.Delete(RealRoot, recursive: true); } catch { }
    }

    /// <summary>Offers whatever the live fixtures have registered. One type serves every fixture because
    /// the registry discovers types, not instances.</summary>
    public sealed class FakeCloudProvider : IThisPcItemProvider
    {
        public string ProviderId => "testcloud";
        public int    SortOrder  => 500;
        public event Action? Changed;

        public void Raise() => Changed?.Invoke();

        public IReadOnlyList<ThisPcItem> GetItems()
        {
            lock (Registered)
                return [.. Registered.Select(kv => new ThisPcItem
                {
                    Id         = kv.Key,
                    Label      = kv.Value.Label,
                    TargetPath = kv.Value.Path,
                    TypeLabel  = "Test Cloud",
                    Icon       = ThisPcItemIcon.Cloud,
                })];
        }
    }
}
