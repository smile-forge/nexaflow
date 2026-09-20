using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using ContentElement = Nexaflow.Visuals.Text.Editing.ContentElement;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// What every diagram's builder has to keep, checked the same way for each. A diagram's builder tests derive from this and
/// hand it the blocks it draws; every check here runs over them, and the tests the class adds itself are only about what that
/// diagram looks like.
///
/// <para>
/// The checks are the ones a reader relies on whatever the diagram: it draws — read, written in, wide and narrow — without
/// falling over; it keeps drawing while every character of it is typed; everything it draws stands for a stretch of what
/// was written; the words a caret can go into are the very characters written; and it is what the Markdown renderer shows.
/// <see cref="MermaidBuilderRulesTests"/> fails for a builder with no tests deriving from this.
/// </para>
/// </summary>
public abstract class MermaidBuilderContract
{
    /// <summary>What a builder that fell over says instead of drawing — see <c>ContentBuilder.Lay</c>.</summary>
    private const string FellOver = "This could not be set";

    /// <summary>The diagram this builder draws.</summary>
    public abstract MermaidDiagram Diagram { get; }

    /// <summary>Blocks that draw every part of the diagram, and blocks half written — the record of what the builder draws.</summary>
    protected abstract IEnumerable<(string What, string Source)> Drawn { get; }

    /// <summary>The language a block of this diagram is fenced with — a language of its own, or Mermaid's.</summary>
    public virtual string Language => "mermaid";

    /// <summary>
    /// The builder under test: the one its diagram names, or, for a language of its own, the one it says itself.
    /// </summary>
    internal virtual MermaidBuilders.Build Builder =>
        MermaidBuilders.For(Diagram)
        ?? throw new AssertFailedException($"{Diagram} is drawn by no builder: MermaidBuilders names none.");

    /// <summary>Lays a block out as the shared renderer would.</summary>
    protected Laid Lay(string source, double room = 700, bool writing = false) =>
        Builder.Invoke(EditState.For(source), MarkdownPalette.Dark, 1.0, room, writing);

    [TestMethod]
    public void EveryBlockDrawsReadOrWritten_WideOrNarrow() => UiThread.Run(() =>
    {
        foreach (var (what, source) in Drawn)
            foreach (var room in new[] { double.PositiveInfinity, 700, 240 })
                foreach (var writing in new[] { false, true })
                {
                    var laid = Lay(source, room, writing);
                    Assert.IsFalse(laid.Trouble.Any(diagnostic => diagnostic.Message.StartsWith(FellOver, StringComparison.Ordinal)),
                                   $"{what} (room {room}, writing {writing}): {string.Join(" | ", laid.Trouble.Select(d => d.Message))}");
                    Assert.IsTrue(laid.Size.Width > 0 && laid.Size.Height > 0, $"{what} (room {room}, writing {writing}) takes room");
                }
    });

    [TestMethod]
    public void EveryBlockKeepsDrawingWhileItIsTyped() => UiThread.Run(() =>
    {
        foreach (var (what, source) in Drawn)
        {
            // Every character of a short block; of a long one, enough of them to cross every line half written.
            var stride = Math.Max(1, source.Length / 120);
            for (var length = 0; length <= source.Length; length += stride)
            {
                var laid = Lay(source[..length], writing: true);
                Assert.IsFalse(laid.Trouble.Any(diagnostic => diagnostic.Message.StartsWith(FellOver, StringComparison.Ordinal)),
                               $"{what}: after {length} character(s): {string.Join(" | ", laid.Trouble.Select(d => d.Message))}");
            }
        }
    });

    [TestMethod]
    public void EverythingDrawnStandsForWhatWasWritten() => UiThread.Run(() =>
    {
        foreach (var (what, source) in Drawn)
            foreach (var piece in Lay(source, writing: true).Root.SelfAndDescendants())
            {
                if (piece.Part is not { } part) continue;
                Assert.IsTrue(part.Start >= 0 && part.Start + part.Length <= source.Length,
                              $"{what}: a {piece.Kind} stands for {part.Start}+{part.Length}, outside the {source.Length} characters written");
            }
    });

    [TestMethod]
    public void WordsACaretGoesIntoAreTheCharactersWritten() => UiThread.Run(() =>
    {
        foreach (var (what, source) in Drawn)
            foreach (var piece in Lay(source, writing: true).Root.SelfAndDescendants())
            {
                if (piece is not { Words: { Maps: true } words, Part: { } part }) continue;
                Assert.AreEqual(source.Substring(part.Start, part.Length), words.Glyphs.Text,
                                $"{what}: a {piece.Kind} a caret goes into says what was written there");
            }
    });

    [TestMethod]
    public void ItIsWhatTheMarkdownRendererShows() => UiThread.Run(() =>
    {
        var (what, source) = Drawn.First();
        var element = DiagramRenderer.Render(Language, source, MarkdownPalette.Dark);

        Assert.IsInstanceOfType<ContentElement>(element, $"{what}: drawn on the shared layout tree");

        var block = (ContentElement)element;
        block.Measure(new Size(700, double.PositiveInfinity));
        block.Arrange(new Rect(block.DesiredSize));
        Assert.IsTrue(block.Picture().PixelWidth > 0, $"{what}: and has a picture to copy");
    });

    // ── What a diagram's own tests ask of what it drew ──────────────────────

    /// <summary>Every piece of a kind that was drawn, in the order it was drawn.</summary>
    protected static List<Piece> Pieces(Laid laid, string kind) => [.. laid.Root.SelfAndDescendants().Where(piece => piece.Kind == kind)];

    /// <summary>What a piece stands for, as it was written — empty for a piece standing for nothing.</summary>
    protected static string Written(string source, ISourcePart? part) => part is null ? "" : source.Substring(part.Start, part.Length);

    /// <summary>The middle of what a piece takes up, which is where it is for the purposes of saying what is beside what.</summary>
    protected static Point Middle(Rect rect) => new(rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2));

    /// <summary>The colour a piece is drawn in — the first fill under it — or null where nothing under it is filled.</summary>
    protected static Color? Fill(Piece piece) =>
        piece.SelfAndDescendants().SelectMany(inner => inner.Marks.ToArray()).OfType<GeometryMark>()
            .Select(mark => mark.Fill).OfType<SolidColorBrush>().Select(brush => (Color?)brush.Color).FirstOrDefault();
}
