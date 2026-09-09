using System.Linq;
using System.Windows.Media;
using Nexaflow.Maths.Latex;
using Nexaflow.Visuals.Text.Editing;
using WpfMath.Parsers;
using WpfMath.Rendering;
using XamlMath;
using XamlMath.Rendering;

// A using alias beats a using-namespace, so these win over XamlMath.Rendering's own Point/Size.
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;
using Vector = System.Windows.Vector;

namespace Nexaflow.Visuals.Text.Markdown.Latex;

/// <summary>
/// A typeset formula: the tree of what was drawn, and the ability to draw it again.
/// <para>
/// Typesetting happens once, in <see cref="Build"/> — fonts, glyph metrics and all. After that this holds
/// no reference to the typesetter at all: both what you can <em>ask</em> about the formula and what you
/// can <em>paint</em> of it come out of <see cref="Tree"/>. That is the strongest statement that the tree
/// is complete, and it is why the rules deciding what a drag selected or where an arrow key goes can be
/// exercised without a desktop.
/// </para>
/// </summary>
public sealed class LatexLayout
{
    private LatexLayout(LatexTree tree) => Tree = tree;

    /// <summary>What was drawn where — every question about the formula's shape goes here.</summary>
    public LatexTree Tree { get; }

    /// <summary>The source this was built from.</summary>
    public string Latex => Tree.Latex;

    /// <summary>The formula's painted size in element pixels.</summary>
    public Size Size => Tree.Size;

    /// <summary>
    /// Typesets <paramref name="latex"/> and records where every piece landed, or returns null when it
    /// will not parse — which the caller shows as source rather than as a formula.
    /// </summary>
    /// <param name="shownAsWritten">
    /// A stretch to set as the characters written rather than read as maths — the piece being edited,
    /// which has to be seen exactly as typed while the formula around it stays typeset.
    /// <para>
    /// It goes through the typesetter rather than being painted over the top afterwards, which is the
    /// only way the rest of the formula can be laid out knowing it is there. Painted over, a stretch of
    /// any length in the middle of a formula simply covered whatever followed it.
    /// </para>
    /// </param>
    /// <param name="placeholders">
    /// Whether an argument or table cell left empty is given a hole to stand in it. Asked for by a
    /// surface being written on, where the hole is how the reader sees there is something still to
    /// write and how they aim at it. Off by default, because a box in the middle of a formula that is
    /// only being read would simply be wrong, and reading is the commoner case.
    /// </param>
    public static LatexLayout? Build(string latex, double scale, bool inline = false, string systemFont = "Arial",
                                     LatexRawZone? shownAsWritten = null, bool placeholders = false)
    {
        if (string.IsNullOrEmpty(latex)) return null;

        try
        {
            // The tables, not the reader. Everything below builds from our own reading; what this is
            // asked for is what a name means to a typesetter — which symbol, which face, which command
            // it has a drawing for.
            var knowledge = WpfTeXFormulaParser.Instance;

            var editing = shownAsWritten is { } zone && zone.Length > 0
                ? (zone.Start, zone.Length)
                : ((int, int)?)null;

            // One reading, every time, whatever the caret is doing. What cannot be drawn and what is
            // being typed are both settled before this — they come back as pieces that say they are to
            // be shown rather than read, and the builder sets them as characters without having to know
            // which of the two it is looking at.
            // Asked of the builder, because the builder is what draws. It was asked of the tables, which
            // describe what the engine's own parser could read — and being a different question, it came back
            // a different answer: a `\ ` the builder sets directly was shown as its own characters in red.
            var read = TexPipeline.Read(
                latex, name => XamlMath.TexFormulaBuilder.Draws(name, knowledge), editing, placeholders);
            var reading = TexReading.Of(read);
            var formula = XamlMath.TexFormulaBuilder.Build(reading.Root, knowledge);

            if (formula is null)
            {
                // Nothing here could set it as maths, so it is set as it was typed. Which is the same
                // answer this gives a stretch under the caret and a command nobody has heard of, reached
                // by the same road — and a great deal more use to whoever wrote it than a blank space.
                reading = TexReading.Of(TexNode.Branch(TexKind.Sequence, [TexNode.Shown(latex)]));
                formula = XamlMath.TexFormulaBuilder.Build(reading.Root, knowledge);
            }

            if (formula is null) return null;

            var environment = WpfTeXEnvironment.Create(
                style: inline ? TexStyle.Text : TexStyle.Display,
                scale: scale,
                systemTextFontName: systemFont);

            var capture = new LatexLayoutCapture(scale, reading);
            formula.RenderTo(capture, environment, 0, 0);

            // …which also settles the tree onto the origin. A shifted or transformed box can land above or
            // left of where the pen started, and a tree with negative coordinates would put the caret outside
            // the control that draws it — one number now, because everything in it is relative to the root.
            capture.FinishRendering();
            if (capture.Tree is not { } laid) return null;

            // Asked of the tree rather than collected on the way through it. A piece that could not be
            // read carries the reason it could not, so there is one place the answer lives and no second
            // list to fall out of step with it — and a piece being typed carries nothing, which is how
            // it draws without being complained about.
            var trouble = reading.Root.SelfAndDescendants()
                .Where(part => part.Node.Trouble is not null)
                .Select(part => TexSourcePart.Trouble(part, DiagnosticSeverity.Error, part.Node.Trouble!))
                .Concat(formula.Ignored.Select(part => TexSourcePart.Trouble(
                    part,
                    DiagnosticSeverity.Warning,
                    "This was read, and nothing here knows how to draw it.")))
                .ToList();

            return new LatexLayout(new LatexTree(latex, reading, new Laid(laid, capture.Size, trouble)));
        }
        catch
        {
            // Every reading failure is the same answer to the caller: there is no formula to map.
            return null;
        }
    }

    /// <summary>
    /// Paints the formula into <paramref name="dc"/> in the tree's own coordinates, so a caret or a
    /// selection wash drawn from it lands exactly where the glyphs did.
    /// <para>
    /// The picture is walked out of the tree rather than typeset again. That is what makes the tree
    /// trustworthy: structure, geometry and drawing all came out of the one pass, so they cannot disagree
    /// about where anything is. It also means a single term can be painted on its own — see
    /// <paramref name="subtree"/> — which is what a caret blink or a term-by-term reveal needs, and why
    /// the caller no longer has to cache the whole formula as one drawing to stay affordable.
    /// </para>
    /// <para>
    /// The foreground is passed per paint because it is the theme's, and the theme can change without the
    /// formula doing so. Only marks the formula gave no colour of its own take it; a <c>	extcolor</c>
    /// keeps what it asked for.
    /// </para>
    /// <para>
    /// Two layers, in the typesetter's own order: every wash goes down first and then all the ink over it,
    /// so a <c>\colorbox</c> behind one term cannot paint over the glyphs of another. That is a question
    /// about marks, which is why the shared painter can answer it and this no longer walks the tree itself.
    /// </para>
    /// </summary>
    /// <param name="subtree">One piece to paint, or nothing for the whole formula.</param>
    public void Paint(DrawingContext dc, Brush foreground, Piece subtree = default)
    {
        var from = subtree.Exists ? subtree : Tree.Root;
        if (!from.Exists) return;

        LayoutPainter.PaintOne(dc, from, foreground, mark => mark is WashMark);

        var ink = new DrawingGroup();
        using (var layer = ink.Open()) LayoutPainter.PaintOne(layer, from, foreground, mark => mark is not WashMark);
        ink.Freeze();
        dc.DrawDrawing(ink);
    }
}
