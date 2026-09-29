using Nexaflow.Core.Controls;
using Nexaflow.Core.Services;
using Nexaflow.Tests.Fixtures;
using System;
using System.IO;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>Where a Save As dialog opens, and what the name typed into it comes to.</summary>
[TestClass]
[CoversNode("win-cux-file-picker")]
public class SavePickingTests
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "nexaflow-savepick-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Teardown()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [TestMethod]
    public void TheFolderTheCallerSuggestedIsTheOneItOpensOn()
    {
        Assert.AreEqual(_root, SavePicking.Start(_root, [".png"]));
    }

    [TestMethod]
    public void AFileIsTakenAsTheFolderHoldingIt()
    {
        var doc = Path.Combine(_root, "notes.md");
        File.WriteAllText(doc, "#");

        Assert.AreEqual(_root, SavePicking.Start(doc, [".png"]),
                        "callers pass the document being saved beside, not a folder");
    }

    [TestMethod]
    public void AFolderNothingCanBeSavedIntoIsNotOfferedAsTheStart()
    {
        // A folder that is not there is one where pressing Save could only fail, so the dialog opens where this
        // kind of file belongs instead. The same fallback catches a folder that refuses this user a write.
        Assert.AreEqual(KnownFolderService.PicturesPath,
                        SavePicking.Start(Path.Combine(_root, "gone", "deeper", "chart.png"), [".png"]));

        Assert.AreEqual(KnownFolderService.DocumentsPath,
                        SavePicking.Start(Path.Combine(_root, "gone", "deeper", "notes.reg"), [".reg"]));
    }

    [TestMethod]
    public void WithNothingSuggestedTheExtensionDecidesWhereItBelongs()
    {
        // Registry export passes no folder at all.
        Assert.AreEqual(KnownFolderService.DocumentsPath, SavePicking.Start(null, [".reg"]));
        Assert.AreEqual(KnownFolderService.PicturesPath,  SavePicking.Start(null, [".PNG"]));
        Assert.AreEqual(KnownFolderService.DocumentsPath, SavePicking.Start(null, null));
    }

    [TestMethod]
    public void TheExtensionIsAddedToANameTypedWithoutOne()
    {
        Assert.AreEqual(Path.Combine(_root, "chart.png"), SavePicking.Named(_root, "chart", [".png"]));
    }

    [TestMethod]
    public void ANameThatAlreadyCarriesAnAllowedExtensionKeepsIt()
    {
        Assert.AreEqual(Path.Combine(_root, "chart.png"),  SavePicking.Named(_root, "chart.png", [".png"]));
        Assert.AreEqual(Path.Combine(_root, "chart.PNG"),  SavePicking.Named(_root, "chart.PNG", [".png"]));
        Assert.AreEqual(Path.Combine(_root, "chart.jpg"),  SavePicking.Named(_root, "chart.jpg", [".png", ".jpg"]));
    }

    [TestMethod]
    public void ANameCarryingSomeOtherExtensionStillGetsTheOneAskedFor()
    {
        // Saving a .reg export as "backup.old" means backup.old.reg, not a file reg.exe will not read.
        Assert.AreEqual(Path.Combine(_root, "backup.old.reg"), SavePicking.Named(_root, "backup.old", [".reg"]));
    }

    [TestMethod]
    public void WithNoExtensionAskedForTheNameStandsAsTyped()
    {
        // Hex Save As of a file that had no extension.
        Assert.AreEqual(Path.Combine(_root, "dump"), SavePicking.Named(_root, "dump", null));
        Assert.AreEqual(Path.Combine(_root, "dump"), SavePicking.Named(_root, "dump", []));
    }

    [TestMethod]
    public void APathTypedIntoTheNameBoxIsHonouredOverWhereTheTreeStands()
    {
        Assert.AreEqual(@"D:\elsewhere\chart.png", SavePicking.Named(_root, @"D:\elsewhere\chart", [".png"]));
        Assert.AreEqual(Path.Combine(_root, "sub", "chart.png"),
                        SavePicking.Named(_root, Path.Combine("sub", "chart"), [".png"]));
    }

    [TestMethod]
    public void SurroundingSpaceIsNotPartOfTheName()
    {
        Assert.AreEqual(Path.Combine(_root, "chart.png"), SavePicking.Named(_root, "  chart  ", [".png"]));
    }

    [TestMethod]
    public void WithNothingTypedThereIsNoTarget()
    {
        Assert.IsNull(SavePicking.Named(_root, "", [".png"]));
        Assert.IsNull(SavePicking.Named(_root, "   ", [".png"]));
        Assert.IsNull(SavePicking.Named(_root, null, [".png"]));
    }

    [TestMethod]
    public void WithNoFolderChosenOnlyAPathOfItsOwnIsATarget()
    {
        Assert.IsNull(SavePicking.Named(null, "chart", [".png"]));
        Assert.AreEqual(@"D:\chart.png", SavePicking.Named(null, @"D:\chart", [".png"]));
    }
}
