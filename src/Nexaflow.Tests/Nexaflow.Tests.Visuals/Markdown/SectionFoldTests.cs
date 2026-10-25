using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Prose;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// A document's sections fold away and open again: a heading's corner offers to fold the section under it, what is folded is
/// off the page with a marker under the heading saying how much is behind it, and a press on the marker — or anything that has
/// to show a place inside — opens it again. What is folded is the engine's, never written into the source.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("markdown-section-fold")]
public class SectionFoldTests
{
    private const string Document =
        "Opening words.\n\n# Pets\n\nDogs bark.\n\n## Cats\n\nCats purr.\n\n# Plants\n\nFerns grow.\n\n# Empty\n";

    [TestMethod]
    public void AHeadingWithSomethingUnderIt_OffersToFoldIt_AndNothingElseOffersAnything() => UiThread.Run(() =>
    {
        var (engine, _) = LaidOut(Document);

        CollectionAssert.AreEqual(new[] { LayoutVerbs.Collapse }, Verbs(engine, Middle(Where(engine, "# Pets"))));
        Assert.AreEqual(0, Verbs(engine, Middle(Where(engine, "# Empty"))).Count, "a heading with nothing under it has nothing to fold");
        Assert.AreEqual(0, Verbs(engine, Middle(Where(engine, "Dogs bark"))).Count, "and prose is still read, not handled");
    });

    [TestMethod]
    public void FoldingASection_TakesItsBlocksOffThePage_AndLeavesAMarkerSayingHowManyWaitBehindIt() => UiThread.Run(() =>
    {
        var (engine, _) = LaidOut(Document);

        FoldAt(engine, "# Pets");
        var words = Words(engine);

        Assert.IsFalse(words.Any(said => said.Contains("Dogs") || said.Contains("Cats")), "the section and the one under it are folded away");
        Assert.IsTrue(words.Any(said => said.Contains("Pets")), "while its heading stays");
        Assert.IsTrue(words.Any(said => said.Contains("Ferns")), "and the next section is untouched");
        Assert.AreEqual("+3", Says(Marker(engine)), "a paragraph, a heading and a paragraph wait behind it");
        Assert.AreEqual(Document, engine.State.Source, "folding writes nothing into the source");
    });

    [TestMethod]
    public void PressingTheMarker_OpensTheSectionAgain() => UiThread.Run(() =>
    {
        var (engine, _) = LaidOut(Document);

        FoldAt(engine, "# Pets");
        engine.Input(new ContentPress(Middle(Marker(engine).Bounds)));

        Assert.IsTrue(Words(engine).Any(said => said.Contains("Cats purr")), "what was folded is back on the page");
        Assert.IsFalse(Pieces(engine, PortablePieces.Fold).Any(), "and the marker is gone");
    });

    [TestMethod]
    public void AFoldedSection_StaysFolded_WhenTheDocumentIsLaidAgain() => UiThread.Run(() =>
    {
        var (engine, _) = LaidOut(Document);

        FoldAt(engine, "# Plants");
        engine.Relay();

        Assert.IsFalse(Words(engine).Any(said => said.Contains("Ferns")), "the engine keeps what the reader folded, by the heading's name");
    });

    [TestMethod]
    public void ShowingAPlaceInsideAFoldedSection_OpensItFirst() => UiThread.Run(() =>
    {
        var (engine, element) = LaidOut(Document);

        FoldAt(engine, "# Pets");
        var cats = Document.IndexOf("Cats purr", StringComparison.Ordinal);

        Assert.IsTrue(element.Show((cats, 4)), "a hit inside a folded section is still somewhere to show");
        Assert.IsTrue(Words(engine).Any(said => said.Contains("Cats purr")), "because the section holding it was opened");
    });

    /// <summary>Folds the section under the heading written as <paramref name="heading"/>, through its corner.</summary>
    private static void FoldAt(ContentEngine engine, string heading)
    {
        engine.Input(new ContentHover(Middle(Where(engine, heading))));
        var fold = Buttons(engine).Single(button => button.Acts!.Click!.Verb == LayoutVerbs.Collapse);

        engine.Input(new ContentPress(Middle(fold.Bounds)));
        engine.Input(new ContentHover(null));
    }

    /// <summary>The one fold marker on the page.</summary>
    private static PortablePiece Marker(ContentEngine engine) => Pieces(engine, PortablePieces.Fold).Single();

    /// <summary>What a marker says, as drawn.</summary>
    private static string Says(PortablePiece marker) => string.Concat(marker.Marks.ToArray().OfType<PortableTextMark>().Select(mark => mark.Glyphs.Text));

    private static IEnumerable<PortablePiece> Pieces(ContentEngine engine, string kind) =>
        engine.Laid.Root.SelfAndDescendants().Where(piece => piece.Kind == kind);

    /// <summary>Every run of words drawn on the page.</summary>
    private static List<string> Words(ContentEngine engine) =>
        [.. engine.Laid.Root.SelfAndDescendants().Select(piece => piece.Words?.Glyphs.Text).OfType<string>()];

    /// <summary>The corner's buttons, in the order they stand.</summary>
    private static List<PortablePiece> Buttons(ContentEngine engine) =>
        engine.Corner is null ? [] : [.. engine.Corner.Root.SelfAndDescendants().Where(piece => piece.Acts is not null)];

    private static List<string> Verbs(ContentEngine engine, LayoutPoint at)
    {
        engine.Input(new ContentHover(at));

        return [.. Buttons(engine).Select(button => button.Acts!.Click!.Verb)];
    }

    /// <summary>The document laid out as a reader is shown it, and the engine that laid it.</summary>
    private static (ContentEngine Engine, MarkdownElement Element) LaidOut(string document)
    {
        var engine = new ContentEngine();
        var element = new MarkdownElement(document, WpfStyles.Dark, null, engine);

        element.Measure(new Size(600, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));

        return (engine, element);
    }

    /// <summary>Where the block holding <paramref name="written"/> came out on the page.</summary>
    private static LayoutRect Where(ContentEngine engine, string written) =>
        engine.Where(engine.Blocked(Document.IndexOf(written, StringComparison.Ordinal))!);

    private static LayoutPoint Middle(LayoutRect where) => new(where.X + (where.Width / 2), where.Y + (where.Height / 2));
}
