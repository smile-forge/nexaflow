using System.Collections.Generic;
using System.Linq;
using System.Windows;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Prose;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Prose;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals.Markdown.Prose;

/// <summary>
/// Words broken over several lines.
///
/// <para>
/// Each line is drawn as a piece of its own, and each stands for its own stretch of the source rather than the whole run it
/// was cut from — so a press on the third line means a letter on the third line, and a drag along one picks out what is on
/// it. Escaped characters break a sentence into several runs, which is where this showed first, but a plain paragraph is
/// broken the same way.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[DoNotParallelize]
[CoversNode("markdown-text")]
public class WrappedWordsTests
{
    private const string Plain =
        "Equations in which derivatives appear describe numerous laws of nature. The sought-after function plays, for "
        + "example, the role of a physical or chemical quantity that depends on time.\n";

    private const string Escaped =
        "Equations in which \\<derivative>derivatives\\</derivative>s appear. Such equations describe numerous laws of nature. "
        + "The sought-after \\<function>function\\</function> plays, for example, the role of a physical or chemical quantity "
        + "that depends on time. This applies, for example, to the\n";

    [TestMethod]
    public void EachLineOfAWrappedRunStandsForWhatIsDrawnOnIt() => UiThread.Run(() =>
    {
        var lines = Lines(Element(Plain));

        Assert.IsTrue(lines.Count >= 3, $"wrapped over several lines: {lines.Count}");

        foreach (var line in lines)
        {
            var sits = line.Sits();

            Assert.AreEqual(line.Words!.Glyphs.Text, Plain.Substring(sits.Start, sits.Length),
                "the source a line stands for is the words drawn on it");
        }
    });

    [TestMethod]
    public void APressOnALaterLineMeansALetterOnThatLine() => UiThread.Run(() =>
    {
        var element = Element(Plain);

        foreach (var line in Lines(element))
        {
            var sits = line.Sits();
            var offset = element.Laid.OffsetAt(Along(line, 0.5).ToLayout());

            Assert.IsTrue(offset > sits.Start && offset < sits.End,
                $"pressed half way along '{line.Words!.Glyphs.Text}' and landed at {offset}, outside {sits.Start}..{sits.End}");
        }
    });

    [TestMethod]
    public void ADragAlongALineOfEscapedWordsPicksOutLettersOfThatLine() => UiThread.Run(() =>
    {
        var element = Element(Escaped);
        var lines = Lines(element).Where(line => line.Words!.Glyphs.Text.Length > 20).ToList();

        Assert.IsTrue(lines.Count >= 2, "several lines long enough to drag along");

        foreach (var line in lines)
        {
            Sweep(element, Along(line, 0.3), Along(line, 0.4));

            var drawn = line.Words!.Glyphs.Text;
            var picked = element.SelectedText;

            Assert.IsTrue(picked.Length > 0 && picked.Length < drawn.Length, $"a few letters, not the whole run: '{picked}'");
            StringAssert.Contains(drawn, picked, "and they are the ones on the line dragged along");
        }
    });

    [TestMethod]
    public void ASliceOfAPartIsReadAsThePartItIsCutFrom()
    {
        var root = ContentPart.Of(MarkdownParser.Parsing()("Some words.\n").Tree);
        var words = root.SelfAndDescendants().First(part => part.Children.Count == 0);

        Assert.AreSame(words, words.TreePart());
        Assert.AreSame(words, new PartSlice(words, 1, 2).TreePart());
        Assert.AreSame(words, new PartSlice(new PartSlice(words, 1, 3), 1, 1).TreePart(), "through as many cuts as there are");
        Assert.IsNull(new PartRun([words, root]).TreePart(), "several parts are no one of them");
        Assert.IsNull(new SourceSpan(0, 3).TreePart(), "nor is a stretch no part names");
    }

    private static MarkdownElement Element(string source)
    {
        var element = new MarkdownElement(source, WpfStyles.Dark);

        element.Measure(new Size(360, double.PositiveInfinity));
        element.Arrange(new Rect(0, 0, 360, element.DesiredSize.Height));
        element.UpdateLayout();

        return element;
    }

    /// <summary>Every piece drawing words as they were written, in the order they are read.</summary>
    private static List<LayoutPiece> Lines(MarkdownElement element) =>
        [.. element.Laid.Root.SelfAndDescendants().Where(piece => piece.Words is { Maps: true })];

    private static Point Along(LayoutPiece piece, double fraction) =>
        new(piece.Bounds.X + (piece.Bounds.Width * fraction), piece.Bounds.Y + (piece.Bounds.Height / 2));

    /// <summary>A press, a drag and a release.</summary>
    private static void Sweep(MarkdownElement element, Point from, Point to)
    {
        element.BeginPointerSelect(from);
        element.ExtendPointerSelect(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2));
        element.ExtendPointerSelect(to);
        element.EndPointerSelect();
    }
}
