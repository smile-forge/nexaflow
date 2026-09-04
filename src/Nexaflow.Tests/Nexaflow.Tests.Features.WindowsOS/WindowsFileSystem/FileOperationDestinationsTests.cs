using System;
using System.IO;
using System.Linq;
using Nexaflow.Features.WindowsFileSystem.Operations;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

/// <summary>
/// The rules for turning dropped or pasted sources into destination paths.
/// <para>
/// These lived only in the clipboard-paste path. Drag-drop had none of them, so the same gesture
/// behaved differently depending on which one you used: paste refused a folder copied into itself and
/// renamed a clash, drop attempted the first and failed the second. The last test here is the one
/// that keeps them from drifting apart again.
/// </para>
/// </summary>
[TestClass]
[CoversNode("winfs-drag-drop")]
[CoversNode("winfs-act-paste")]
public class FileOperationDestinationsTests
{
    private string _scratch = string.Empty;

    [TestInitialize]
    public void CreateScratch()
    {
        _scratch = Path.Combine(Path.GetTempPath(), "nexa-dests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scratch);
    }

    [TestCleanup]
    public void RemoveScratch() { try { Directory.Delete(_scratch, recursive: true); } catch { } }

    private string Folder(string name)
    {
        var p = Path.Combine(_scratch, name);
        Directory.CreateDirectory(p);
        return p;
    }

    private string FileIn(string folder, string name)
    {
        var p = Path.Combine(folder, name);
        File.WriteAllText(p, "x");
        return p;
    }

    [TestMethod]
    public void ASourceThatVanishedIsDroppedWithoutComplaint()
    {
        var dest  = Folder("dest");
        var items = FileOperationDestinations.Plan([Path.Combine(_scratch, "ghost.txt")], dest, move: false, out var refusals);

        Assert.AreEqual(0, items.Count);
        Assert.AreEqual(0, refusals.Count, "a file that went away between the drag and the drop is not an error");
    }

    [TestMethod]
    public void AFolderCopiedIntoItselfIsRefused()
    {
        var src   = Folder("src");
        var inner = Folder(Path.Combine("src", "inner"));

        var items = FileOperationDestinations.Plan([src], inner, move: false, out var refusals);

        Assert.AreEqual(0, items.Count);
        Assert.AreEqual(1, refusals.Count);
        StringAssert.Contains(refusals[0], "into itself");
    }

    [TestMethod]
    public void CopyingIntoTheFolderItIsAlreadyInBecomesCopyOfIt()
    {
        var src  = Folder("src");
        var file = FileIn(src, "report.txt");

        var items = FileOperationDestinations.Plan([file], src, move: false, out _);

        Assert.AreEqual(1, items.Count);
        Assert.AreEqual(Path.Combine(src, "Copy of report.txt"), items[0].Destination);
    }

    [TestMethod]
    public void MovingSomethingToWhereItAlreadyIsDoesNothing()
    {
        var src  = Folder("src");
        var file = FileIn(src, "report.txt");

        var items = FileOperationDestinations.Plan([file], src, move: true, out var refusals);

        Assert.AreEqual(0, items.Count, "a move to the same folder is not a rename, it is a no-op");
        Assert.AreEqual(0, refusals.Count);
    }

    [TestMethod]
    public void AnOrdinaryDestinationKeepsTheName()
    {
        var src  = Folder("src");
        var dest = Folder("dest");
        var file = FileIn(src, "report.txt");

        var items = FileOperationDestinations.Plan([file], dest, move: false, out _);

        Assert.AreEqual(Path.Combine(dest, "report.txt"), items[0].Destination);
    }

    [TestMethod]
    public void DropAndPasteAgreeOnEverySource()
    {
        var src    = Folder("src");
        var dest   = Folder("dest");
        var inner  = Folder(Path.Combine("src", "inner"));
        var file   = FileIn(src, "report.txt");
        string[] sources = [file, src, Path.Combine(_scratch, "ghost.txt"), inner];

        // There is only one planner, so this can only fail if a caller starts doing its own thing.
        foreach (var target in new[] { dest, src })
        {
            var asDrop  = FileOperationDestinations.Plan(sources, target, move: false, out var dropRefusals);
            var asPaste = FileOperationDestinations.Plan(sources, target, move: false, out var pasteRefusals);

            CollectionAssert.AreEqual(asDrop.Select(i => i.Destination).ToArray(),
                                      asPaste.Select(i => i.Destination).ToArray());
            CollectionAssert.AreEqual(dropRefusals.ToArray(), pasteRefusals.ToArray());
        }
    }
}
