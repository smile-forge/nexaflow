using System;
using System.Linq;
using System.Windows.Input;

using Nexaflow.Markdown.Layout;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// A box narrower than the lines of code it keeps whole slides sideways inside the room it has, rather than running past it: slid by the
/// wheel, by its bar, and to wherever the caret or a match is brought — and on paper, where nothing slides, its lines wrap instead.
/// </summary>
[TestClass]
[DoNotParallelize]
[CoversNode("md-code-slide")]
public class SlidingBoxTests
{
    private const double Room = 300;

    private static readonly string Long = "var said = \"" + new string('x', 120) + "\";";

    private static readonly string Document = $"Before.\n\n```csharp\n{Long}\nvar y = 2;\n```\n\nAfter.\n";

    [TestMethod]
    public void ACodeFenceWiderThanItsBox_SlidesInsideIt_TheBoxKeepingToTheRoom() => UiThread.Run(() =>
    {
        var engine = Written(Document);
        var (_, slide) = Slid(engine);

        Assert.AreEqual(Room, engine.Laid.Size.Width, 0.5, "the box kept to the room");
        Assert.IsTrue(slide.Whole > slide.Shown, "it says it holds more than it shows");
        Assert.AreEqual(0, slide.At, "nobody has slid it yet");
        Assert.IsFalse(slide.Bar.IsEmpty, "and it has a bar to say so");
    });

    [TestMethod]
    public void AFenceWhoseLinesFit_SlidesNothing() => UiThread.Run(() =>
    {
        var engine = Written("```csharp\nvar x = 1;\n```\n");

        Assert.IsFalse(Slides(engine).Any(), "nothing to slide, so no bar");
    });

    [TestMethod]
    public void TheWheelTurnedSideways_SlidesIt_AsFarAsItGoesEachWay() => UiThread.Run(() =>
    {
        var engine = Written(Document);
        var (piece, slide) = Slid(engine);
        var over = Inside(piece, slide);

        var end = Document.IndexOf(Long, StringComparison.Ordinal) + Long.Length;
        var was = engine.Laid.Root.CaretRect(end).X;

        Assert.IsTrue(engine.Input(new ContentSlide(over, 100)), "the box took the wheel");
        Assert.AreEqual(100, Slid(engine).Slide.At, 0.01);
        Assert.AreEqual(was - 100, engine.Laid.Root.CaretRect(end).X, 0.5, "what it holds is drawn slid along with it");

        engine.Input(new ContentSlide(over, -1000));
        Assert.AreEqual(0, Slid(engine).Slide.At, "back to its start, and no further");

        engine.Input(new ContentSlide(over, 100_000));
        Assert.AreEqual(slide.Furthest, Slid(engine).Slide.At, 0.01, "on to where its last character shows, and no further");

        Assert.IsFalse(engine.Input(new ContentSlide(new LayoutPoint(5, 2), 100)), "over the words before it, nothing slides");
    });

    [TestMethod]
    public void HoldingTheThumbAndDragging_SlidesIt_AndPressingTheBarBesideTheThumbSlidesABoxful() => UiThread.Run(() =>
    {
        var engine = Written(Document);
        var (piece, slide) = Slid(engine);
        var origin = piece.Bounds;
        var thumb = new LayoutPoint(origin.X + slide.Thumb.X + (slide.Thumb.Width / 2), origin.Y + slide.Thumb.Y + (slide.Thumb.Height / 2));

        engine.Input(new ContentPress(thumb));
        engine.Input(new ContentDrag(thumb with { X = thumb.X + 20 }));
        engine.Input(new ContentRelease());

        var runs = slide.Bar.Width - slide.Thumb.Width;
        Assert.AreEqual(20 * slide.Furthest / runs, Slid(engine).Slide.At, 0.01, "the box slid as far as the thumb moved, scaled to the whole");
        Assert.AreEqual(Document.Length, engine.State.Document.Length, "and nothing was written or picked out");
        Assert.IsFalse(engine.State.HasSelection);

        var (_, now) = Slid(engine);
        var beyond = new LayoutPoint(origin.X + slide.Bar.Right - 1, thumb.Y);
        engine.Input(new ContentPress(beyond));
        engine.Input(new ContentRelease());

        Assert.AreEqual(Math.Min(now.Furthest, now.At + now.Shown), Slid(engine).Slide.At, 0.01, "a boxful further on");
    });

    [TestMethod]
    public void APressInsideTheBoxOnceSlid_LandsOnTheLetterDrawnThere() => UiThread.Run(() =>
    {
        var engine = Written(Document);
        var (piece, slide) = Slid(engine);

        engine.Input(new ContentSlide(Inside(piece, slide), 200));

        var target = Document.IndexOf(Long, StringComparison.Ordinal) + 40;
        var at = engine.Laid.Root.CaretRect(target);

        engine.Input(new ContentPress(new LayoutPoint(at.X + 0.5, at.Y + (at.Height / 2))));
        engine.Input(new ContentRelease());

        Assert.AreEqual(target, engine.State.Caret, "the press went where the letter is drawn, slid along");
    });

    [TestMethod]
    public void APlaceTheBoxHasSlidOutOfView_IsSlidBackIntoIt() => UiThread.Run(() =>
    {
        var engine = Written(Document);
        var end = Document.IndexOf(Long, StringComparison.Ordinal) + Long.Length;

        Assert.IsTrue(engine.SlideOnto(end), "the end of the long line was out of view");

        var (piece, slide) = Slid(engine);
        var caret = engine.Laid.Root.CaretRect(end).X - piece.Bounds.X;
        Assert.IsTrue(caret >= slide.Window.Left && caret <= slide.Window.Right, $"the end of the line shows: {caret} in {slide.Window}");

        Assert.IsFalse(engine.SlideOnto(end), "showing already, it slides no further");
        Assert.IsFalse(engine.SlideOnto(Document.IndexOf("After", StringComparison.Ordinal)), "a place in no box slides nothing");
    });

    [TestMethod]
    public void HowFarItIsSlid_IsKeptThroughAnEditBeforeIt() => UiThread.Run(() =>
    {
        var engine = Written(Document);
        var (piece, slide) = Slid(engine);

        engine.Input(new ContentSlide(Inside(piece, slide), 150));
        engine.Apply(engine.State.MoveCaretTo(0), notify: false);
        engine.Input(new ContentText("Just "));

        Assert.AreEqual(150, Slid(engine).Slide.At, 0.01, "the same fence, slid where the reader left it");
    });

    [TestMethod]
    public void OnPaper_NothingSlides_AndTheLinesWrapInsideTheBox() => UiThread.Run(() =>
    {
        var engine = new ContentEngine { Slides = false };
        engine.Show(null, WpfStyles.Dark);
        engine.Start(Document);
        engine.Fit(Room);

        Assert.IsFalse(Slides(engine).Any(), "paper slides nothing");
        Assert.AreEqual(Room, engine.Laid.Size.Width, 0.5, "the box keeps to the room");
        Assert.IsTrue(engine.Laid.Size.Height > Written(Document).Laid.Size.Height, "its long line wrapped onto rows of its own");
    });

    /// <summary><paramref name="document"/> laid to be written in at <see cref="Room"/>.</summary>
    private static ContentEngine Written(string document)
    {
        var engine = new ContentEngine();
        engine.Show(null, WpfStyles.Dark);
        engine.Start(document);
        engine.Fit(Room);

        return engine;
    }

    /// <summary>Every piece laid that slides, and what it says of its sliding.</summary>
    private static IEnumerable<(LayoutPiece Piece, LayoutSlide Slide)> Slides(ContentEngine engine) =>
        engine.Laid.Root.SelfAndDescendants().Where(piece => piece.Acts?.Slide is not null).Select(piece => (piece, piece.Acts!.Slide!));

    /// <summary>The one piece laid that slides.</summary>
    private static (LayoutPiece Piece, LayoutSlide Slide) Slid(ContentEngine engine) => Slides(engine).Single();

    /// <summary>A point inside what <paramref name="slide"/> shows, on the page.</summary>
    private static LayoutPoint Inside(LayoutPiece piece, LayoutSlide slide) =>
        new(piece.Bounds.X + slide.Window.X + 10, piece.Bounds.Y + slide.Window.Y + 4);
}
