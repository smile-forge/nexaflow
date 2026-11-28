using System;
using System.Collections.Generic;
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
    public void TheWindow_HoldsTheFilesCharactersWhereItStands_OnWholeLines_WhereverItMoves()
    {
        var text = Lines(8000);
        var window = Opened(text);

        Assert.AreEqual(text.Length, window.Length);
        Assert.AreEqual(text.Substring(0, window.Document.Current.Length), window.Document.ToString());

        foreach (var centre in new long[] { text.Length / 2, text.Length - 5, 30_000, 0 })
        {
            window.MoveTo(centre);
            var held = window.Document.ToString();

            Assert.AreEqual(text.Substring((int)window.Start, held.Length), held, $"round {centre}");
            Assert.IsTrue(window.Start == 0 || text[(int)window.Start - 1] == '\n', $"the window starts where a line does: round {centre}");
            Assert.IsTrue(window.Start + held.Length == text.Length || held.EndsWith('\n'), $"and ends where one ends: round {centre}");
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
        var written = text[..100] + "WRITTEN" + text[100..];
        Assert.AreEqual(written[..window.Document.Current.Length], window.Document.ToString());
        Assert.AreEqual(text.Length + 7, window.Length);
    }

    [TestMethod]
    public void ItsLines_AreTheFilesLines_CountedOnFromThoseBeforeIt_AndAnyLineOfTheFileIsFound()
    {
        var text = Lines(8000);
        var starts = LineStarts(text);
        var window = Opened(text);

        window.MoveTo(text.Length / 2);
        Write(window, 10, "written\nin two lines ");

        var written = text[..(int)(window.Start + 10)] + "written\nin two lines " + text[(int)(window.Start + 10)..];
        var writtenStarts = LineStarts(written);

        Assert.AreEqual(starts.Count(start => start < window.Start), window.LinesBefore, "the lines before the window");
        Assert.AreEqual(writtenStarts.Count, window.LineCount, "the lines of the whole file, what was written in the window among them");

        foreach (var line in new[] { 0, 1, 2000, (int)window.LinesBefore, (int)window.LinesBefore + 3, writtenStarts.Count - 1 })
            Assert.AreEqual(writtenStarts[line], window.OffsetOfLine(line), $"line {line}");

        Assert.IsNull(window.OffsetOfLine(writtenStarts.Count), "a line past the last");
    }

    [TestMethod]
    public void EveryLineOfTheWholeFile_IsReadInOrder_WithWhereItStarts_AndTheWholeOfItWritten()
    {
        var text = Lines(8000) + "last line, no line end";
        var path = Written(text);
        var window = Opened(path, text);

        window.MoveTo(text.Length / 2);
        Write(window, 0, "W");

        var written = text[..(int)window.Start] + "W" + text[(int)window.Start..];
        var read = new List<(long Number, long Start, string Text)>();
        window.EachLine((number, start, line) => read.Add((number, start, line)));

        var expected = written.Split('\n');
        Assert.AreEqual(expected.Length, read.Count);
        for (var line = 0; line < expected.Length; line += 997)
        {
            Assert.AreEqual(line, read[line].Number);
            Assert.AreEqual(expected[line], read[line].Text);
            Assert.AreEqual(LineStarts(written)[line], read[line].Start);
        }

        using var whole = new StringWriter();
        window.WriteTo(whole);
        Assert.AreEqual(written, whole.ToString());
    }

    [TestMethod]
    public void OpenedIntoAPagesDocument_ThatDocumentIsTheWindows_AndLoadedWholeAgainItStandsOverNone()
    {
        var text = Lines(8000);
        var page = new EditedDocument();
        var file = ContentFile.Open(Written(text), new UTF8Encoding(false), 0, ContentFileAccess.Editable, blockBytes: 4000);
        file.Scan();

        var window = ContentFileWindow.Open(file, page, at: 0, size: Size);

        Assert.AreSame(page, window.Document);
        Assert.AreSame(window, page.Window);

        page.Load("whole");
        Assert.IsNull(page.Window);
    }

    /// <summary>Where each line of <paramref name="text"/> starts, every line feed ending one.</summary>
    private static List<long> LineStarts(string text)
    {
        var starts = new List<long> { 0 };
        for (var at = 0; at < text.Length; at++)
            if (text[at] == '\n') starts.Add(at + 1);

        return starts;
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

    [TestMethod]
    public void WhatAWriterAdds_GrowsAWindowThatReachesTheEnd_AndMovesItOnPastItsSize()
    {
        var text = Lines(2000);
        var path = Written(text);
        var file = ContentFile.Open(path, new UTF8Encoding(false), 0, ContentFileAccess.ReadOnly, blockBytes: 4000);
        file.Scan();
        var window = ContentFileWindow.Open(file, at: file.Characters, size: Size);
        var lines = window.LineCount;

        File.AppendAllText(path, "added one\nadded two\n");
        file.Scan();
        window.Grew();

        StringAssert.EndsWith(window.Document.Current.ToString(), "added one\nadded two\n", "grown into the window that reaches the end");
        Assert.AreEqual(text.Length + 20, window.Length);
        Assert.AreEqual(lines + 2, window.LineCount);
        Assert.IsFalse(window.Document.CanUndo, "growth is no step to take back");
        Assert.IsFalse(window.Document.IsDirty, "nor anything to save");

        var more = Lines(3000)[..60_000];
        File.AppendAllText(path, more);
        file.Scan();
        window.Grew();

        Assert.IsTrue(window.Document.Current.Length <= Size + 100, "moved on rather than grown past its size");
        Assert.AreEqual(window.Length, window.Start + window.Document.Current.Length, "its end still on the file's");
        StringAssert.EndsWith(window.Document.Current.ToString(), more[^500..]);
    }

    [TestMethod]
    public void WhatAWriterAdds_PastAWindowThatDoesNotReachTheEnd_IsHeldAfterIt_AndThereWhenItComes()
    {
        var text = Lines(2000);
        var path = Written(text);
        var file = ContentFile.Open(path, new UTF8Encoding(false), 0, ContentFileAccess.ReadOnly, blockBytes: 4000);
        file.Scan();
        var window = ContentFileWindow.Open(file, at: 0, size: Size);
        var (shown, lines) = (window.Document.Current.ToString(), window.LineCount);

        File.AppendAllText(path, "added one\nadded two\n");
        file.Scan();
        window.Grew();

        Assert.AreEqual(shown, window.Document.Current.ToString(), "the window is left as it was");
        Assert.AreEqual(lines + 2, window.LineCount, "but the file it is over counts the lines added");
        Assert.AreEqual("added two", window.Text(window.OffsetOfLine(window.LineCount - 2)!.Value, 9));

        window.MoveTo(window.Length);
        StringAssert.EndsWith(window.Document.Current.ToString(), text[^200..] + "added one\nadded two\n");
    }

    [TestMethod]
    public void ALineFarBeforeTheWindow_IsFound_ReadingOnlyTheBlockItStartsIn()
    {
        var text = Lines(4000);
        var file = ContentFile.Open(Written(text), new UTF8Encoding(false), 0, ContentFileAccess.ReadOnly, blockBytes: 4000);
        file.Scan();
        var window = ContentFileWindow.Open(file, at: file.Characters, size: Size);
        var starts = LineStarts(text);
        var read = file.CachedCount;

        // Some thirty blocks before the window; the line asked for in the last of them.
        var line = (int)window.LinesBefore - 50;
        Assert.AreEqual(starts[line], window.OffsetOfLine(line));
        Assert.IsTrue(file.CachedCount - read <= 2, $"{file.CachedCount - read} blocks read to find one line");

        foreach (var each in new[] { 0, 1, 777, line, (int)window.LinesBefore, (int)window.LinesBefore + 3, starts.Count - 1 })
            Assert.AreEqual(starts[each], window.OffsetOfLine(each), $"line {each}");
    }

    [TestMethod]
    public void TheWholeFileRead_OffTheWindowsThread_IsAsItWasWhenAsked_WhateverTheWindowDoesMeanwhile()
    {
        var text = Lines(2000);
        var path = Written(text);
        var file = ContentFile.Open(path, new UTF8Encoding(false), 0, ContentFileAccess.ReadOnly, blockBytes: 4000);
        file.Scan();
        var window = ContentFileWindow.Open(file, at: 0, size: Size);

        var lines = window.Lines();
        var writing = window.Writing();

        // Grown and moved after both were asked for.
        File.AppendAllText(path, "added one\nadded two\n");
        file.Scan();
        window.Grew();
        window.MoveTo(window.Length);

        var read = new List<string>();
        lines((_, _, each) => read.Add(each), default);
        Assert.AreEqual(2001, read.Count, "the lines as they were when asked, the closing empty one among them");

        using var written = new StringWriter();
        writing(written);
        Assert.AreEqual(text, written.ToString());
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
