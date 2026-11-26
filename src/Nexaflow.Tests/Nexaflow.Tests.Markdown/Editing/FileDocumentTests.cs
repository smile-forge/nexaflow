using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Nexaflow.Markdown.Editing;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Editing;

/// <summary>
/// A document over a file written in, grown and saved: a long stretch taken out kept as the file's leaves rather than a string, what
/// another program adds taken up with nothing to take back, and a save that leaves the document over the file it wrote, every step kept.
/// </summary>
[TestClass]
[CoversNode("content-file")]
public class FileDocumentTests
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void Begin() => Directory.CreateDirectory(_folder = Path.Combine(Path.GetTempPath(), "nexaflow-file-document-" + Guid.NewGuid().ToString("N")));

    [TestCleanup]
    public void End()
    {
        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
    }

    [TestMethod]
    public void ALongStretchTakenOut_IsKeptToBeWrittenBack_AsTheFilesOwnLeaves()
    {
        var text = Lines(12000);
        using var file = Opened(text, ContentFileAccess.Editable);
        var before = ContentDocument.Of(file);

        var after = before.Apply(new ContentChange([new ContentWrite(1000, 300000, string.Empty)], 1000));

        var kept = after.Inverse!.Writes.Single();
        Assert.IsNotNull(kept.Held, "what was taken out is a stretch of the version it was taken from");
        Assert.AreEqual(300000, kept.WrittenLength);
        Assert.IsTrue(kept.Held.HeldCharacters <= 4 * 4096,
            "left where it stands in the file, so keeping it costs only the leaves its two ends were cut out of");

        Assert.AreEqual(text, after.Apply(after.Inverse).ToString(), "written back, it is what was taken out");
    }

    [TestMethod]
    public void TakingBackALongStretchTakenOut_PutsItBack_WithoutHoldingIt()
    {
        // Long enough that what is left once the stretch is taken out is still a rope over the file, not a string of it.
        var text = Lines(12000);
        using var file = Opened(text, ContentFileAccess.Editable);
        var document = new EditedDocument(ContentDocument.Of(file));

        var start = new EditState(document.Current, 0);
        var cut = start.Made(new ContentChange([new ContentWrite(5000, 250000, "x")], 5001));
        document.Wrote(start, cut);

        var back = document.Undo(cut)!;

        Assert.AreEqual(text, back.Document.ToString());
        Assert.IsTrue(back.Document.HeldCharacters < 20000, "what came back is the file's leaves again, not a string of them");
    }

    [TestMethod]
    public void WhatAnotherProgramAdds_IsGrownOn_WithNothingToTakeBack_AndAStepWrittenBeforeKept()
    {
        var path = Written("first\n");
        using var file = ContentFile.Open(path, new UTF8Encoding(false), 0, ContentFileAccess.ReadOnly);
        file.Scan();

        var document = new EditedDocument(ContentDocument.Of(file));
        var told = new List<ContentChangeKind>();
        document.Changed += (_, change) => told.Add(change.Kind);

        var blocks = file.BlockCount;
        File.AppendAllText(path, "second\n");
        file.Scan();
        document.Grow(ContentDocument.Of(file, blocks));

        Assert.AreEqual("first\nsecond\n", document.ToString());
        Assert.IsFalse(document.CanUndo);
        Assert.IsFalse(document.IsDirty);
        CollectionAssert.AreEqual(new[] { ContentChangeKind.Grown }, told);
    }

    [TestMethod]
    public void GrowingAtTheEnd_KeepsWhatCanBeTakenBack_AndGrowingAtTheStart_LetsItGo()
    {
        var document = new EditedDocument("middle\n");
        var start = new EditState(document.Current, 0);
        var typed = start.Made(ContentChange.Write(0, 0, "> "));
        document.Wrote(start, typed);
        document.MarkSaved();

        document.Grow(ContentDocument.Of("end\n"));
        Assert.IsTrue(document.CanUndo);
        Assert.AreEqual("middle\nend\n", document.Undo(new EditState(document.Current, 2))!.Document.ToString());

        document.Redo(new EditState(document.Current, 0));
        document.MarkSaved();
        document.Grow(ContentDocument.Of("start\n"), atStart: true);
        Assert.IsFalse(document.CanUndo, "every offset a step was written at has moved");
        Assert.AreEqual("start\n> middle\nend\n", document.ToString());
    }

    [TestMethod]
    public void ADocumentWrittenIn_DoesNotGrow()
    {
        var document = new EditedDocument("text\n");
        document.Write(ContentChange.Write(0, 0, "x"));

        Assert.ThrowsExactly<InvalidOperationException>(() => document.Grow(ContentDocument.Of("more\n")));
    }

    [TestMethod]
    public void Saving_WritesTheFile_AndLeavesTheDocumentOverIt_EveryStepKept()
    {
        var text = Lines(6000);
        var path = Written(text);
        var opened = ContentFile.Open(path, new UTF8Encoding(false), 0, ContentFileAccess.Editable);
        opened.Scan();

        var document = new EditedDocument(ContentDocument.Of(opened));
        Assert.AreEqual(ContentFileHold.Held, opened.HoldForEdit());

        var start = new EditState(document.Current, 0);
        var typed = start.Made(ContentChange.Write(0, 0, "saved "));
        document.Wrote(start, typed);

        var told = new List<ContentChangeKind>();
        document.Changed += (_, change) => told.Add(change.Kind);

        using var saved = ContentFile.Save(document, path, new UTF8Encoding(false), preamble: false);

        Assert.AreEqual("saved " + text, File.ReadAllText(path));
        Assert.IsFalse(document.IsDirty);
        Assert.IsTrue(document.CanUndo);
        Assert.IsTrue(saved.IsHeld);
        CollectionAssert.AreEqual(new[] { ContentChangeKind.Rebased }, told);
        Assert.IsTrue(document.Current.HeldCharacters < 20000, "it stands over the file it was saved to");

        var back = document.Undo(new EditState(document.Current, 6))!;
        Assert.AreEqual(text, back.Document.ToString());
        Assert.IsTrue(document.IsDirty);

        opened.Dispose();
    }

    [TestMethod]
    public void Saving_WritesTheEncodingsMark_OnlyWhereItIsAskedFor()
    {
        var path = Path.Combine(_folder, "marked.txt");
        var document = new EditedDocument("é\n");

        using (ContentFile.Save(document, path, Encoding.UTF8, preamble: true, hold: false))
            CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF, 0xC3, 0xA9, 0x0A }, File.ReadAllBytes(path));

        using (ContentFile.Save(document, path, Encoding.UTF8, preamble: false, hold: false))
            CollectionAssert.AreEqual(new byte[] { 0xC3, 0xA9, 0x0A }, File.ReadAllBytes(path));
    }

    private ContentFile Opened(string text, ContentFileAccess access)
    {
        var file = ContentFile.Open(Written(text), new UTF8Encoding(false), 0, access, blockBytes: 4000);
        file.Scan();

        return file;
    }

    private string Written(string text)
    {
        var path = Path.Combine(_folder, Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, text, new UTF8Encoding(false));

        return path;
    }

    private static string Lines(int count) =>
        string.Concat(Enumerable.Range(0, count).Select(line => new string((char)('a' + line % 26), line % 97) + "\n"));
}
