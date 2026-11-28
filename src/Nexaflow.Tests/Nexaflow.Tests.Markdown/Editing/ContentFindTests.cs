using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Nexaflow.Markdown.Editing;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Editing;

/// <summary>
/// The one search every text page drives: over a file's lines — read off the page, marked on the lines drawn, stepped either way or from
/// the caret, narrowed, run again as the file is written — and over a document held whole, a place at a time.
/// </summary>
[TestClass]
[CoversNode("content-search")]
public class ContentFindTests
{
    // Lines: 0 "a needle", 1 "hay", 2 "needle and needle", 3 "hay", 4 "last needle". The needles stand at 2, 13, 24 and 40.
    private const string Haystack = "a needle\nhay\nneedle and needle\nhay\nlast needle\n";

    private static readonly LineQuery Needle = new(line => Written(line, "needle"));

    [TestMethod]
    public async Task AFind_ReadsEveryLineOfTheFile_MarksWhatItFinds_AndGoesToTheFirst()
    {
        using var opened = new Opened(Haystack);

        var found = await opened.Find.FindAsync(Needle, first: 2, default);

        CollectionAssert.AreEqual(new long[] { 0, 2, 4 }, found.Lines);
        CollectionAssert.AreEqual(new[] { 1, 3, 4 }, found.Ends, "how many matches stand up to each line found");
        CollectionAssert.AreEqual(new[] { (0L, "a needle"), (2L, "needle and needle") }, found.First.ToArray(), "the first of them, with what each says");
        Assert.AreEqual(4, opened.Find.Count, "a match at a time: the line holding two counts twice");
        Assert.AreEqual(0, opened.Find.At);
        Assert.IsTrue(opened.Find.IsShowing);
        Assert.AreEqual(2, opened.Editor.Caret, "the caret on the first match itself");
        CollectionAssert.AreEqual(new[] { (2, 6), (13, 6), (24, 6), (40, 6) }, opened.Washed(), "every match on every line marked");
        Assert.AreEqual((2, 6), opened.Editor.Found.LookedAt);
    }

    [TestMethod]
    public async Task Stepping_GoesThroughEveryMatch_RoundEitherWay_AndOnFromTheCaret()
    {
        using var opened = new Opened(Haystack);
        var find = opened.Find;
        await find.FindAsync(Needle, first: 0, default);

        find.Next();
        Assert.AreEqual((1, 13), (find.At, opened.Editor.Caret));
        Assert.AreEqual((13, 6), opened.Editor.Found.LookedAt);

        find.Next();
        Assert.AreEqual((2, 24), (find.At, opened.Editor.Caret), "the second match on the same line, not the next line");
        Assert.AreEqual((24, 6), opened.Editor.Found.LookedAt);

        find.Next();
        find.Next();
        Assert.AreEqual(0, find.At, "past the last, round to the first");

        find.Previous();
        Assert.AreEqual(3, find.At, "before the first, round to the last");

        opened.Editor.MoveCaretTo(10);
        find.StepFromCaret(forward: true);
        Assert.AreEqual(1, find.At, "the first after the caret");

        opened.Editor.MoveCaretTo(10);
        find.StepFromCaret(forward: false);
        Assert.AreEqual(0, find.At, "the last before the caret");

        opened.Editor.MoveCaretTo(13);
        find.StepFromCaret(forward: true);
        Assert.AreEqual(2, find.At, "from a match, on to the next on the same line");

        opened.Editor.MoveCaretTo(24);
        find.StepFromCaret(forward: false);
        Assert.AreEqual(1, find.At, "and back to the one before it on that line");

        opened.Editor.MoveCaretTo(41);
        find.StepFromCaret(forward: true);
        Assert.AreEqual(0, find.At, "past the last, round to the first");
    }

    [TestMethod]
    public async Task Reading_ChangesNothingShown()
    {
        using var opened = new Opened(Haystack);

        var found = await opened.Find.ReadAsync(Needle, first: 10, default);

        Assert.AreEqual((3, 4), (found.Lines.Length, found.Count), "three lines found, four matches on them");
        Assert.IsFalse(opened.Find.IsShowing);
        Assert.IsTrue(opened.Editor.Found.IsEmpty);
        Assert.AreEqual(0, opened.Editor.Caret);
    }

    [TestMethod]
    public async Task Narrowed_ItCountsMarksAndStepsOnlyTheLinesChosen_CarriedThroughAWrite_AndIsNotRunAgain()
    {
        using var opened = new Opened(Haystack);
        var find = opened.Find;
        await find.FindAsync(Needle, first: 0, default);

        Assert.IsTrue(find.Narrow([4, 4, 2]));
        CollectionAssert.AreEqual(new long[] { 2, 4 }, find.Lines.ToArray());
        CollectionAssert.AreEqual(new[] { (13, 6), (24, 6), (40, 6) }, opened.Washed(), "only the lines chosen marked");
        Assert.AreEqual(13, opened.Editor.Caret, "gone to the first of them");

        opened.Document.Write(ContentChange.Write(0, 0, "new\n"));
        CollectionAssert.AreEqual(new long[] { 3, 5 }, find.Lines.ToArray(), "each line carried with what is written on it");
        CollectionAssert.AreEqual(new[] { (17, 6), (28, 6), (44, 6) }, opened.Washed());

        await Task.Delay(600);
        Assert.AreEqual(2, find.Count, "narrowed by hand, it is not run again");
    }

    [TestMethod]
    public async Task WrittenIn_ItRunsAgainOnceWritingPauses_TheReaderKeptOnTheMatchTheyWereOn()
    {
        using var opened = new Opened(Haystack);
        var find = opened.Find;
        await find.FindAsync(Needle, first: 0, default);
        find.Next();

        opened.Document.Write(ContentChange.Write(0, 0, "needle first\n"));
        Assert.AreEqual(5, opened.Washed().Count, "at once, the marks are of what is written now");

        for (var wait = 0; wait < 200 && find.Count != 5; wait++) await Task.Delay(10);

        Assert.AreEqual(5, find.Count, "the match written is counted once writing pauses");
        Assert.AreEqual(3, find.LookedAtLine, "still on the line they were on, a line further down now");
        Assert.AreEqual(13, opened.Editor.Caret - "needle first\n".Length, "and nothing moved");
    }

    [TestMethod]
    public async Task Cleared_NothingIsFoundOrMarked()
    {
        using var opened = new Opened(Haystack);
        await opened.Find.FindAsync(Needle, first: 0, default);

        opened.Find.Clear();

        Assert.AreEqual((0, -1, false), (opened.Find.Count, opened.Find.At, opened.Find.IsShowing));
        Assert.IsTrue(opened.Editor.Found.IsEmpty);
    }

    [TestMethod]
    public void APlaceFind_FindsAgainOnlyWhereAnEditWrote_TheOneLookedAtKept_AndGoesNowhere()
    {
        var document = new EditedDocument(Haystack);
        using var editor = new ContentEditor(document);
        var gone = new List<(int Start, int Length)>();
        var find = new PlaceFind(() => document.Current, () => editor.Found, gone.Add);

        Assert.AreEqual(4, find.Find(Needle, from: 10), "a place at a time");
        Assert.AreEqual(1, find.At, "the first at or after where it was asked from");
        CollectionAssert.AreEqual(new[] { (13, 6) }, gone);

        find.Next();
        document.Write(ContentChange.Write(0, 0, "needle\n"));
        find.Refind();

        Assert.AreEqual(5, find.Count);
        Assert.AreEqual((31, 6), find.LookedAt, "the one looked at, where its characters went");
        Assert.AreEqual(2, gone.Count, "found again as the reader writes, nothing is gone to");
        Assert.AreEqual(5, editor.Found.Places.Count);
    }

    [TestMethod]
    public void PlacesAHostFound_AreCarriedThroughAnEdit_AndRestrictingKeepsThoseChosen()
    {
        var document = new EditedDocument(Haystack);
        using var editor = new ContentEditor(document);
        var gone = new List<(int Start, int Length)>();
        var find = new PlaceFind(() => document.Current, () => editor.Found, gone.Add);

        find.Show([(2, 6), (40, 6)], at: 1);
        Assert.AreEqual(0, gone.Count, "shown, nothing is gone to");
        Assert.AreEqual((40, 6), editor.Found.LookedAt);

        document.Write(ContentChange.Write(0, 0, ">> "));
        find.Refind();
        CollectionAssert.AreEqual(new[] { (5, 6), (43, 6) }, find.Places.ToArray(), "carried, not found again");
        Assert.AreEqual((43, 6), find.LookedAt);

        Assert.AreEqual(1, find.Restrict(new HashSet<int> { 1 }));
        CollectionAssert.AreEqual(new[] { (43, 6) }, gone);
    }

    private static List<(int Index, int Length)> Written(string line, string word)
    {
        var found = new List<(int, int)>();
        for (var at = line.IndexOf(word, StringComparison.Ordinal); at >= 0; at = line.IndexOf(word, at + word.Length, StringComparison.Ordinal)) found.Add((at, word.Length));
        return found;
    }

    /// <summary>A file open in a view, with a search over its lines.</summary>
    private sealed class Opened : IDisposable
    {
        public Opened(string text)
        {
            this.Document = new EditedDocument(text);
            this.Editor = new ContentEditor(this.Document);
            this.File = new ShownFile(this.Editor);
            this.Find = new FileFind(this.File, this.Editor.Found);
        }

        public EditedDocument Document { get; }

        public ContentEditor Editor { get; }

        public ShownFile File { get; }

        public FileFind Find { get; }

        /// <summary>Every stretch the view washes as found, over the whole document.</summary>
        public List<(int Start, int Length)> Washed()
        {
            var document = this.Document.Current;
            return [.. this.Editor.Marks.Mark(new ContentMarkRequest(document, 0, document.Length))
                .Where(mark => mark.Kind == ContentMarkKind.Wash).Select(mark => (mark.Start, mark.Length))];
        }

        public void Dispose()
        {
            this.Find.Dispose();
            this.File.Dispose();
            this.Editor.Dispose();
        }
    }
}
