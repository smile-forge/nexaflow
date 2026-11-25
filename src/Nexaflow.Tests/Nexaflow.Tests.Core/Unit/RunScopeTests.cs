
using Nexaflow.Features.Common;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// What a run's tools may reach is what the tabs show, with names compared as the file system holding them compares them.
/// A WSL distro's folder is granted by the Windows folder that shows it (<c>\\wsl.localhost\…</c>), yet its names differ by
/// case, as Linux's do: a grant on <c>proj</c> holds nothing in <c>PROJ</c>. On this disk case is still ignored.
/// </summary>
[TestClass]
[CoversNode("ai-context-paths")]
public sealed class RunScopeTests
{
    private const string Root = "::wsl-runscope-probe";

    private string _share = string.Empty;
    private LocationFileSystem _files = null!;

    private sealed class SameNames : ILocationNativePaths
    {
        public IReadOnlyList<string> GetNativeRoots(ConnectionProfile connection) => [];
        public string ToNativeName(string name) => name;
        public string FromNativeName(string nativeName) => nativeName;
    }

    private sealed class NoSession : ILocationSessionSource
    {
        public ValueTask<ILocationSession> GetSessionAsync(CancellationToken ct)
            => throw new InvalidOperationException("a grant is compared without the location");
    }

    [TestInitialize]
    public void Setup()
    {
        _share = TestTemp.CreateSubdirectory("nexa-runscope-share-").FullName;
        _files = new LocationFileSystem(VirtualFileSystem.Instance);
        _files.RegisterLocation(new LocationMount(new ConnectionProfile("wsl-runscope-probe", "wsl", "Probe", "Probe"),
                                                  LocationCapabilities.SameMachine | LocationCapabilities.CaseSensitive, new NoSession())
        {
            NativeRoots = [_share],
            NativePaths = new SameNames(),
        });
    }

    private PathScope ScopeOf(ContextGrant grant)
    {
        var page = Substitute.For<IPageViewModel>();
        page.GetContextGrant().Returns(grant);
        return RunScope.Of(page, _files);
    }

    [TestMethod]
    public void ADistrosFolderGrantedByItsWindowsFolder_HoldsNothingThatDiffersByCase()
    {
        var scope = ScopeOf(ContextGrant.OfFolder($@"{_share}\home\proj"));

        Assert.IsNotNull(scope.Resolve($@"{_share}\home\proj\notes.txt"));
        Assert.IsNull(scope.Resolve($@"{_share}\home\PROJ\notes.txt"), "PROJ is another folder in Linux");
        Assert.IsNull(scope.Resolve($@"{_share}\HOME\proj\notes.txt"));
    }

    [TestMethod]
    public void ADistrosFileGrantedByItsWindowsFolder_IsThatFileAlone()
    {
        var scope = ScopeOf(ContextGrant.OfFile($@"{_share}\home\notes.txt"));

        Assert.IsNotNull(scope.Resolve($@"{_share}\home\notes.txt"));
        Assert.IsNull(scope.Resolve($@"{_share}\home\Notes.txt"), "Notes.txt is another file in Linux");
    }

    [TestMethod]
    public void ANameWindowsWouldTrim_ReachesNoMoreThanWhatWasGranted()
    {
        var scope = ScopeOf(ContextGrant.OfFolder($@"{_share}\home\proj"));

        // Windows reads 'proj.' as 'proj', so it is resolved as the granted folder itself, and nothing else.
        Assert.AreEqual($@"{_share}\home\proj\a.txt", scope.Resolve($@"{_share}\home\proj.\a.txt"));
        Assert.IsNull(scope.Resolve($@"{_share}\home\pro.\a.txt"));
    }

    [TestMethod]
    public void ADistrosFolderGrantedByItsOwnPath_HoldsNothingThatDiffersByCase()
    {
        var scope = ScopeOf(ContextGrant.OfFolder($@"{Root}\home\proj"));

        Assert.IsNotNull(scope.Resolve($@"{Root}\home\proj\notes.txt"));
        Assert.IsNull(scope.Resolve($@"{Root}\home\PROJ\notes.txt"));
    }

    [TestMethod]
    public void AFolderOnThisDisk_StillIgnoresCase()
    {
        var folder = TestTemp.CreateSubdirectory("nexa-runscope-disk-").FullName;
        var scope  = ScopeOf(ContextGrant.OfFolder(folder));

        Assert.IsNotNull(scope.Resolve(folder.ToUpperInvariant() + @"\notes.txt"));
    }
}
