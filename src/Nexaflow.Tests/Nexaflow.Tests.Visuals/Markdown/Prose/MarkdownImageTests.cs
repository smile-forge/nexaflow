using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Nexaflow.Markdown.Prose;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Prose;
using Nexaflow.Visuals.Text.Markdown.Stages;

namespace Nexaflow.Tests.Visuals.Markdown.Prose;

/// <summary>
/// A picture in a document: found by the host or beside the document, fitted, and drawn into the page.
///
/// <para>
/// <strong>Where a picture comes from is the host's, and where it goes is the builder's.</strong> A name in
/// the source means nothing on its own — the same <c>![](pic.png)</c> is a file on disk in one document and a
/// frame of a language pack in another — so a stage resolves it and hangs the picture on the node, and by the
/// time a builder sees it there is nothing left to look up.
/// </para>
/// <para>
/// These are the behaviours <c>MarkdownImageResolverTests</c> pinned on the FlowDocument renderer, asked of
/// the layout tree instead.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("md-image-resolver")]
public class MarkdownImageTests
{
    private const string Doc = "![pic alt](pic.png)\n";

    [TestMethod]
    public void TheHostsOwnPictureIsWhatIsDrawn() => UiThread.Run(() =>
    {
        var pixel = Pixel();

        Assert.AreSame(pixel, Pictures(Lay(Doc, named => named == "pic.png" ? pixel : null)).Single(),
            "the resolver's own instance should be what reaches the page");
    });

    [TestMethod]
    public void AndItBeatsTheFileBesideTheDocument() => UiThread.Run(() => WithPngOnDisk(folder =>
    {
        var pixel = Pixel();

        Assert.AreSame(pixel, Pictures(Lay(Doc, _ => pixel, folder)).Single(),
            "a host that answered must beat the file on disk");
    }));

    [TestMethod]
    public void AHostWithNoAnswerFallsBackToTheFile() => UiThread.Run(() => WithPngOnDisk(folder =>
    {
        var drawn = Pictures(Lay(Doc, _ => null, folder)).Single();

        Assert.IsInstanceOfType<BitmapImage>(drawn, "null should fall through to the document's own folder");
        Assert.IsTrue(drawn.IsFrozen, "a file is read whole and frozen, so every view of the page shares it");
    }));

    [TestMethod]
    public void AHostThatThrewIsStillAskedTheFolder() => UiThread.Run(() => WithPngOnDisk(folder =>
    {
        // Throwing is not the same as saying there is none: it is the host failing to answer, and a
        // renderer that took that personally would lose a picture that is sitting right there.
        Assert.AreEqual(1, Pictures(Lay(Doc, _ => throw new InvalidOperationException("host bug"), folder)).Count);
    }));

    [TestMethod]
    public void AndWhereNothingIsFoundTheWordsWrittenInsteadAreDrawn() => UiThread.Run(() =>
    {
        var laid = Lay(Doc, _ => throw new InvalidOperationException("host bug"));

        Assert.AreEqual(0, Pictures(laid).Count, "nothing was found, so nothing is drawn as a picture");
        StringAssert.Contains(Drawn(laid), "pic alt", "which is the whole point of alt text");
    });

    [TestMethod]
    public void APictureIsFittedDownAndNeverUp() => UiThread.Run(() =>
    {
        var huge = Box(Lay(Doc, _ => Blank(4000, 2000)));
        var tiny = Box(Lay(Doc, _ => Blank(12, 8)));

        Assert.AreEqual(600, huge.Width, 0.5, "a picture bigger than the page is brought down to fit");
        Assert.AreEqual(300, huge.Height, 0.5, "and keeps its proportions coming down");

        Assert.AreEqual(12, tiny.Width, 0.5, "a small picture is left alone rather than blown up to fill");
        Assert.AreEqual(8, tiny.Height, 0.5);
    });

    [TestMethod]
    public void APictureSitsInTheSentenceItWasWrittenIn() => UiThread.Run(() =>
    {
        var laid = Lay("Before ![pic alt](pic.png) after.\n", _ => Blank(20, 10));

        var words = laid.Root.SelfAndDescendants()
            .Where(piece => piece.Kind == MarkdownPieces.Words)
            .Select(piece => Math.Round(piece.Bounds.Y))
            .Distinct()
            .ToList();

        Assert.AreEqual(1, words.Count,
            "the sentence broke into more than one line round a picture that should have sat in it");
    });

    [TestMethod]
    public void AndItStandsForTheCharactersItWasWrittenAs() => UiThread.Run(() =>
    {
        var picture = Pieces(Lay(Doc, _ => Blank(20, 10)), MarkdownPieces.Picture).Single();

        Assert.IsNotNull(picture.Part, "a press on the picture has to mean the ![…](…) somebody typed");
        Assert.AreEqual(Doc.IndexOf('!'), picture.Part!.Start);
    });

    [TestMethod]
    public void AnImageNobodyResolvedIsStillTheLinkItWasWritten() => UiThread.Run(() =>
    {
        // No stage ran at all, which is every surface that was never given a way to find a picture.
        var laid = Laying.Lay(null, Doc, 480, StyleFormat.Dark);

        Assert.AreEqual(0, Pictures(laid).Count);
        StringAssert.Contains(Drawn(laid), "pic alt");
    });

    // ── Reading the answers ─────────────────────────────────────────────────

    private static Laid Lay(string source, Func<string, ImageSource?>? asked, string? folder = null) =>
        Laying.Lay(null, source, 480,
                   options: new DiagramRenderOptions { Palette = StyleFormat.Dark, Pictures = MarkdownPictures.Found(asked, folder) });

    private static List<PictureMark> Marks(Laid laid)
    {
        var found = new List<PictureMark>();

        foreach (var piece in laid.Root.SelfAndDescendants())
            foreach (var mark in piece.Marks)
                if (mark is PictureMark picture) found.Add(picture);

        return found;
    }

    private static List<ImageSource> Pictures(Laid laid) => [.. Marks(laid).Select(mark => mark.Picture)];

    private static Rect Box(Laid laid) => Marks(laid).Single().Bounds;

    private static List<Piece> Pieces(Laid laid, string kind) =>
        [.. laid.Root.SelfAndDescendants().Where(piece => piece.Kind == kind)];

    private static string Drawn(Laid laid) =>
        string.Concat(laid.Root.SelfAndDescendants()
                          .Where(piece => piece.Words is not null)
                          .Select(piece => piece.Words!.Glyphs.Text));

    private static BitmapSource Pixel() => Blank(1, 1);

    private static BitmapSource Blank(int wide, int tall)
    {
        var made = BitmapSource.Create(wide, tall, 96, 96, PixelFormats.Bgra32, null,
                                       new byte[wide * tall * 4], wide * 4);
        made.Freeze();

        return made;
    }

    /// <summary>A real pic.png in a throwaway folder, so the file route is live and a host answer has something to beat.</summary>
    private static void WithPngOnDisk(Action<string> body)
    {
        var folder = Path.Combine(Path.GetTempPath(), "nf-md-pic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(Pixel()));

            using (var file = File.Create(Path.Combine(folder, "pic.png"))) encoder.Save(file);

            body(folder);
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch { /* best effort */ }
        }
    }
}
