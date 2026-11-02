using System.Collections.Generic;
using System.ComponentModel;
using System.IO;

using Nexaflow.Markdown.Editing;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Editing;

/// <summary>
/// A document being written in, as a page holds it: what it is at, whether that differs from what was saved, and what changed —
/// with nothing of a control in it.
/// </summary>
[TestClass]
[CoversNode("markdown-long-writing")]
public class EditedDocumentTests
{
    [TestMethod]
    public void WhatIsLoadedIsWhatWasSaved_WithNothingToTakeBack()
    {
        var document = new EditedDocument("first");
        document.Write(ContentChange.Write(5, 0, " edit"));

        document.Load("another");

        Assert.AreEqual("another", document.ToString());
        Assert.IsFalse(document.IsDirty);
        Assert.IsFalse(document.CanUndo, "what was written in the last one is not this one's to take back");
    }

    [TestMethod]
    public void AWriteOnTheReadersBehalf_IsAStepThatMakesItDirty()
    {
        var document = new EditedDocument("Some words.");

        document.Write(ContentChange.Write(5, 0, "more "));

        Assert.AreEqual("Some more words.", document.ToString());
        Assert.IsTrue(document.IsDirty);

        var back = document.Undo(new EditState(document.Current, 0))!;
        Assert.AreEqual("Some words.", back.Document.ToString());
        Assert.IsFalse(document.IsDirty, "taken back, it is what was saved again");
    }

    [TestMethod]
    public void WrittenBackByHand_ItIsWhatWasSaved()
    {
        var document = new EditedDocument("abc");

        document.Write(ContentChange.Write(1, 1, "X"));
        Assert.IsTrue(document.IsDirty);

        document.Write(ContentChange.Write(1, 1, "b"));
        Assert.IsFalse(document.IsDirty, "the characters are what was saved, however they came to be");
    }

    [TestMethod]
    public void WhatIsSaved_IsWhatItIsHeldUpAgainst()
    {
        var document = new EditedDocument("one");
        document.Write(ContentChange.Write(3, 0, " two"));

        document.MarkSaved();
        Assert.IsFalse(document.IsDirty);

        document.Write(ContentChange.Write(7, 0, " three"));
        Assert.IsTrue(document.IsDirty);
    }

    [TestMethod]
    public void EveryVersionItMovesTo_IsToldOnce_WithHowItCameTo()
    {
        var document = new EditedDocument("x");
        var told = new List<ContentChangeKind>();
        var said = new List<string?>();
        document.Changed += (_, change) => told.Add(change.Kind);
        ((INotifyPropertyChanged)document).PropertyChanged += (_, e) => said.Add(e.PropertyName);

        var before = new EditState(document.Current, 1);
        document.Wrote(before, before.Write("y"));
        document.Undo(new EditState(document.Current, 2));
        document.Load("z");

        CollectionAssert.AreEqual(new[] { ContentChangeKind.Written, ContentChangeKind.Undone, ContentChangeKind.Loaded }, told);
        CollectionAssert.Contains(said, nameof(EditedDocument.IsDirty));
        CollectionAssert.Contains(said, nameof(EditedDocument.CanUndo));
    }

    [TestMethod]
    public void SavingIsWritingWhatItHolds()
    {
        var document = new EditedDocument("# Title\n\nBody.\n");
        using var writer = new StringWriter();

        document.WriteTo(writer);

        Assert.AreEqual("# Title\n\nBody.\n", writer.ToString());
    }
}
