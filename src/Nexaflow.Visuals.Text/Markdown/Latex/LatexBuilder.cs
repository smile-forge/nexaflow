using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Latex;
using Nexaflow.Visuals.Text.Editing;
using WpfMath.Parsers;
using WpfMath.Rendering;
using XamlMath;
using XamlMath.Rendering;
using System.Collections.Generic;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Visuals.Text.Markdown.Latex;

/// <summary>
/// The one thing that knows a formula is a formula.
///
/// <para>
/// It reads LaTeX, typesets it, and lays the result out as pieces. Everything after that — painting,
/// hit-testing, where a caret goes, what a drag took, what a selection washes — is the same code a tune
/// and a barcode run, over the <see cref="Laid"/> this hands back. That is the whole of the arrangement:
/// a builder per kind of content, and nothing per kind of content anywhere else.
/// </para>
/// <para>
/// There is deliberately no <c>LatexLayout</c> any more. It held a tree, forwarded the size and the
/// source to it, and offered a <c>Paint</c> that was the shared painter with a default argument — three
/// objects wrapping one, and the outer two are why a formula could not be hosted by the same element as
/// a score.
/// </para>
/// </summary>
public sealed class LatexBuilder : ContentBuilder
{
    private readonly double _scale;
    private readonly bool _inline;
    private readonly string _systemFont;
    private readonly RawZone? _shownAsWritten;
    private readonly bool _placeholders;
    private readonly double _pixelsPerDip;
    private readonly double _block;

    private LatexBuilder(string latex, double scale, bool inline, string systemFont,
                         RawZone? shownAsWritten, bool placeholders, double pixelsPerDip, double block)
        : base(latex)
    {
        _scale = scale;
        _inline = inline;
        _systemFont = systemFont;
        _shownAsWritten = shownAsWritten;
        _placeholders = placeholders;
        _pixelsPerDip = pixelsPerDip;
        _block = block;
    }

    /// <summary>
    /// Typesets <paramref name="latex"/> and records where every piece landed.
    ///
    /// <para>
    /// <strong>Always a formula.</strong> Source that will not read comes back as its own characters with
    /// a wave under it, and so does source that made the typesetter throw; empty source comes back as an
    /// empty line with somewhere to put the caret. There is no answer meaning "there is nothing here",
    /// because a caller given one has to grow a second way of being a formula — which is exactly what the
    /// element used to have, in ten <c>is null</c> guards and a render path of its own.
    /// </para>
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
    /// <param name="block">
    /// How wide the block is that a display formula is set in, or nothing where it has none. Only a formula with
    /// a number asks, because the number stands against the block's right edge — see <see cref="Numbered"/>.
    /// </param>
    public static Laid Build(string latex, double scale, bool inline = false, string systemFont = "Arial",
                             RawZone? shownAsWritten = null, bool placeholders = false,
                             double pixelsPerDip = 1.0, double block = 0) =>
        new LatexBuilder(latex, scale, inline, systemFont, shownAsWritten, placeholders, pixelsPerDip, block).Lay();

    /// <summary>
    /// Whether the typesetter has a drawing for a named command — a fact about the engine, and the one thing
    /// anything reading LaTeX has to ask it. Handed to <see cref="LatexTree"/> as a function, so that asking
    /// a formula questions still needs no fonts and no desktop.
    /// </summary>
    internal static bool Draws(string name) =>
        XamlMath.TexFormulaBuilder.Draws(name, WpfTeXFormulaParser.Instance);


    protected override Laid? Read()
    {
        if (Source.Length == 0) return null;

        // The tables, not the reader. Everything below builds from our own reading; what this is
        // asked for is what a name means to a typesetter — which symbol, which face, which command
        // it has a drawing for.
        var knowledge = WpfTeXFormulaParser.Instance;

        var editing = _shownAsWritten is { } zone && zone.Length > 0
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
            Source, name => XamlMath.TexFormulaBuilder.Draws(name, knowledge), editing, _placeholders);
        var reading = ContentReading.Of(read);
        var formula = XamlMath.TexFormulaBuilder.Build(reading.Root, knowledge);

        if (formula is null)
        {
            // Nothing here could set it as maths, so it is set as it was typed. Which is the same
            // answer this gives a stretch under the caret and a command nobody has heard of, reached
            // by the same road — and a great deal more use to whoever wrote it than a blank space.
            reading = ContentReading.Of(ContentNode.Branch(Kinds.Sequence, [ContentNode.Shown(Source)]));
            formula = XamlMath.TexFormulaBuilder.Build(reading.Root, knowledge);
        }

        if (formula is null) return null;

        var environment = WpfTeXEnvironment.Create(
            style: _inline ? TexStyle.Text : TexStyle.Display,
            scale: _scale,
            systemTextFontName: _systemFont);

        var capture = new LatexCapture(_scale, reading);
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

        // An equation's number, where one was written, set against the right edge of the block the formula is
        // displayed in — see Numbered.
        var (tree, size) = XamlMath.TexFormulaBuilder.Number(reading.Root) is { } number
            ? Numbered(laid, capture, number, environment, reading)
            : (laid, capture.Size);

        var made = new Laid(tree, size, trouble);

        // Which cells of a matrix read across and which read down — said to the tree once it is sealed.
        Order(reading, made.Root);

        return made;
    }

    /// <summary>
    /// The formula and its number as one block: the formula in the middle of it, where a display puts it, and the
    /// number against its right edge on the formula's baseline — which is where LaTeX puts an equation's number,
    /// wherever the <c>\tag</c> was written.
    ///
    /// <para>
    /// Two layouts put down together rather than one. The typesetter sets a row from left to right and knows
    /// nothing of a block, so given the number as part of the formula it put it straight after the last term.
    /// Which side of the block a thing stands against is the layout's to say — see <see cref="Side"/>.
    /// </para>
    /// </summary>
    private (LayoutTree Tree, System.Windows.Size Size) Numbered(LayoutTree formula, LatexCapture laid, TexFormula number,
                                                 XamlMath.TexEnvironment environment, ContentReading reading)
    {
        var capture = new LatexCapture(_scale, reading);
        number.RenderTo(capture, environment, 0, 0);
        capture.FinishRendering();
        if (capture.Tree is not { } tag) return (formula, laid.Size);

        var build = new LayoutBuilder();
        build.Open("Block");
        build.Graft(formula, default, Side.Centre);

        // On the formula's baseline, and a quad clear of it at the least, as LaTeX keeps an equation's number.
        build.Graft(tag, new System.Windows.Point(0, laid.Baseline - capture.Baseline), Side.Right, clear: _scale);
        build.Close();

        // Inline there is no block to stand against, and the number simply follows.
        var block = _inline ? 0 : _block;
        var tree = build.Seal(block);

        var covers = LatexCapture.Extent(tree.Root);
        tree.Settle(new Vector(block > 0 ? 0 : -covers.X, -covers.Y));

        return (tree, new System.Windows.Size(block > 0 ? System.Math.Max(block, covers.Right) : covers.Width, covers.Height));
    }

    /// <summary>
    /// How a formula sets characters it could not read: a monospaced face, so that what is on the page is
    /// unmistakably the source rather than a poorly typeset formula, and a reader can count the braces.
    /// </summary>
    protected override FormattedText Characters(string text) =>
        new(text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Consolas"),
            _scale * 0.6,
            Brushes.Black,   // never used: the mark takes the theme's ink at paint time
            _pixelsPerDip);

    /// <summary>
    /// Declares which cells of a matrix read across and which read down, so a drag over one means what it
    /// does on a sheet.
    ///
    /// <para>
    /// The shape comes from the parse tree, which knows a matrix is a table and says which row and column
    /// every cell is in. Nothing here clusters rectangles into bands or counts separators — the previous
    /// answer did exactly that, and it only ever worked for a matrix because a matrix is the one thing
    /// whose rows all hold the same number of things.
    /// </para>
    /// <para>
    /// Said to the tree after it was sealed, which a run can do and a parent cannot: which piece stands for
    /// a cell is a question about what was drawn, so it cannot be asked while the drawing is going on. It is
    /// the builder's, because a run is layout — a way of taking a step through what was drawn — and the
    /// thing that draws is the only thing that can say so.
    /// </para>
    /// <para>
    /// The ink is gathered once and only when there is a table to gather it for. A formula with no matrix
    /// walks nothing, and one with a matrix walks its tree once rather than once per cell — which is a
    /// difference of nine walks on the smallest interesting case and rather more on a real one.
    /// </para>
    /// </summary>
    private static void Order(ContentReading reading, Piece root)
    {
        if (root.Tree is not { } tree) return;

        List<Piece>? ink = null;

        foreach (var grid in TexGrid.In(reading.Root.Node))
        {
            ink ??= [.. root.Leaves().Where(piece => piece.Sits().Length > 0)];

            var cells = new Piece[grid.RowCount, grid.ColumnCount];
            foreach (var cell in grid.Cells) cells[cell.Row, cell.Column] = Holding(ink, cell);

            for (var row = 0; row < grid.RowCount; row++)
                Declare(Enumerable.Range(0, grid.ColumnCount).Select(at => cells[row, at]), vertical: false);

            for (var column = 0; column < grid.ColumnCount; column++)
                Declare(Enumerable.Range(0, grid.RowCount).Select(at => cells[at, column]), vertical: true);
        }

        // A cell that drew nothing is left out rather than standing as a gap: a run is a way to take a
        // step, and there is nothing to step to at an empty cell.
        void Declare(IEnumerable<Piece> cells, bool vertical) =>
            tree.Runs([.. cells.Where(cell => cell.Exists).Distinct()], vertical);
    }

    /// <summary>
    /// The piece standing for one cell: the lowest one holding every piece of ink written inside it, or
    /// nothing for a cell that drew nothing — one squared off so that "the third column" means the same in
    /// every row.
    ///
    /// <para>
    /// Found by what it <em>contains</em> rather than by what it says, because most cells say nothing: the
    /// typesetter makes a box per cell and the box names no source. For a cell holding one letter the
    /// answer is that letter; for one holding <c>4b^{2}+3</c> it is the box around the five of them.
    /// </para>
    /// </summary>
    private static Piece Holding(List<Piece> ink, TexCell cell)
    {
        var lowest = default(Piece);

        foreach (var piece in ink)
        {
            var at = piece.Sits();
            if (at.Start < cell.Start || at.End > cell.End) continue;

            if (!lowest.Exists) { lowest = piece; continue; }

            while (lowest.Exists && lowest != piece && !piece.Ancestors().Contains(lowest))
                lowest = lowest.Parent;
        }

        return lowest;
    }
}
