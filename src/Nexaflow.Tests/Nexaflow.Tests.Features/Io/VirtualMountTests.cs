using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Nexaflow.Features.Compressed.Handlers;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Io;

/// <summary>
/// Pass-through mounts: a <c>::{id}</c> root mapped onto a real directory. The mapping happens before
/// every other VFS rule, so a mounted path behaves exactly like the directory behind it — including
/// descending into an archive that lives inside it — while never revealing where that directory is.
/// <para>
/// The load-bearing distinction from an archive entry is <see cref="VirtualBacking"/>: a mount is
/// <c>PassThrough</c> (the whole subtree is on disk, so a resolved path is as good as a real one),
/// an archive entry is <c>Materialized</c> (only that one file can be produced, to a temp copy).
/// </para>
/// </summary>
[TestClass]
[CoversNode("io-vfs-mounts")]
public class VirtualMountTests
{
    private VirtualFileSystem _vfs = null!;
    private string _dir  = string.Empty;   // the real directory behind the mount
    private string _away = string.Empty;   // a real directory outside every mount

    private const string MountId = "docs";
    private const string Root    = "::docs";

    [TestInitialize]
    public void Setup()
    {
        _vfs = new VirtualFileSystem();               // isolated — never the process-wide Instance
        _vfs.RegisterHandler(new ZipArchiveHandler());

        _dir  = NewTempDir("nexa-mount-");
        _away = NewTempDir("nexa-away-");

        Directory.CreateDirectory(Path.Combine(_dir, "sub"));
        File.WriteAllText(Path.Combine(_dir, "top.txt"), "top");
        File.WriteAllText(Path.Combine(_dir, "sub", "deep.txt"), "deep");

        _vfs.RegisterMount(new VirtualMount(MountId, "My Documents", _dir));
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_dir,  recursive: true); } catch { }
        try { Directory.Delete(_away, recursive: true); } catch { }
    }

    // ── Resolution ───────────────────────────────────────────────────────────

    [TestMethod]
    public void TheMountRootBrowsesAsADirectory()
    {
        Assert.IsTrue(_vfs.Exists(Root));
        Assert.IsTrue(_vfs.IsDirectory(Root));
    }

    [TestMethod]
    public void APathBeneathTheMountReadsTheRealFile()
    {
        Assert.AreEqual("top",  _vfs.ReadAllText($@"{Root}\top.txt"));
        Assert.AreEqual("deep", _vfs.ReadAllText($@"{Root}\sub\deep.txt"));
    }

    [TestMethod]
    public void ForwardSlashesResolveTheSameAsBackslashes()
    {
        Assert.AreEqual("deep", _vfs.ReadAllText($"{Root}/sub/deep.txt"));
    }

    [TestMethod]
    public void EnumeratingTheMountListsTheRealDirectorysChildren()
    {
        var names = _vfs.EnumerateEntries(Root).Select(e => e.Name).OrderBy(n => n).ToArray();
        CollectionAssert.AreEqual(new[] { "sub", "top.txt" }, names);
    }

    [TestMethod]
    public void AnUnregisteredMountResolvesToNothingRatherThanGuessing()
    {
        Assert.IsFalse(_vfs.Exists(@"::nosuch\top.txt"));
        Assert.IsNull(_vfs.TryResolveReal(@"::nosuch\top.txt"));
    }

    [TestMethod]
    public void TheInnermostMountWinsWhenTwoNest()
    {
        var inner = Path.Combine(_dir, "sub");
        _vfs.RegisterMount(new VirtualMount("inner", "Inner", inner));

        // Both mounts cover this file; the longer root must claim it.
        Assert.AreEqual(@"::inner\deep.txt", _vfs.TryToVirtual(Path.Combine(inner, "deep.txt")));
    }

    // ── Materialisation: a mount must never produce a temp copy ──────────────

    [TestMethod]
    public void MaterializeReturnsTheMappedPathAndWritesNoTempCopy()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "nexaflow-vfs");
        var before   = Directory.Exists(tempRoot) ? Directory.GetFiles(tempRoot).Length : 0;

        var real = _vfs.MaterializeFile($@"{Root}\sub\deep.txt");

        Assert.AreEqual(Path.Combine(_dir, "sub", "deep.txt"), real);
        var after = Directory.Exists(tempRoot) ? Directory.GetFiles(tempRoot).Length : 0;
        Assert.AreEqual(before, after, "a pass-through mount must not extract anything to the temp cache");
    }

    [TestMethod]
    public void WritingThroughAMountUpdatesTheRealFileInPlace()
    {
        // Proves the write took the atomic-real branch rather than trying to rebuild a "container".
        _vfs.WriteAllText($@"{Root}\top.txt", "CHANGED", new UTF8Encoding(false));

        Assert.AreEqual("CHANGED", File.ReadAllText(Path.Combine(_dir, "top.txt")));
    }

    // ── Backing classification ───────────────────────────────────────────────

    [TestMethod]
    public void APlainWindowsPathIsBackedReal()
    {
        Assert.AreEqual(VirtualBacking.Real, _vfs.GetBacking(Path.Combine(_away, "anything.txt")));
    }

    [TestMethod]
    public void AMountedPathIsBackedPassThrough()
    {
        Assert.AreEqual(VirtualBacking.PassThrough, _vfs.GetBacking(Root));
        Assert.AreEqual(VirtualBacking.PassThrough, _vfs.GetBacking($@"{Root}\sub\deep.txt"));
    }

    [TestMethod]
    public void AnArchiveEntryIsBackedMaterializedEvenInsideAMount()
    {
        WriteZipFile(Path.Combine(_dir, "bundle.zip"), ("x.txt", Utf8("zipped")));

        Assert.AreEqual(VirtualBacking.Materialized, _vfs.GetBacking($@"{Root}\bundle.zip\x.txt"));
    }

    [TestMethod]
    public void ResolvingRealYieldsAPathForAMountButNotForAnArchiveEntry()
    {
        WriteZipFile(Path.Combine(_dir, "bundle.zip"), ("x.txt", Utf8("zipped")));

        Assert.AreEqual(Path.Combine(_dir, "sub", "deep.txt"), _vfs.TryResolveReal($@"{Root}\sub\deep.txt"));
        Assert.IsNull(_vfs.TryResolveReal($@"{Root}\bundle.zip\x.txt"));
    }

    [TestMethod]
    public void AnArchiveInsideAMountStillSplitsAtTheRealArchiveFile()
    {
        var zip = Path.Combine(_dir, "bundle.zip");
        WriteZipFile(zip, ("x.txt", Utf8("zipped")));

        var (container, inner) = _vfs.SplitOutermostContainer($@"{Root}\bundle.zip\x.txt");

        Assert.AreEqual(zip, container);
        Assert.AreEqual("x.txt", inner);
        Assert.AreEqual("zipped", _vfs.ReadAllText($@"{Root}\bundle.zip\x.txt"));
    }

    // ── Real ⇄ virtual mapping ───────────────────────────────────────────────

    [TestMethod]
    public void ARealPathUnderTheMountMapsBackToItsVirtualForm()
    {
        Assert.AreEqual(Root,                 _vfs.TryToVirtual(_dir));
        Assert.AreEqual($@"{Root}\top.txt",   _vfs.TryToVirtual(Path.Combine(_dir, "top.txt")));
        Assert.AreEqual($@"{Root}\sub\deep.txt", _vfs.TryToVirtual(Path.Combine(_dir, "sub", "deep.txt")));
    }

    [TestMethod]
    public void ARealPathOutsideEveryMountHasNoVirtualForm()
    {
        Assert.IsNull(_vfs.TryToVirtual(Path.Combine(_away, "top.txt")));
    }

    [TestMethod]
    public void ASiblingDirectorySharingTheMountsNamePrefixIsNotMistakenForIt()
    {
        // "…\nexa-mount-abc" must not swallow "…\nexa-mount-abc-other".
        var decoy = _dir + "-other";
        Directory.CreateDirectory(decoy);
        try { Assert.IsNull(_vfs.TryToVirtual(Path.Combine(decoy, "top.txt"))); }
        finally { try { Directory.Delete(decoy, recursive: true); } catch { } }
    }

    // ── Navigation helpers ───────────────────────────────────────────────────

    [TestMethod]
    public void GoingUpWalksTheMountChainAndStopsAtItsRoot()
    {
        Assert.AreEqual($@"{Root}\sub", _vfs.GetParentPath($@"{Root}\sub\deep.txt"));
        Assert.AreEqual(Root,           _vfs.GetParentPath($@"{Root}\sub"));
        Assert.IsNull(_vfs.GetParentPath(Root), "above a mount root is This PC, not a real folder");
    }

    [TestMethod]
    public void GoingUpFromARealPathStillWalksTheRealChain()
    {
        Assert.AreEqual(_dir, _vfs.GetParentPath(Path.Combine(_dir, "top.txt")));
    }

    [TestMethod]
    public void TheMountRootDisplaysItsFriendlyLabelNotItsId()
    {
        Assert.AreEqual("My Documents", _vfs.GetDisplayName(Root));
        Assert.AreEqual("deep.txt",     _vfs.GetDisplayName($@"{Root}\sub\deep.txt"));
    }

    [TestMethod]
    public void BreadcrumbsLeadWithTheLabelAndNeverNameTheRealDirectory()
    {
        var crumbs = _vfs.GetBreadcrumbs($@"{Root}\sub\deep.txt");

        CollectionAssert.AreEqual(
            new[] { "My Documents", "sub", "deep.txt" },
            crumbs.Select(c => c.Label).ToArray());
        CollectionAssert.AreEqual(
            new[] { Root, $@"{Root}\sub", $@"{Root}\sub\deep.txt" },
            crumbs.Select(c => c.Path).ToArray());
        Assert.IsFalse(crumbs.Any(c => c.Path.Contains(_dir, StringComparison.OrdinalIgnoreCase)),
            "the real location must never appear in a breadcrumb");
    }

    [TestMethod]
    public void BreadcrumbsForARealPathStartAtTheDriveRoot()
    {
        var crumbs = _vfs.GetBreadcrumbs(Path.Combine(_dir, "top.txt"));

        var root = Path.GetPathRoot(_dir)!;
        Assert.AreEqual(root.TrimEnd(Path.DirectorySeparatorChar), crumbs[0].Label);
        Assert.AreEqual(root, crumbs[0].Path);
        Assert.AreEqual("top.txt", crumbs[^1].Label);
        Assert.AreEqual(Path.Combine(_dir, "top.txt"), crumbs[^1].Path);
    }

    // ── Registration lifecycle ───────────────────────────────────────────────

    [TestMethod]
    public void ReRegisteringAnIdenticalMountChangesNothingAndStaysSilent()
    {
        int raised = 0;
        _vfs.MountsChanged += () => raised++;

        _vfs.RegisterMount(new VirtualMount(MountId, "My Documents", _dir));

        Assert.AreEqual(0, raised, "an unchanged re-registration must not churn open browsers");
        Assert.AreEqual(1, _vfs.Mounts.Count);
    }

    [TestMethod]
    public void ReRegisteringTheSameIdWithNewDetailsReplacesItAndAnnounces()
    {
        int raised = 0;
        _vfs.MountsChanged += () => raised++;

        _vfs.RegisterMount(new VirtualMount(MountId, "Renamed", _dir));

        Assert.AreEqual(1, raised);
        Assert.AreEqual(1, _vfs.Mounts.Count);
        Assert.AreEqual("Renamed", _vfs.GetDisplayName(Root));
    }

    [TestMethod]
    public void UnregisteringRemovesTheMountAndAnnouncesIt()
    {
        int raised = 0;
        _vfs.MountsChanged += () => raised++;

        _vfs.UnregisterMount(MountId);

        Assert.AreEqual(1, raised);
        Assert.AreEqual(0, _vfs.Mounts.Count);
        Assert.IsFalse(_vfs.Exists(Root));
    }

    [TestMethod]
    public void UnregisteringSomethingAbsentIsANoOp()
    {
        int raised = 0;
        _vfs.MountsChanged += () => raised++;

        _vfs.UnregisterMount("nosuch");

        Assert.AreEqual(0, raised);
        Assert.AreEqual(1, _vfs.Mounts.Count);
    }

    [TestMethod]
    public void AnIdThatWouldNotSurviveAsOnePathSegmentIsRejected()
    {
        foreach (var bad in new[] { "", "  ", @"a\b", "a/b", "a:b" })
            Assert.ThrowsExactly<ArgumentException>(
                () => _vfs.RegisterMount(new VirtualMount(bad, "L", _dir)),
                $"id '{bad}' must be rejected — it would break the ::id path grammar");
    }

    [TestMethod]
    public void AMountWithNoRealRootIsRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => _vfs.RegisterMount(new VirtualMount("x", "L", "")));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string NewTempDir(string prefix)
    {
        var d = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    private static byte[] Utf8(string s) => new UTF8Encoding(false).GetBytes(s);

    private static void WriteZipFile(string path, params (string Name, byte[] Body)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, body) in entries)
            {
                using var s = zip.CreateEntry(name).Open();
                s.Write(body, 0, body.Length);
            }
        File.WriteAllBytes(path, ms.ToArray());
    }
}
