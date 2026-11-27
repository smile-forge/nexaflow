using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Code;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Code;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// A reading that can start at any line start, read where the reader is and skipped elsewhere: read into anywhere and then whole, it is the
/// reading read from the start — unit by unit the same lines, as tall — and a change into what it skipped reads nothing.
/// </summary>
[TestClass]
[CoversNode("windowed-reading")]
public class SkippedReadingTests
{
    private const string NoGrammar = "no-grammar-code";
    private const int Stretch = 32 * 1024;

    [TestMethod]
    public void ReadIntoTheMiddleAndThenWhole_ItIsTheReadingFromTheStart() => UiThread.Run(() =>
    {
        var document = ContentDocument.Of(MarkdownLayoutBench.GeneratedCode(400 * 1024));
        var parse = CodeParser.Windowed(NoGrammar);

        var from = new ContentUnits(NoGrammar);
        while (!from.Done) from = from.ReadOn(parse, document, Stretch);

        var skipping = ContentUnits.Skipping(NoGrammar, ContentUnit.Read(parse, document, 0, Stretch, null), document.Length, null);
        skipping = skipping.ReadInto(skipping.IndexAt(document.Length / 2), document.Length / 2, parse, document, Stretch, most: 2);
        Assert.IsTrue(skipping.Skipped, "read into the middle, what is either side of it is still skipped");

        skipping = skipping.ReadAllSkipped(parse, document, Stretch);

        Assert.IsFalse(skipping.Skipped);
        Assert.AreEqual(document.Length, skipping.Reached);
        Assert.AreEqual(Height(from), Height(skipping), 1e-6, "read where the reader was, it stands as tall as read from the start");
        Assert.AreEqual(from.Tree(document).ToString(), skipping.Tree(document).ToString());
    });

    [TestMethod]
    public void ReadIntoAtTheVeryEnd_ReadsItsLastStretch_AndTheReadingStillEnds() => UiThread.Run(() =>
    {
        var document = ContentDocument.Of(MarkdownLayoutBench.GeneratedCode(200 * 1024));
        var parse = CodeParser.Windowed(NoGrammar);
        var skipping = ContentUnits.Skipping(NoGrammar, ContentUnit.Read(parse, document, 0, Stretch, null), document.Length, null);

        skipping = skipping.ReadInto(1, document.Length, parse, document, Stretch, most: 4);

        var (last, start) = skipping[skipping.Count - 1];
        Assert.IsFalse(last.Skipped, "where the caret stands at the end, the end is read");
        Assert.IsTrue(document.Length - skipping[skipping.Count - 2].Start >= Stretch || !skipping[skipping.Count - 2].Unit.Skipped,
            "the last stretch's worth of it, as reading on from the start ends");
        Assert.IsTrue(last.Last);
        Assert.IsTrue(skipping.Done);
        Assert.AreEqual(document.Length, start + last.Width);
    });

    [TestMethod]
    public void AUnitIsNeverReadFromABlankLine_SoWhatIsSkippedBeforeItReadsUpToIt() => UiThread.Run(() =>
    {
        // Blank lines at the end, where the caret stands, and before a line in the middle.
        var text = MarkdownLayoutBench.GeneratedCode(200 * 1024) + "\n\n\n";
        var document = ContentDocument.Of(text);
        var parse = CodeParser.Windowed(NoGrammar);

        var from = new ContentUnits(NoGrammar);
        while (!from.Done) from = from.ReadOn(parse, document, Stretch);

        var skipping = ContentUnits.Skipping(NoGrammar, ContentUnit.Read(parse, document, 0, Stretch, null), document.Length, null);
        skipping = skipping.ReadInto(1, document.Length, parse, document, Stretch, most: 1);
        skipping = skipping.ReadAllSkipped(parse, document, Stretch);

        Assert.AreEqual(from.Tree(document).ToString(), skipping.Tree(document).ToString());
    });

    [TestMethod]
    public void AChangeIntoWhatIsSkipped_ReadsNothing_AndKeepsItsWidth() => UiThread.Run(() =>
    {
        var document = ContentDocument.Of(MarkdownLayoutBench.GeneratedCode(200 * 1024));
        var parse = CodeParser.Windowed(NoGrammar);
        var skipping = ContentUnits.Skipping(NoGrammar, ContentUnit.Read(parse, document, 0, Stretch, null), document.Length, null);

        var at = document.Length - 100;
        var edited = document.Apply(new ContentChange([new ContentWrite(at, 0, "inserted\n")], at));
        var (units, _, read) = skipping.Edited(at, at, 9, edited, parse, Stretch);

        Assert.AreEqual(0, read);
        Assert.AreEqual(2, units.Count);
        Assert.AreEqual(edited.Length, units.Reached);
        Assert.IsTrue(units[1].Unit.Skipped);
    });

    [TestMethod]
    public void AUnitLetGo_IsSkippedAgain_JoinedToWhatIsSkippedBesideIt() => UiThread.Run(() =>
    {
        var document = ContentDocument.Of(MarkdownLayoutBench.GeneratedCode(400 * 1024));
        var parse = CodeParser.Windowed(NoGrammar);
        var skipping = ContentUnits.Skipping(NoGrammar, ContentUnit.Read(parse, document, 0, Stretch, null), document.Length, null);
        skipping = skipping.ReadInto(1, document.Length / 2, parse, document, Stretch, most: 1);

        Assert.AreEqual(4, skipping.Count, "the first unit, what is skipped before the one read, it, and what is skipped after");

        var let = skipping.LetGo(2);

        Assert.AreEqual(2, let.Count);
        Assert.IsTrue(let[1].Unit.Skipped);
        Assert.AreEqual(document.Length, let.Reached);
    });

    [TestMethod]
    public void ALongTextOpened_ReadsWhatIsShown_AndJumpedToItsEnd_ReadsItsEndAndNothingBetween() => UiThread.Run(() =>
    {
        var text = MarkdownLayoutBench.GeneratedCode(8 * 1024 * 1024);
        var engine = Opened(text);

        Assert.IsTrue(engine.UnitsRead <= 8, $"opened, it read {engine.UnitsRead} units");

        engine.Uncover(text.Length - 10);
        for (var pass = 0; pass < 10; pass++) engine.Relay();

        Assert.IsTrue(engine.UnitsRead <= 16, $"jumped to its end, it read {engine.UnitsRead} units");
        Assert.IsNull(engine.UnlaidTop(text.Length - 10), "the end was laid");
    });

    [TestMethod]
    public void PagingThroughALongText_KeepsOnlySoManyUnitsRead() => UiThread.Run(() =>
    {
        var text = MarkdownLayoutBench.GeneratedCode(24 * 1024 * 1024);
        var engine = Opened(text);

        for (var page = 0; page < 600; page++)
        {
            engine.OnScreen = new LayoutRect(0, page * 40000.0, 600, 800);
            engine.Relay();
        }

        Assert.IsTrue(engine.UnitsRead <= 300, $"paged through, it holds {engine.UnitsRead} units read");
    });

    private static ContentEngine Opened(string text)
    {
        var engine = new ContentEngine(WpfMedium.OnThisThread, WpfStyles.Dark);
        engine.Show(NoGrammar, WpfStyles.Dark);
        engine.Start(text);
        engine.Fit(600);
        engine.OnScreen = new LayoutRect(0, 0, 600, 800);
        for (var pass = 0; pass < 5; pass++) engine.Relay();

        return engine;
    }

    /// <summary>How tall the units stand, each as code laid unwrapped.</summary>
    private static double Height(ContentUnits units) =>
        Enumerable.Range(0, units.Count).Sum(index => CodeSetting.HeightOf(units[index].Unit.Tree, WpfStyles.Dark, WpfMedium.OnThisThread, wrapsIn: null));
}
