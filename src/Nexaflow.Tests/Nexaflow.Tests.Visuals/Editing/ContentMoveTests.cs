using System.Collections.Generic;
using System.Linq;
using System.Windows;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Prose;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Prose;

namespace Nexaflow.Tests.Visuals.Editing;

/// <summary>
/// The seam a move is told through, and what the engine does with one where the language says nothing.
///
/// <para>
/// Moving is the engine's: pressing on what is picked out, carrying it, laying the content out as it would read after
/// the drop, and letting it go. What it comes to by default is the characters carried, emptied from where they were
/// and written in at the drop — which is right for anything whose source is what a reader sees. A language says
/// otherwise only when what is carried is something of its own, and then it says it the same way it says what an edit
/// is: which stretches to write, and what goes in them.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[DoNotParallelize]
[CoversNode("markdown-text")]
public class ContentMoveTests
{
    private const string Source = "alpha beta gamma\n";

    [TestMethod]
    public void AWordCarriedToAnotherPlaceMovesItsCharacters() => UiThread.Run(() =>
    {
        var element = Element();
        var words = Words(element);

        // Picked out by a drag across it, then pressed on and carried — which is the one gesture, told apart by whether
        // the press landed on what was already picked out.
        Sweep(element, Left(words), Middle(words, "alpha".Length));
        var picked = Picked(element);

        Assert.IsTrue(picked.Length > 0, "the sweep picked something out to carry");

        Sweep(element, Middle(words, 2), Right(words));
        element.UpdateLayout();

        // Said without depending on exactly which letter the pointer landed between: a move writes the characters
        // carried and nothing else, so the same characters are there and they are not where they were.
        Assert.AreEqual(Source.Length, element.Source.Length, $"a move invents none and loses none: {element.Source}");
        CollectionAssert.AreEquivalent(Source.ToCharArray(), element.Source.ToCharArray(), "the same characters");
        Assert.AreNotEqual(Source, element.Source, "somewhere else");
    });

    [TestMethod]
    public void AndNothingPickedOutIsNothingToCarry() => UiThread.Run(() =>
    {
        var element = Element();
        var words = Words(element);

        Sweep(element, Left(words), Right(words));
        element.UpdateLayout();

        Assert.AreEqual(Source, element.Source, "a press that picked nothing out wrote nothing");
    });

    // ── What a move says of itself ──────────────────────────────────────────

    [TestMethod]
    public void AMoveSaysWhatIsCarriedRatherThanLeavingItToBeLookedUp()
    {
        var move = Carrying("alpha beta gamma\n", [new EditRange(6, 4)], to: 0);

        Assert.AreEqual("beta", move.Text);
        Assert.IsTrue(move.Holds, "carried out of the language's own source");
    }

    [TestMethod]
    public void AndSeveralStretchesAreCarriedInTheOrderTheyWereWritten()
    {
        var move = Carrying("alpha beta gamma\n", [new EditRange(11, 5), new EditRange(0, 5)], to: 6);

        Assert.AreEqual("gammaalpha", move.Text, "in the order they were given, which is the order the engine sorts them into");
    }

    [TestMethod]
    public void AndWhichOfItsOwnPartsTheyCoverWhole()
    {
        // Whole, and that is the distinction that matters: a column of a matrix and a slice of a pie are parts a stretch
        // covers exactly, which is what makes a move of them something other than a move of characters. A stretch across
        // the middle of a run of words covers no part, and such a move is the characters and nothing more.
        const string source = "alpha *beta* gamma\n";
        var at = source.IndexOf('*', StringComparison.Ordinal);

        var whole = Carrying(source, [new EditRange(at, "*beta*".Length)], to: 0);
        var partly = Carrying(source, [new EditRange(0, 3)], to: 13);

        Assert.IsTrue(whole.Carrying().Any(part => part.Print() == "*beta*"), "the construct the stretch covers exactly");
        Assert.AreEqual(0, partly.Carrying().Count, "and nothing at all for a stretch across the middle of a run of words");
    }

    [TestMethod]
    public void AndThatWhatIsCarriedCameFromSomewhereElse()
    {
        // The stretch is outside the root the move was told, which is how a language tells something of its own from
        // something arriving out of the prose around it.
        var move = Carrying("alpha\n", [new EditRange(40, 3)], to: 0);

        Assert.IsFalse(move.Holds);
    }

    /// <summary>
    /// A move of <paramref name="carried"/> to <paramref name="to"/>, told against a reading of <paramref name="source"/>
    /// — both passes of it, because what a stretch covers is a question about the words and not about the blocks.
    /// </summary>
    private static ContentMove Carrying(string source, IReadOnlyList<EditRange> carried, int to)
    {
        var root = ContentPart.Of(MarkdownParser.Parsing()(source).Tree);

        return new ContentMove(carried, to, new Landing(new EditState(source, to), Laid.Nothing, -1), default, root, root);
    }

    private static MarkdownElement Element()
    {
        var element = new MarkdownElement(Source, StyleFormat.Dark);

        element.Measure(new Size(400, double.PositiveInfinity));
        element.Arrange(new Rect(0, 0, 400, element.DesiredSize.Height));
        element.UpdateLayout();

        return element;
    }

    /// <summary>The piece the words of the one paragraph were drawn as.</summary>
    private static Piece Words(MarkdownElement element) =>
        element.Laid.Root.SelfAndDescendants().First(piece => piece.Words is { Maps: true });

    /// <summary>A press, a drag and a release, which is one gesture whether it picks out or carries.</summary>
    private static void Sweep(MarkdownElement element, Point from, Point to)
    {
        element.BeginPointerSelect(from);
        element.ExtendPointerSelect(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2));
        element.ExtendPointerSelect(to);
        element.EndPointerSelect();
    }

    private static string Picked(MarkdownElement element) => element.SelectedText;

    private static Point Left(Piece piece) => new(piece.Bounds.X + 1, piece.Bounds.Y + (piece.Bounds.Height / 2));

    private static Point Right(Piece piece) =>
        new(piece.Bounds.Right - 1, piece.Bounds.Y + (piece.Bounds.Height / 2));

    /// <summary>A point about <paramref name="letters"/> letters along the run, which is enough to land inside a word.</summary>
    private static Point Middle(Piece piece, int letters) =>
        new(piece.Bounds.X + (piece.Bounds.Width * letters / Source.TrimEnd('\n').Length),
            piece.Bounds.Y + (piece.Bounds.Height / 2));
}
