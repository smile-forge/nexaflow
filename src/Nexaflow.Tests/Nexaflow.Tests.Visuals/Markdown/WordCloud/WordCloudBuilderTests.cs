using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.WordCloud;
using System.Windows.Media.Imaging;

namespace Nexaflow.Tests.Visuals.Markdown.WordCloud;

/// <summary>
/// The picture a block comes to: a run of text per word, where the packing put it, carrying the characters it
/// was written with.
///
/// <para>
/// Nothing here is word-cloud machinery. The shared queries answer where a press landed and where a caret may
/// stand for a formula, a barcode and this alike; what these assert is that the tree handed to them says the
/// right things — that every word is a run somebody can type into, that none of them is drawn on top of
/// another, and that a block which is not a cloud at all still shows its lines.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("wordcloud-render")]
[CoversNode("wordcloud-editing")]
[CoversNode("wordcloud-stencil")]
public class WordCloudBuilderTests
{
    private const string Stack = "WPF: 40\nXAML: 25\nMVVM: 12";

    private static Laid Lay(string source, double room = 480) =>
        Laying.Lay("wordcloud", source, room, StyleFormat.Dark);

    private static Piece[] Words(Laid laid) =>
        [.. laid.Root.SelfAndDescendants().Where(piece => piece.Kind == WordCloudPiece.Word)];

    // ── The words ──────────────────────────────────────────────────────────

    [TestMethod]
    public void EveryWordOfTheBlockIsDrawn() => UiThread.Run(() =>
    {
        Assert.AreEqual(3, Words(Lay(Stack)).Length);
    });

    [TestMethod]
    public void AWordIsDrawnFromTheCharactersItWasWrittenWith() => UiThread.Run(() =>
    {
        // What makes the picture editable: a caret in a word is a caret in the line it came from, so typing
        // into the cloud edits the block.
        foreach (var word in Words(Lay(Stack)))
        {
            var part = word.Part;

            Assert.IsNotNull(part, "a word drawn from nothing could not be typed into");
            CollectionAssert.Contains(new[] { "WPF", "XAML", "MVVM" },
                                      Stack.Substring(part!.Start, part.Length));
        }
    });

    [TestMethod]
    public void AWordIsOneRunWithACaretBetweenAnyTwoOfItsLetters() => UiThread.Run(() =>
    {
        var word = Words(Lay(Stack)).First();

        Assert.IsNotNull(word.Words, "a word is a run of text, not a picture of one");
        Assert.AreNotEqual(Stops.None, word.Stops);
    });

    [TestMethod]
    public void TheHeaviestWordIsTheBiggest() => UiThread.Run(() =>
    {
        var words = Words(Lay(Stack));

        var heaviest = words.Single(word => Said(word) == "WPF");
        var lightest = words.Single(word => Said(word) == "MVVM");

        Assert.IsTrue(heaviest.Bounds.Height > lightest.Bounds.Height,
                      "the weights decide the sizes, and WPF weighs the most");
    });

    /// <summary>
    /// The letters of two words never share a pixel — which is the promise the whole cloud is built on, and
    /// is asked of the letters rather than of the boxes on purpose. Word clouds interlock: a short word
    /// nests in the descender of a long one, so their boxes overlap heavily and should.
    ///
    /// <para>
    /// A builder test rather than a packing one. That two shapes were packed apart is settled headlessly in
    /// <c>WordCloudPackingTests</c>; what this catches is a builder that drew a word somewhere other than
    /// where the packing put it — an outline measured from one origin and set from another.
    /// </para>
    /// </summary>
    [TestMethod]
    public void TheLettersOfTwoWordsNeverTouch() => UiThread.Run(() =>
    {
        var words = Words(Lay("""
                              gap: 4
                              rotate: 0
                              one: 40
                              two: 30
                              three: 20
                              four: 10
                              five: 5
                              """));

        var inked = words.Select(word => word.Words!.Glyphs.BuildGeometry(word.Bounds.TopLeft)).ToList();

        for (var one = 0; one < inked.Count; one++)
            for (var other = one + 1; other < inked.Count; other++)
            {
                var shared = Geometry.Combine(inked[one], inked[other], GeometryCombineMode.Intersect, null);

                Assert.AreEqual(0, shared.GetArea(), 0.5,
                                $"{Said(words[one])} and {Said(words[other])} share ink");
            }
    });

    [TestMethod]
    public void EveryWordStaysInsideThePicture() => UiThread.Run(() =>
    {
        var laid = Lay("width: 400\nheight: 300\n" + Stack);

        // Half a pixel of slack: the picture is the union of the words' own rectangles, so the word on each
        // edge lands exactly on it and whether it is "inside" comes down to the last bit of a double.
        var picture = new Rect(laid.Size);
        picture.Inflate(0.5, 0.5);

        foreach (var word in Words(laid))
            Assert.IsTrue(picture.Contains(word.Bounds), $"{Said(word)} at {word.Bounds} is outside {picture}");
    });

    [TestMethod]
    public void TurnedWordsAreTurned() => UiThread.Run(() =>
    {
        var laid = Lay("rotate: 1\nminRotation: -90\nmaxRotation: -90\n" + Stack);

        foreach (var word in Words(laid))
            Assert.IsNotNull(word.Turned, $"{Said(word)} should have been set on its side");
    });

    [TestMethod]
    public void ALevelCloudTurnsNothing() => UiThread.Run(() =>
    {
        foreach (var word in Words(Lay("rotate: 0\n" + Stack)))
            Assert.IsNull(word.Turned);
    });

    // ── The picture ────────────────────────────────────────────────────────

    /// <summary>
    /// `width:` and `height:` are how much room the packing is given, not how big the picture is: what is
    /// drawn is trimmed to the words, so a dozen words in a wide column are a small picture rather than a
    /// dozen words in the middle of a field of nothing.
    /// </summary>
    [TestMethod]
    public void ThePictureIsTrimmedToWhatWasDrawn() => UiThread.Run(() =>
    {
        var laid = Lay("width: 360\nheight: 240\n" + Stack);

        Assert.IsTrue(laid.Size.Width is > 0 and <= 360, $"{laid.Size.Width} should be within the room given");
        Assert.IsTrue(laid.Size.Height is > 0 and <= 240, $"{laid.Size.Height} should be within the room given");

        var widest = Words(laid).Max(word => word.Bounds.Right);
        Assert.AreEqual(laid.Size.Width, widest, 1, "the picture ends where the widest word does");
    });

    /// <summary>
    /// A cloud told no size is given the column it sits in to pack into. It is a bound rather than a size —
    /// a handful of words pack as tightly as they would anywhere — so what this holds is that the picture
    /// never runs past the column, however wide or narrow it is.
    /// </summary>
    [TestMethod]
    public void ACloudWithNoSizeIsGivenTheColumnItIsIn() => UiThread.Run(() =>
    {
        foreach (var room in new double[] { 240, 480, 700 })
            Assert.IsTrue(Lay(Stack, room).Size.Width <= room, $"wider than the {room} column it is in");
    });

    // ── The shape it is packed into ────────────────────────────────────────

    /// <summary>
    /// A cloud packed into letters fills them and leaves the rest alone. An `L` is the shape to ask it with:
    /// whatever face it is set in, its top right corner is empty, and a cloud that ignored the stencil would
    /// put words there first — it is the side nearest the middle.
    /// </summary>
    [TestMethod]
    public void LettersShapeTheCloud() => UiThread.Run(() =>
    {
        var laid = Lay("letters: L\nminSize: 5\nmaxSize: 18\ngap: 1\n" + Many(), room: 700);
        var words = Words(laid);

        Assert.IsTrue(words.Length > 20, $"only {words.Length} word(s) were placed");

        var corner = new Rect(laid.Size.Width * 0.55, 0, laid.Size.Width * 0.45, laid.Size.Height * 0.55);

        foreach (var word in words)
            Assert.IsFalse(corner.Contains(word.Bounds),
                           $"{Said(word)} is in the corner an L does not reach");
    });

    [TestMethod]
    public void APictureThatCannotBeFoundStopsTheBlock() => UiThread.Run(() =>
    {
        // Not a cloud of the wrong shape and no word about it: a reader who named a mask meant it, and a
        // round cloud would look like the mask had simply not worked.
        var laid = Lay("mask: nowhere.png\n" + Stack);

        StringAssert.Contains(Text(laid, LayoutText.SourceKind), "mask: nowhere.png", "shown as written");
        StringAssert.Contains(laid.Trouble[0].Message, "nowhere.png");
    });

    [TestMethod]
    public void APictureIsTheShapeItsDarkPartMakes() => UiThread.Run(() =>
    {
        // Black on white, left half only — every word should land in that half.
        var laid = Laying.Lay("wordcloud", "mask: half.png\nminSize: 5\nmaxSize: 18\ngap: 1\n" + Many(), 600,
                              options: new DiagramRenderOptions { Palette = StyleFormat.Dark, Pictures = _ => Half() });

        Assert.IsTrue(Words(laid).Length > 20, $"only {Words(laid).Length} word(s) were placed");

        // Asked of the picture's proportions rather than of where each word sits, because what is drawn is
        // trimmed to the words: a square mask with only its left half dark leaves a cloud half as wide as it
        // is tall, and one that ignored the mask would leave a square.
        Assert.IsTrue(laid.Size.Width < laid.Size.Height * 0.75,
                      $"{laid.Size} is not the half the picture marks");
    });

    /// <summary>A picture whose left half is black and whose right half is white.</summary>
    private static ImageSource Half()
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 200, 200));
            drawing.DrawRectangle(Brushes.Black, null, new Rect(0, 0, 100, 200));
        }

        var picture = new RenderTargetBitmap(200, 200, 96, 96, PixelFormats.Pbgra32);
        picture.Render(visual);
        picture.Freeze();
        return picture;
    }

    /// <summary>Enough words for a shape to have something to fill.</summary>
    private static string Many()
    {
        var words = new List<string>();
        for (var at = 0; at < 160; at++) words.Add($"word{at}: {200 - at}");
        return string.Join("\n", words);
    }

    // ── When it will not read ──────────────────────────────────────────────

    [TestMethod]
    public void ABlockThatIsNotACloudIsShownAsWritten_AndSaysWhyUnderIt() => UiThread.Run(() =>
    {
        // Its lines are all a reader has left to work with, so they are what is shown — and this says why, under them.
        var laid = Lay("shape: blob\nWPF: 40");

        Assert.AreEqual("shape: blob\nWPF: 40", Text(laid, LayoutText.SourceKind), "there is no cloud to draw");
        Assert.AreEqual(1, laid.Trouble.Count);
        StringAssert.Contains(laid.Trouble[0].Message, "not a shape");
        StringAssert.Contains(Text(laid, SourceShown.Reason), "not a shape");
    });

    private static string Text(Laid laid, string kind) =>
        string.Concat(laid.Root.SelfAndDescendants().Where(piece => piece.Kind == kind)
            .SelectMany(piece => piece.Marks.ToArray()).OfType<TextMark>().Select(mark => mark.Glyphs.Text));

    [TestMethod]
    public void AWordThatWillNotReadIsMarkedInTheSource() => UiThread.Run(() =>
    {
        // A cloud is only read where it is drawn, so a line that is no word is put right where it is written.
        var laid = Lay("WPF: 40\nXAML: lots\nMVVM: 12");

        Assert.IsTrue(laid.ShowsSource);
        Assert.AreEqual(1, laid.Trouble.Count);
        Assert.AreEqual(DiagnosticSeverity.Error, laid.Trouble[0].Severity);
        Assert.AreEqual(1, laid.Root.SelfAndDescendants().Count(piece => piece.Kind == SourceShown.Unread), "that line marked");
    });

    [TestMethod]
    public void AnEmptyBlockSaysWhatItNeeds() => UiThread.Run(() =>
    {
        var laid = Lay("");

        StringAssert.Contains(laid.Trouble[0].Message, "word");
        StringAssert.Contains(Text(laid, SourceShown.Reason), "word", "said under what is written");
    });

    [TestMethod]
    public void AWordWithNoRoomLeftSaysSoRatherThanVanishingQuietly() => UiThread.Run(() =>
    {
        // Small picture, big words: what will not fit has to be said, because a word silently missing from
        // a cloud is a word the reader believes was never counted.
        var laid = Lay("width: 120\nheight: 100\nminSize: 40\nmaxSize: 60\nfit: false\n"
                     + "alpha: 10\nbravo: 10\ncharlie: 10\ndelta: 10\necho: 10");

        Assert.IsTrue(laid.Trouble.Any(trouble => trouble.Severity == DiagnosticSeverity.Warning),
                      "something should have been left out, and said so");
    });

    private static string Said(Piece word) => word.Words?.Glyphs.Text ?? word.Kind;
}
