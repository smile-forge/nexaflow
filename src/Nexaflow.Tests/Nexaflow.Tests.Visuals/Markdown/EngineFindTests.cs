using System;
using System.Linq;

using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// Finding and going to a place are the engine's: asked of an engine with nothing showing it, it finds, quotes and marks what it found,
/// stands its view where it is told, and — for content shown whole — says what whatever holds it is to bring onto its screen.
/// </summary>
[TestClass]
[DoNotParallelize]
[CoversNode("markdown-finding")]
public class EngineFindTests
{
    [TestMethod]
    public void AWordIsFoundEverywhereItIsWritten_QuotedByItsLine_AndMarkedDownThePage() => UiThread.Run(() =>
    {
        var engine = Laid("# Title\n\nalpha beta\n\ngamma alpha\n", language: null);

        engine.FindWord("alpha");

        Assert.AreEqual(2, engine.Found.Count, "every place the word is written is found");
        Assert.AreEqual(0, engine.FoundAt, "the first is the one looked at");
        Assert.AreEqual("gamma alpha", engine.FoundPreview(1), "a place is quoted by the line it is on");

        var marks = engine.FoundMarkPositions();
        Assert.AreEqual(2, marks.Count, "every place has its mark");
        Assert.IsTrue(marks[0] < marks[1] && marks.All(mark => mark is >= 0 and <= 1), "marks run down the page in order");

        engine.StopFinding();
        Assert.AreEqual(0, engine.Found.Count, "stopping forgets what was found");
    });

    [TestMethod]
    public void AViewOfItsOwn_StandsFromALine_AndCentresOne() => UiThread.Run(() =>
    {
        var text = string.Join('\n', Enumerable.Range(1, 200).Select(line => $"line {line}"));
        var engine = Laid(text, ContentLanguages.AsTyped);
        engine.Scrolls = true;
        engine.ViewSized(400, 120);

        var fifty = engine.State.Document.OffsetOf(49);
        Assert.IsNotNull(engine.StandFrom(fifty), "line fifty stands somewhere");
        Assert.AreEqual(fifty, engine.ShownFrom, "the view stands from the line it was put at");

        Assert.IsNull(engine.ScrollToLine(99, ShownPlace.Centre, 120), "a view of its own is moved, not handed to a holder");
        var hundred = engine.Laid.Root.CaretRect(engine.State.Document.OffsetOf(99));
        Assert.AreEqual(60, hundred.Y - engine.ViewOrigin.Y, hundred.Height, "the line stands half way down the view");
    });

    [TestMethod]
    public void ContentShownWhole_AsksItsHolderToBringAPlaceOntoItsScreen_CentredUnlessItIsThereAlready() => UiThread.Run(() =>
    {
        var text = string.Join('\n', Enumerable.Range(1, 60).Select(line => $"line {line}"));
        var engine = Laid(text, ContentLanguages.AsTyped);
        engine.OnScreen = new LayoutRect(0, 0, 400, 100);

        LayoutRect? asked = null;
        engine.Bringing += (_, box) => asked = box;

        Assert.IsTrue(engine.ShowPlace((engine.State.Document.OffsetOf(1), 4), choose: false), "the second line is there to show");
        Assert.IsNull(asked, "a place on the screen already is left where it stands");

        var far = engine.State.Document.OffsetOf(49);
        Assert.IsTrue(engine.ShowPlace((far, 4)), "line fifty is there to show");
        Assert.IsNotNull(asked, "a place off the screen is asked of whatever holds the content");
        Assert.AreEqual(100, asked!.Value.Height, 0.001, "a place shorter than the screen is brought with the screen round it");

        var line = engine.Laid.Root.CaretRect(far);
        Assert.AreEqual(line.Y + (line.Height / 2), asked.Value.Y + 50, line.Height, "and stands half way down it");
        Assert.AreEqual(far, engine.State.SelectionStart, "what was shown is picked out");
    });

    private static ContentEngine Laid(string text, string? language)
    {
        var engine = new ContentEngine(WpfMedium.OnThisThread, WpfStyles.Dark);
        engine.Show(language, WpfStyles.Dark);
        engine.Start(text);
        engine.Fit(400);

        return engine;
    }
}
