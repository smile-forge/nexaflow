using System;
using System.IO;
using System.Linq;
using System.Text;

using Nexaflow.Markdown.Editing;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Editing;

/// <summary>
/// A file too long for one document, shown through a window that moves: what it holds is the file's characters where it stands, what is
/// written stays written wherever the window goes, a step is taken back where it was written, and a save writes all of it.
/// </summary>
[TestClass]
[CoversNode("content-file-window")]
public class ContentFileWindowTests
{
    private const int Size = 20_000;

    private string _folder = string.Empty;

    [TestInitialize]
    public void Begin() => _folder = TestTemp.CreateSubdirectory("file-window").FullName;

    [TestMethod]
    public void TheWindow_HoldsTheFilesCharactersWhereItStands_WhereverItMoves()
    {
        var text = Lines(8000);
        var window = Opened(text);

        Assert.AreEqual(text.Length, window.Length);
        Assert.AreEqual(text[..Size], window.Document.ToString());

        foreach (var centre in new long[] { text.Length / 2, text.Length - 5, 30_000, 0 })
        {
            window.MoveTo(centre);

            Assert.AreEqual(text.Substring((int)window.Start, window.Document.Current.Length), window.Document.ToString(), $"round {centre}");
            Assert.IsTrue(window.Covers(Math.Min(centre, text.Length - 1), Math.Min(centre, text.Length - 1)));
        }
    }

    [TestMethod]
    public void WhatIsWritten_StaysWritten_AsTheWindowLeavesAndComesBack()
    {
        var text = Lines(8000);
        var window = Opened(text);

        Write(window, 100, "WRITTEN");
        window.MoveTo(text.Length / 2);
        Assert.IsFalse(window.Document.ToString().Contains("WRITTEN"));

        window.MoveTo(0);
        Assert.AreEqual(text[..100] + "WRITTEN" + text[100..(Size - 7)], window.Document.ToString()[..(Size - 7 + 7)]);
        Assert.AreEqual(text.Length + 7, window.Length);
    }

    [TestMethod]
    public void AnAnchorWhereBothWindowsReach_StaysWithItsCharactersThroughTheMove()
    {
        var text = Lines(8000);
        var window = Opened(text);
        var place = Size - 500;
        using var anchor = window.Document.Anchor(place);

        window.MoveTo(Size);

        Assert.AreEqual(text[place], window.Document.Current[anchor.Offset]);
        Assert.AreEqual(place, window.Start + anchor.Offset);
    }

    [TestMethod]
    public void AStepWrittenBeforeTheWindowMoved_IsTakenBackWhereItWasWritten_TheWindowGoingThere()
    {
        var text = Lines(8000);
        var window = Opened(text);

        Write(window, 50, "EARLY");
        window.MoveTo(text.Length - 1);
        Assert.IsFalse(window.Covers(50, 55));

        window.Document.ReachUndo();
        Assert.IsTrue(window.Covers(50, 55), "taking it back brings the window to it first");

        var back = window.Document.Undo(new EditState(window.Document.Current, 0))!;
        Assert.AreEqual(text.Substring((int)window.Start, back.Document.Length), back.Document.ToString());
        Assert.AreEqual(text.Length, window.Length);

        window.Document.ReachRedo();
        Assert.IsNotNull(window.Document.Redo(new EditState(window.Document.Current, 0)));
        Assert.AreEqual(text.Length + 5, window.Length);
    }

    [TestMethod]
    public void Saving_WritesWhatWasWrittenInEveryWindow_AndKeepsEveryStep()
    {
        var text = Lines(8000);
        var path = Written(text);
        var window = Opened(path, text);

        Write(window, 10, "FIRST");
        window.MoveTo(text.Length - 1000);
        var at = (int)(text.Length - 1000 - window.Start);
        Write(window, at, "SECOND");

        using var saved = window.Save(path, new UTF8Encoding(false), preamble: false);

        // The second place is in the file as written by then, the first write five characters in front of it.
        var first = text[..10] + "FIRST" + text[10..];
        var second = text.Length - 1000;
        var expected = first[..second] + "SECOND" + first[second..];
        Assert.AreEqual(expected, File.ReadAllText(path));
        Assert.IsFalse(window.Document.IsDirty);
        Assert.IsTrue(window.Document.CanUndo);

        window.Document.ReachUndo();
        window.Document.Undo(new EditState(window.Document.Current, 0));
        window.Document.ReachUndo();
        window.Document.Undo(new EditState(window.Document.Current, 0));

        Assert.AreEqual(text.Length, window.Length);
        Assert.IsTrue(window.Document.IsDirty);
    }

    private static void Write(ContentFileWindow window, int at, string text)
    {
        var before = new EditState(window.Document.Current, at);
        window.Document.Wrote(before, before.Made(ContentChange.Write(at, 0, text)));
    }

    private ContentFileWindow Opened(string text) => Opened(Written(text), text);

    private static ContentFileWindow Opened(string path, string text)
    {
        var file = ContentFile.Open(path, new UTF8Encoding(false), 0, ContentFileAccess.Editable, blockBytes: 4000);
        file.Scan();

        return ContentFileWindow.Open(file, at: 0, size: Size);
    }

    private string Written(string text)
    {
        var path = Path.Combine(_folder, Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, text, new UTF8Encoding(false));

        return path;
    }

    private static string Lines(int count) =>
        string.Concat(Enumerable.Range(0, count).Select(line => $"line {line:D6} {new string((char)('a' + line % 26), line % 40)}\n"));
}
