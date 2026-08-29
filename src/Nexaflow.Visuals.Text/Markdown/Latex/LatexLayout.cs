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
    /// What a painter must translate by so its output lands in the tree's coordinates. A box may be
    /// laid out above or left of the origin, and the tree is normalised so it never is.
    /// </summary>
    public Vector PaintOffset { get; private init; }

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
            var read = TexPipeline.Read(latex, knowledge.Draws, editing, placeholders);
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

            var capture = new LatexLayoutCapture(scale, latex);
            formula.RenderTo(capture, environment, 0, 0);
            capture.FinishRendering();
            if (capture.Root is not { } root) return null;

            // Normalise: a shifted or transformed box can land above or left of the origin, and a tree
            // with negative coordinates would put the caret outside the control that draws it.
            var union = Extent(root);
            var offset = new Vector(-union.X, -union.Y);
            Settle(root, offset);
            Own(root);

            // Asked of the tree rather than collected on the way through it. A piece that could not be
            // read carries the reason it could not, so there is one place the answer lives and no second
            // list to fall out of step with it — and a piece being typed carries nothing, which is how
            // it draws without being complained about.
            var trouble = reading.Root.SelfAndDescendants()
                .Where(part => part.Node.Trouble is not null)
                .Select(part => Diagnostic.Of(part, DiagnosticSeverity.Error, part.Node.Trouble!))
                .Concat(formula.Ignored.Select(part => Diagnostic.Of(
                    part,
                    DiagnosticSeverity.Warning,
                    "This was read, and nothing here knows how to draw it.")))
                .ToList();

            var tree = new LatexTree(latex, root, new Size(union.Width, union.Height), trouble);
            return new LatexLayout(tree) { PaintOffset = offset };
        }
        catch
        {
            // Every reading failure is the same answer to the caller: there is no formula to map.
            return null;
        }
    }

    /// <summary>
    /// The same thing <see cref="Attribute"/> does, for a formula built from the reading — where it is
    /// a hand-over rather than a search, because every box came out of an atom that already knew.
    /// <para>
    /// This is what the exercise was for. <see cref="Attribute"/> exists only because the boxes were
    /// built by somebody else's reading of the same LaTeX, so the one thing the two shared was offsets
    /// and matching on them needed rules. Here there is nothing to match: the whole of it is a walk and
    /// an assignment, and it is right by construction rather than by rule.
    /// </para>
    /// </summary>
    private static void Own(LayoutNode root)
    {
        foreach (var node in root.SelfAndDescendants())
            if (node is LatexNode piece)
                piece.Part = piece.Formula?.Origin;
    }

    /// <summary>
    /// How much of the page the formula actually covers. Spacing is left out: a strut is as tall as the
    /// line it reserves room on, so counting it would pad the element with margin nothing is drawn in.
    /// </summary>
    private static Rect Extent(LayoutNode root)
    {
        var union = Rect.Empty;
        foreach (var node in root.SelfAndDescendants())
            if (node.Kind is not ("StrutBox" or "GlueBox"))
                union.Union(node.Bounds);

        return union.IsEmpty ? new Rect(0, 0, 0, 0) : union;
    }

    /// <summary>Moves the tree onto the origin, so nothing sits at a negative coordinate.</summary>
    private static void Settle(LayoutNode root, Vector offset)
    {
        foreach (var node in root.SelfAndDescendants())
        {
            if (node is not LayoutNode moving) continue;

            var bounds = moving.Bounds;
            bounds.Offset(offset);
            moving.Bounds = bounds;
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
    /// formula doing so. Only marks the formula gave no colour of its own take it; a <c>\textcolor</c>
    /// keeps what it asked for.
    /// </para>
    /// </summary>
    /// <param name="subtree">One piece to paint, or null for the whole formula.</param>
    public void Paint(DrawingContext dc, Brush foreground, ILayoutNode? subtree = null)
    {
        if ((subtree ?? Tree.Root) is not LatexNode from) return;

        dc.PushTransform(new TranslateTransform(PaintOffset.X, PaintOffset.Y));
        try
        {
            // A piece painted on its own still has to be placed by whatever encloses it.
            var outer = 0;
            foreach (var ancestor in from.Ancestors().Reverse().OfType<LatexNode>())
                foreach (var transform in ancestor.Transforms)
                {
                    dc.PushTransform(transform);
                    outer++;
                }

            // Two layers, in WpfMath's own order: every wash goes down first, then all the ink over it, so
            // a \colorbox behind one term cannot paint over the glyphs of another.
            PaintWashes(dc, from);

            var ink = new DrawingGroup();
            using (var layer = ink.Open()) PaintMarks(layer, from, foreground);
            ink.Freeze();
            dc.DrawDrawing(ink);

            for (var i = 0; i < outer; i++) dc.Pop();
        }
        finally { dc.Pop(); }
    }

    private static void PaintWashes(DrawingContext dc, LatexNode node)
    {
        if (node.Guidelines is not null) dc.PushGuidelineSet(node.Guidelines);

        if (node.Background is not null)
            dc.DrawRectangle(node.Background, null, node.BackgroundBounds);

        foreach (var child in node.Children.OfType<LatexNode>()) PaintWashes(dc, child);

        if (node.Guidelines is not null) dc.Pop();
    }

    private static void PaintMarks(DrawingContext dc, LatexNode node, Brush foreground)
    {
        foreach (var transform in node.Transforms) dc.PushTransform(transform);
        if (node.Guidelines is not null) dc.PushGuidelineSet(node.Guidelines);

        foreach (var mark in node.Marks) mark.PaintOn(dc, foreground);
        foreach (var child in node.Children.OfType<LatexNode>()) PaintMarks(dc, child, foreground);

        if (node.Guidelines is not null) dc.Pop();
        for (var i = 0; i < node.Transforms.Count; i++) dc.Pop();
    }
}
