using System.Linq;

using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// What a page shows over the content and where the caret goes unasked are the engine's to say, asked of an engine nothing shows: where a
/// caret starts, the width content is laid in, a page key on a view of its own, the rows a mark is drawn on, and what the pointer is over.
/// </summary>
[TestClass]
[DoNotParallelize]
[CoversNode("vtext-editor")]
public class EngineShownTests
{
    [TestMethod]
    public void AFieldOfOneLanguageIsWrittenAtItsEnd_ADocumentIsReadFromItsTop() => UiThread.Run(() =>
    {
        var field = Laid("x + 1", "latex");
        field.CaretWhereReadingStarts();
        Assert.AreEqual(5, field.State.Caret, "a field is written at the end of what it holds");

        var document = Laid("# Title\n\nWords.\n", null);
        document.Apply(document.State.MoveCaretTo(9), notify: false);
        document.CaretWhereReadingStarts();
        Assert.AreEqual(0, document.State.Caret, "a document is read from its top");
    });

    [TestMethod]
    public void TheWidthLaidIn_IsWhatIsGiven_TheRoomItSlidesIn_NoneToKeepTo_OrAPage() => UiThread.Run(() =>
    {
        var engine = Laid("words", null);

        Assert.AreEqual(300, engine.RoomOffered(300, unlimited: false), "a width given is kept to");
        Assert.AreEqual(680, engine.RoomOffered(double.PositiveInfinity, unlimited: false), "given none, a page of reading");
        Assert.IsTrue(double.IsPositiveInfinity(engine.RoomOffered(double.PositiveInfinity, unlimited: true)), "or none to keep to");

        engine.SlidingRoom = 512;
        Assert.AreEqual(512, engine.RoomOffered(double.PositiveInfinity, unlimited: true), "sliding sideways, the room it slides in");
    });

    [TestMethod]
    public void PageDownOnAViewOfItsOwn_ScrollsItAScreen() => UiThread.Run(() =>
    {
        var engine = Laid(string.Join('\n', Enumerable.Range(1, 200).Select(line => $"line {line}")), ContentLanguages.AsTyped);
        engine.IsReadOnly = true;
        engine.Scrolls = true;
        engine.ViewSized(400, 100);

        Assert.IsTrue(engine.Input(new ContentKey(EditingKey.PageDown)), "the key is the view's");
        Assert.AreEqual(100, engine.ViewOrigin.Y, 0.5, "the view moved a screen down");
    });

    [TestMethod]
    public void TheRowsALineMarkIsDrawnOn_AreThoseOfItsLines_NearTheScreen() => UiThread.Run(() =>
    {
        var engine = Laid("one\ntwo\nthree\nfour", ContentLanguages.AsTyped);
        var near = engine.NearScreen(new LayoutRect(0, 0, 400, 1000));

        Assert.IsNotNull(near, "a screen said is a stretch near it");
        Assert.IsNull(engine.NearScreen(null), "nothing is near a screen nobody has said");

        var rows = engine.RowsOfLines(0, engine.State.Document.Length, near!.Value).ToList();
        Assert.AreEqual(4, rows.Count, "a row for every line");
        Assert.IsTrue(rows.Zip(rows.Skip(1)).All(pair => pair.First.Top < pair.Second.Top), "the rows run down the page");
        Assert.IsTrue(rows.All(row => row.Tall > 0), "and each has a height");
    });

    [TestMethod]
    public void ThePointerIsABarOverWhatCanBeWrittenIn_AndAnArrowWhereNothingCan() => UiThread.Run(() =>
    {
        var engine = Laid("words to write in", ContentLanguages.AsTyped);
        var over = engine.Laid.Root.CaretRect(3);
        var on = new LayoutPoint(over.X, over.Y + (over.Height / 2));

        Assert.AreEqual(PointerShape.Text, engine.PointedAt(on).Shape, "a bar over words written in");

        engine.IsReadOnly = true;
        Assert.AreEqual(PointerShape.Arrow, engine.PointedAt(on).Shape, "an arrow where nothing may be written");
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
