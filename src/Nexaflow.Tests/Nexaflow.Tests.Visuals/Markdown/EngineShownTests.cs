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
    public void LinesLaidOneUnderAnother_StandTheSameWholeNumberOfPixelsApart_AtAnyDensity() => UiThread.Run(() =>
    {
        foreach (var perUnit in new[] { 1.0, 1.25, 1.5 })
        {
            var engine = new ContentEngine(WpfMedium.OnThisThread, WpfStyles.Dark);
            engine.Show(ContentLanguages.AsTyped, WpfStyles.Dark with { DevicePixel = 1 / perUnit });
            engine.Start(string.Join('\n', Enumerable.Range(1, 30).Select(line => $"line {line}")));
            engine.Fit(400);

            var document = engine.State.Document;
            var tops = Enumerable.Range(0, 30).Select(line => engine.Laid.Root.CaretRect(document.OffsetOf(line)).Y).ToList();
            var apart = tops.Zip(tops.Skip(1), (above, below) => Math.Round((below - above) * perUnit, 6)).Distinct().ToList();

            Assert.AreEqual(1, apart.Count, $"at {perUnit} pixels a unit, lines stand {string.Join(" or ", apart)} pixels apart");
            Assert.AreEqual(Math.Round(apart[0]), apart[0], 1e-6, $"at {perUnit} pixels a unit, lines stand {apart[0]} pixels apart, not a whole number");
        }
    });

    [TestMethod]
    public void ACaretOnAnEmptyLine_StandsOnThatLinesOwnRow_AndSoDoesItsMark() => UiThread.Run(() =>
    {
        var engine = Laid("one\n\nthree", ContentLanguages.AsTyped);
        var (above, empty, below) = (engine.Laid.Root.CaretRect(0), engine.Laid.Root.CaretRect(4), engine.Laid.Root.CaretRect(5));
        var said = $"above {above}, empty {empty}, below {below}";

        Assert.IsTrue(empty.Y >= above.Y + above.Height - 0.5, "the caret on an empty line stands below the line before it: " + said);
        Assert.IsTrue(empty.Y + empty.Height <= below.Y + 0.5, "and above the line after it: " + said);
        Assert.AreEqual(above.Height, empty.Height, 0.5, "as tall as a line: " + said);
        Assert.AreEqual(below.X, empty.X, 0.5, "at the start of its line: " + said);

        var near = engine.NearScreen(new LayoutRect(0, 0, 400, 1000))!.Value;
        var row = engine.RowsOfLines(4, 4, near).Single();
        Assert.AreEqual(empty.Y, row.Top, 0.5, "a mark on the empty line is drawn on its row: " + said);
    });

    [TestMethod]
    public void AStepDownOrAPressOnAnEmptyLine_PutsTheCaretOnIt() => UiThread.Run(() =>
    {
        var engine = Laid("one\n\nthree", ContentLanguages.AsTyped);

        engine.Apply(engine.State.MoveCaretTo(1), notify: false);
        Assert.IsTrue(engine.Input(new ContentKey(EditingKey.Down)), "the key moves the caret");
        Assert.AreEqual(4, engine.State.Caret, "down from the first line lands on the empty line, not past it");

        Assert.IsTrue(engine.Input(new ContentKey(EditingKey.Down)));
        Assert.AreEqual(5, engine.State.Caret, "and down again on the line after it");

        var empty = engine.Laid.Root.CaretRect(4);
        engine.Apply(engine.State.MoveCaretTo(0), notify: false);
        engine.Input(new ContentPress(new LayoutPoint(200, empty.Y + (empty.Height / 2))));
        Assert.AreEqual(4, engine.State.Caret, "a press along the empty line's row puts the caret on it");
    });

    [TestMethod]
    public void ThePointerIsABarOverWords_WrittenInOrOnlyRead_AndAnArrowAwayFromThem() => UiThread.Run(() =>
    {
        var engine = Laid("words to write in", ContentLanguages.AsTyped);
        var over = engine.Laid.Root.CaretRect(3);
        var on = new LayoutPoint(over.X, over.Y + (over.Height / 2));
        var away = new LayoutPoint(over.X, over.Y + (over.Height * 20));

        Assert.AreEqual(PointerShape.Text, engine.PointedAt(on).Shape, "a bar over words written in");
        Assert.AreEqual(PointerShape.Arrow, engine.PointedAt(away).Shape, "an arrow well away from any");

        engine.IsReadOnly = true;
        Assert.AreEqual(PointerShape.Text, engine.PointedAt(on).Shape, "a bar over words only read too: they are picked out from the caret");
    });

    [TestMethod]
    public void TheCaretShowsWhileTheKeyboardIsHeld_WhereItStood_WrittenInOrOnlyRead() => UiThread.Run(() =>
    {
        var engine = Laid("one\ntwo", ContentLanguages.AsTyped);
        engine.Apply(engine.State.MoveCaretTo(5), notify: false);
        Assert.IsFalse(engine.HasCaret, "nothing holds the caret before the keyboard comes");

        engine.KeyboardHeld(true);
        Assert.IsTrue(engine.HasCaret, "gaining the keyboard shows the caret");
        Assert.IsTrue(engine.ShowsCaret, "and there is one to draw");
        Assert.AreEqual(5, engine.State.Caret, "where it stood, put nowhere new");

        engine.KeyboardHeld(false);
        Assert.IsFalse(engine.HasCaret, "losing the keyboard takes it away");
        Assert.IsFalse(engine.ShowsCaret);

        engine.TakeCaret(2);
        Assert.IsTrue(engine.HasCaret, "a caret put at a place is held, keyboard or not");
        engine.ReleaseCaret();

        engine.KeyboardHeld(true);
        engine.ReleaseCaret();
        engine.Replace(engine.State.MoveCaretTo(0).Insert("zero "));
        Assert.IsTrue(engine.HasCaret, "a change nobody typed, while the keyboard is held, shows the caret where it left it");

        engine.KeyboardHeld(false);
        engine.IsReadOnly = true;
        engine.KeyboardHeld(true);
        Assert.IsTrue(engine.HasCaret, "content only read holds a caret too: it is where picking out starts, and the keys move it");

        engine.ReleaseCaret();
        engine.TakeCaret(0);
        Assert.IsTrue(engine.HasCaret, "and so does one put at a place in it");
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
