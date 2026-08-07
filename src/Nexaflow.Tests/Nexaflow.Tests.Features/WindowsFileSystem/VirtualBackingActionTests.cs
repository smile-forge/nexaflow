using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Nexaflow.Features.Common;
using Nexaflow.Features.Compressed.Handlers;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Features.WindowsFileSystem.Services;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// Which actions may run where, and what path they receive.
/// <para>
/// An action that hands the file to another program can need the file's NEIGHBOURS — a dependency, a
/// sidecar, an installer's payload. Materialising one archive entry gives that program a lone file in a
/// temp folder, so the honest answer is to withhold the action rather than offer one that fails oddly.
/// Under a mount the whole folder really is on disk, so the same action is fine once the path resolves.
/// </para>
/// <para>
/// These cover the two halves the rule is built from: the declaration on each shipped action, and the
/// backing classification <c>FileActionManager</c> filters on. The join between them runs through
/// <c>FileMapManager</c>, which resolves experiences from <c>FileActions/default-filemap.json</c> beside
/// the running exe — absent in a test host, where it therefore matches nothing at all. That last hop is
/// covered by <c>FeatureTouchPointTests</c> (every viewer action is reachable from the bundled filemap)
/// and by the manual pass.
/// </para>
/// </summary>
[TestClass]
[DoNotParallelize]
[CoversNode("winfs-file-actions")]
public class VirtualBackingActionTests
{
    private string _dir = string.Empty;
    private string _zip = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        VirtualFileSystem.Instance.RegisterHandler(new ZipArchiveHandler());
        _dir = Path.Combine(Path.GetTempPath(), "nexa-backing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);

        _zip = Path.Combine(_dir, "bundle.zip");
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var s = zip.CreateEntry("tool.exe").Open();
            var bytes = new UTF8Encoding(false).GetBytes("MZ-not-really");
            s.Write(bytes, 0, bytes.Length);
        }
        File.WriteAllBytes(_zip, ms.ToArray());
        File.WriteAllText(Path.Combine(_dir, "tool.exe"), "MZ-not-really");
    }

    [TestCleanup]
    public void Cleanup() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    // ── The declaration ──────────────────────────────────────────────────────

    [TestMethod]
    public void EveryActionThatLaunchesTheFileElsewhereDeclaresItNeedsARealFolder()
    {
        Assert.IsTrue(((IFileAction)new ExecuteFile()).RequiresFullyBackedPath,
                      "Run needs the exe's dependencies");
        Assert.IsTrue(((IFileAction)new InstallPackage()).RequiresFullyBackedPath,
                      "Install needs the package's payload");
        Assert.IsTrue(((IFileAction)new CustomAction(new ExternalAppDefinition())).RequiresFullyBackedPath,
                      "a user-configured program is opaque — assume it looks around the file");
        Assert.IsTrue(((IFileAction)new ShellVerbAction("open", "Open", "cmd.exe", "/any")).RequiresFullyBackedPath,
                      "a registered verb hands off to whatever program claims the type");
    }

    [TestMethod]
    public void AnActionThatOnlyHandsOverOneFileIsUnrestricted()
    {
        // The default, so opening / inspecting keeps working inside an archive — the point of the VFS.
        // These materialise a single file and that is genuinely all they need.
        Assert.IsFalse(((IFileAction)new FileProperties()).RequiresFullyBackedPath);
        Assert.IsFalse(((IFileAction)new OpenWithAction()).RequiresFullyBackedPath);
    }

    // ── The classification the filter keys on ────────────────────────────────

    [TestMethod]
    public void AnArchiveEntryIsMaterializedSoLaunchingActionsAreWithheld()
    {
        Assert.AreEqual(VirtualBacking.Materialized,
                        VirtualFileSystem.Instance.GetBacking(Path.Combine(_zip, "tool.exe")));
    }

    [TestMethod]
    public void ARealFileAndAMountedFileAreBothFullyBacked()
    {
        const string id = "backingclass";
        VirtualFileSystem.Instance.RegisterMount(new VirtualMount(id, "Backing", _dir));
        try
        {
            Assert.AreEqual(VirtualBacking.Real,
                            VirtualFileSystem.Instance.GetBacking(Path.Combine(_dir, "tool.exe")));
            Assert.AreEqual(VirtualBacking.PassThrough,
                            VirtualFileSystem.Instance.GetBacking($@"{VirtualMount.RootFor(id)}\tool.exe"));
        }
        finally { VirtualFileSystem.Instance.UnregisterMount(id); }
    }

    // ── What the OS actually receives ────────────────────────────────────────

    [TestMethod]
    public void AMountedPathIsHandedToWindowsAsItsRealPathWithNoCopy()
    {
        const string id = "backingreal";
        VirtualFileSystem.Instance.RegisterMount(new VirtualMount(id, "Backing Real", _dir));
        try
        {
            Assert.AreEqual(Path.Combine(_dir, "tool.exe"),
                            ShellPath.Realize($@"{VirtualMount.RootFor(id)}\tool.exe"));
        }
        finally { VirtualFileSystem.Instance.UnregisterMount(id); }
    }

    [TestMethod]
    public void AnInArchivePathIsHandedToWindowsAsATempCopySoClipboardAndDragStillWork()
    {
        var real = ShellPath.Realize(Path.Combine(_zip, "tool.exe"));

        Assert.AreNotEqual(Path.Combine(_zip, "tool.exe"), real);
        Assert.IsTrue(File.Exists(real), "the copy is real enough for Explorer to accept on a drop");
    }

    [TestMethod]
    public void AMutationResolvesAMountButNeverMaterialisesAnArchiveEntry()
    {
        const string id = "backingmut";
        VirtualFileSystem.Instance.RegisterMount(new VirtualMount(id, "Backing Mut", _dir));
        try
        {
            Assert.AreEqual(Path.Combine(_dir, "tool.exe"),
                            ShellPath.RealForMutation($@"{VirtualMount.RootFor(id)}\tool.exe"));

            // Renaming or deleting a temp copy would report success and leave the original untouched.
            var inZip = Path.Combine(_zip, "tool.exe");
            Assert.AreEqual(inZip, ShellPath.RealForMutation(inZip));
        }
        finally { VirtualFileSystem.Instance.UnregisterMount(id); }
    }
}
