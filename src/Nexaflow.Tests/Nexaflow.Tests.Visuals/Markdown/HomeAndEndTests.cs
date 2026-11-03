using System;
using System.Linq;
using System.Windows.Input;

using Nexaflow.Markdown.Layout;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// Home and End go to the start and the end of the row the caret stands on — measured along that row, so a fence narrower than the page,
/// a box round a fence and a longer line above all leave it on its own row.
/// </summary>
[TestClass]
[DoNotParallelize]
[CoversNode("markdown-home-end")]
public class HomeAndEndTests
{
    private const string Wide = "A paragraph long enough to run the page out much wider than any line written in the fence below it.";

    [TestMethod]
    public void InATextFence_EndGoesToTheEndOfTheLine_AndHomeToItsStart() => UiThread.Run(() =>
    {
        var document = $"# Heading\n\n{Wide}\n\n```text\nalpha beta\n    indented  two\n```\n\nAfter.\n";
        var engine = Written(document, caret: At(document, "indented"));

        engine.Input(new ContentKey(Key.End));
        Assert.AreEqual(At(document, "two") + "two".Length, engine.State.Caret, "End left the line it was on");

        engine.Input(new ContentKey(Key.Home));
        Assert.AreEqual(At(document, "    indented"), engine.State.Caret, "Home left the line it was on");
    });

    [TestMethod]
    public void InACodeFenceNarrowerThanThePage_EndGoesToTheEndOfTheLine() => UiThread.Run(() =>
    {
        var document = $"{Wide}\n\n```csharp\nvar x = 1;\nvar y = 2;\n```\n";
        var engine = Written(document, caret: At(document, "var y"));

        engine.Input(new ContentKey(Key.End));
        Assert.AreEqual(At(document, "var y = 2;") + "var y = 2;".Length, engine.State.Caret, "End went past the end of the fence's line");
    });

    [TestMethod]
    public void OnTheLastRowOfAWrappedLine_HomeGoesToTheStartOfThatRow_NotOfTheLongerRowAbove() => UiThread.Run(() =>
    {
        var line = string.Join(' ', Enumerable.Range(0, 30).Select(word => $"word{word}"));
        var document = $"```text\n{line}\n```\n";
        var end = At(document, line) + line.Length;
        var engine = Written(document, caret: end);

        engine.Input(new ContentKey(Key.Home));
        var home = engine.State.Caret;
        var root = engine.Laid.Root;

        Assert.IsTrue(home > At(document, line), "Home went to the start of the line rather than of its last row");
        Assert.AreEqual(root.CaretRect(end).Y, root.CaretRect(home).Y, 0.5, "Home left the row it was on");

        engine.Input(new ContentKey(Key.End));
        Assert.AreEqual(end, engine.State.Caret, "End is where the last row ends");
    });

    [TestMethod]
    [DataRow("```text\n{0}```\n", null)]
    [DataRow("```csharp\n{0}```\n", null)]
    [DataRow("{0}", ContentLanguages.AsTyped)]
    public void APressPastTheEndOfAShortLine_PutsTheCaretAtItsEnd_NotOnTheLongerLineAbove(string shape, string? language) => UiThread.Run(() =>
    {
        var document = string.Format(shape, "a much longer line written above the one under it\nbrief\n");
        var engine = new ContentEngine { WrapsLines = language is not null };
        engine.Show(language, WpfStyles.Dark);
        engine.Start(document);
        engine.Fit(400);

        var end = At(document, "brief") + "brief".Length;
        var at = engine.Laid.Root.CaretRect(end);

        engine.Input(new ContentPress(new LayoutPoint(at.Right + 40, at.Y + (at.Height / 2))));
        Assert.AreEqual(end, engine.State.Caret, $"the press past the end of the line went to the line above it: at {at}, laid {engine.Laid.Width}, root {engine.Laid.Root.OffsetAt(new LayoutPoint(at.Right + 40, at.Y + (at.Height / 2)))}, piece {engine.Laid.Root.PieceAt(new LayoutPoint(at.Right + 40, at.Y + (at.Height / 2))).Kind}");
    });

    /// <summary><paramref name="document"/> laid in a room narrower than its widest paragraph, to be written in, the caret at <paramref name="caret"/>.</summary>
    private static ContentEngine Written(string document, int caret)
    {
        var engine = new ContentEngine();
        engine.Show(null, WpfStyles.Dark);
        engine.Start(document);
        engine.Fit(400);
        engine.Apply(engine.State.MoveCaretTo(caret), notify: false);

        return engine;
    }

    private static int At(string document, string said) => document.IndexOf(said, StringComparison.Ordinal);
}
