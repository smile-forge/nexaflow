using System.Text;
using Nexaflow.Interaction;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Components.Interaction;

/// <summary>
/// A drop or a paste taken (<see cref="DragPayloadReading.Take"/>): read in full while it can be, so what takes it may wait —
/// only the kinds asked for, files that came as content staged, and nothing in it reaching back into the drag.
/// </summary>
[TestClass]
[CoversNode("interaction-drop-staging")]
public class TakeTests
{
    private static DropStaging Staging() => new(TestTemp.CreateSubdirectory("taken-").FullName);

    private static CarriedFile Holding(string name, string content, Action? read = null)
        => new(name, false, into => { read?.Invoke(); into.Write(Encoding.UTF8.GetBytes(content)); });

    [TestMethod]
    public void OnlyTheKindsAskedFor_AreTaken_AndOnlyThoseCarried()
    {
        var dragged = ComposedPayload.OfText("words").With(DragKinds.Html, "<b>words</b>");

        var taken = dragged.Take([DragKinds.Text, DragKinds.Image], Staging());

        Assert.AreEqual("words", taken.Text());
        Assert.IsFalse(taken.Contains(DragKinds.Html), "not asked for");
        Assert.IsFalse(taken.Contains(DragKinds.Image), "not carried");
    }

    [TestMethod]
    public void FilesThatCameAsContent_AreStaged_AndStillToldApartFromFilesThatWereSomewhere()
    {
        var reads = 0;
        var dragged = ComposedPayload.Empty.With(DragKinds.FileContents, new[] { Holding("note.txt", "hello", () => reads++) });

        var taken = dragged.Take([DragKinds.Files, DragKinds.FileContents], Staging());

        Assert.IsFalse(taken.Contains(DragKinds.Files), "they had nowhere of their own");
        Assert.IsFalse(taken.Contains(DragKinds.FileContents), "content that could only be read while the drag lasted is not kept");
        Assert.IsTrue(taken.CarriesFiles());

        var staged = taken.FilesOnDisk().Single();
        Assert.AreEqual("note.txt", Path.GetFileName(staged));
        Assert.AreEqual("hello", File.ReadAllText(staged));
        CollectionAssert.AreEqual(new[] { staged }, taken.FileNames().ToArray());

        taken.FilesOnDisk();
        Assert.AreEqual(1, reads, "read once, as it was taken; never again from the drag");
    }

    [TestMethod]
    public void FilesCut_StayCut_OnceTaken()
    {
        var copied = ComposedPayload.OfFiles([@"C:\a.txt"], cut: true);

        var taken = copied.Take([DragKinds.Files], Staging());

        CollectionAssert.AreEqual(new[] { @"C:\a.txt" }, taken.Files().ToArray());
        Assert.IsTrue(taken.Contains(DragKinds.FilesCut));
    }

    [TestMethod]
    public void FilesByPath_AreTakenAsTheyAre_AndTheirContentIsNotStagedAsWell()
    {
        var dragged = ComposedPayload.OfFiles([@"C:\a.txt"], cut: false)
            .With(DragKinds.FileContents, new[] { Holding("a.txt", "x", () => Assert.Fail("never staged")) });

        var taken = dragged.Take([DragKinds.Files, DragKinds.FileContents], Staging());

        CollectionAssert.AreEqual(new[] { @"C:\a.txt" }, taken.FilesOnDisk().ToArray());
        Assert.IsFalse(taken.Contains(DragKinds.StagedFiles));
    }
}
