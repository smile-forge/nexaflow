using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.Operations;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Location.Memory;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// The Recycle Bin holds nothing of a location's, though Windows can reach the location as a folder: shell32 deletes
/// such a file for good and reports success. So it is never offered — by the location's path or by that folder's.
/// </summary>
[TestClass]
[CoversNode("winfs-act-delete")]
public sealed class LocationDeleteTests
{
    private const string Root = "::wsl-delete-probe";
    private const string Share = @"\\wsl.localhost\DeleteProbe";

    private sealed class SameNames : ILocationNativePaths
    {
        public IReadOnlyList<string> GetNativeRoots(ConnectionProfile connection) => [];
        public string ToNativeName(string name) => name;
        public string FromNativeName(string nativeName) => nativeName;
    }

    private sealed class NoSession : ILocationSessionSource
    {
        public ValueTask<ILocationSession> GetSessionAsync(CancellationToken ct)
            => throw new InvalidOperationException("whether a path can be recycled is answered without the location");
    }

    [TestMethod]
    [DataRow(Root + @"\home\notes.txt", DisplayName = "by the location's path")]
    [DataRow(Share + @"\home\notes.txt", DisplayName = "by the Windows folder that shows it")]
    public void AFileOnALocationIsNeverRecyclable(string path)
    {
        var vfs = new LocationFileSystem(VirtualFileSystem.Instance);
        vfs.RegisterLocation(new LocationMount(new ConnectionProfile("wsl-delete-probe", "wsl", "Probe", "DeleteProbe"),
                                               LocationCapabilities.SameMachine, new NoSession())
        {
            NativeRoots = [Share],
            NativePaths = new SameNames(),
        });

        Assert.IsFalse(FileOperationQueue.For(Substitute.For<IShellServices>(), vfs).IsRecyclable(path));
    }
}
