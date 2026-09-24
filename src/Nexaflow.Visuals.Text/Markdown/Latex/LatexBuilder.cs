using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Latex;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Latex.Tex.Parsers;
using Nexaflow.Visuals.Text.Markdown.Latex.Tex.Rendering;
using Nexaflow.Visuals.Text.Markdown.Latex.Tex;
using System.Collections.Generic;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Visuals.Text.Markdown.Latex;

/// <summary>
/// Reads LaTeX, typesets it, and lays it out as pieces; painting, hit-testing and selection run over the
/// shared <see cref="Laid"/> this hands back, the same code a tune or a barcode uses.
/// </summary>
public sealed partial class LatexBuilder : ContentBuilder
{




    internal LatexBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly)
        : base(reading, state, style, isReadOnly) { }

    /// <summary>How big the formula is set — body size for one in a line of text, larger for one on its own.</summary>
    private double _scale => Style.TextSize;

    /// <summary>Whether it is set in a line of text rather than on its own.</summary>
    private bool _inline => Style.InlineMath;

    /// <summary>The face <c>\text{…}</c> is set in.</summary>
    private const string _systemFont = "Arial";

    /// <summary>The width of the display block, for a formula carrying a number — see <see cref="Numbered"/>.</summary>
    private double _block => double.IsInfinity(base.Room) ? 0 : base.Room;

    /// <summary>
    /// Typesets <paramref name="latex"/> and records where every piece landed. Unreadable or throwing source
    /// comes back as its own characters with a wave under it rather than null, so a caller never needs a
    /// second "not a formula" case.
    /// </summary>
    /// <param name="shownAsWritten">
    /// The stretch being edited, shown as typed rather than typeset. Goes through the typesetter itself
    /// (rather than painted over afterwards) so the rest of the formula lays out around it correctly.
    /// </param>
    /// <param name="placeholders">Show a hole for an empty argument/cell, for a surface being written on. Off by default since reading is the common case.</param>
    /// <param name="block">Width of the display block; only needed when the formula has a number — see <see cref="Numbered"/>.</param>
    internal static Laid Lay(string latex, StyleFormat style, RawZone? shownAsWritten = null,
                             bool placeholders = false, double block = 0, int at = 0)
    {
        var editing = shownAsWritten is { } zone && zone.Length > 0 ? (zone.Start, zone.Length) : ((int, int)?)null;

        // Draws() is asked of the builder rather than the typesetter's tables: the tables describe what the
        // engine's own parser could read, a different question — asking them instead once showed `\ ` in red as
        // unreadable.
        var read = TexPipeline.Read(latex, Draws, editing, placeholders);

        return new LatexBuilder(ContentReading.Of(read, at), new EditState(latex, 0, null, shownAsWritten), style,
                                isReadOnly: !placeholders).Lay(block > 0 ? block : double.PositiveInfinity);
    }

    /// <summary>The same, given a size rather than a whole style — what a caller with nothing else to say uses.</summary>
    internal static Laid Lay(string latex, double scale, bool inline = false, RawZone? shownAsWritten = null,
                             bool placeholders = false, double block = 0, int at = 0) =>
        Lay(latex, StyleFormat.Dark with { TextSize = scale, InlineMath = inline }, shownAsWritten, placeholders, block, at);

    /// <summary>Whether the typesetter has a drawing for a named command. Passed to <see cref="LatexTree"/> as a function so reading needs no fonts or desktop.</summary>
    internal static bool Draws(string name) =>
        Draws(name, WpfTeXFormulaParser.Instance);


    protected override Laid? Build()
    {
        if (Source.Length == 0) return null;

        // The typesetter's own tables, not our reading — what a name means to it.
        var knowledge = WpfTeXFormulaParser.Instance;

        var environment = WpfTeXEnvironment.Create(
            style: _inline ? TexStyle.Text : TexStyle.Display,
            scale: _scale,
            systemTextFontName: _systemFont);

        var formula = Formula(Reading.Root, environment, knowledge);

        // Also settles the tree onto the origin: negative coordinates would put the caret outside the control
        // that draws it.
        var placed = LayFormula(formula, Reading);
        if (placed.Tree is not { } laid) return null;


        // Gathered after the fact rather than collected during read/lay, so a part being typed can pass
        // through without complaint.
        var trouble = Reading.Root.SelfAndDescendants()
            .Where(part => part.Node.Trouble is not null)
            .Select(part => TexSourcePart.Trouble(part, DiagnosticSeverity.Error, part.Node.Trouble!))
            .Concat(placed.Undrawn.Select(part => TexSourcePart.Trouble(
                part,
                DiagnosticSeverity.Warning,
                "This was read, and nothing here knows how to draw it.")))
            .ToList();

        // An equation's \tag number, set against the block's right edge — see Numbered.
        var (tree, size) = Number(Reading.Root, environment) is { } number
            ? Numbered(placed, number, Reading)
            : (laid, placed.Size);

        var made = new Laid(tree, size, trouble);

        // Which cells of a matrix read across and which read down — said to the tree once it is sealed.
        Order(Reading, made.Root);

        return made;
    }

    /// <summary>
    /// The formula and its <c>\tag</c> number as one block, number against the right edge on the formula's
    /// baseline — as LaTeX places it. Grafted rather than set inline, since the typesetter's own row layout
    /// knows nothing of a block edge to stand against.
    /// </summary>
    private (LayoutTree Tree, System.Windows.Size Size) Numbered(Placed formula, Set number, ContentReading reading)
    {
        var laid = LayFormula(number, reading);
        if (laid.Tree is not { } tag) return (formula.Tree!, formula.Size);

        var build = new LayoutBuilder();
        build.Open("Block");
        build.Graft(formula.Tree!, default, Side.Centre);

        // On the formula's baseline, and a quad clear of it at the least, as LaTeX keeps an equation's number.
        build.Graft(tag, new System.Windows.Point(0, formula.Baseline - laid.Baseline), Side.Right, clear: _scale);
        build.Close();

        // Inline there is no block to stand against, and the number simply follows.
        var block = _inline ? 0 : _block;
        var tree = build.Seal(block);

        var covers = Extent(tree.Root);
        tree.Settle(new Vector(block > 0 ? 0 : -covers.X, -covers.Y));

        return (tree, new System.Windows.Size(block > 0 ? System.Math.Max(block, covers.Right) : covers.Width, covers.Height));
    }

    /// <summary>Unreadable source shown in a monospaced face, so it reads as source rather than a poorly typeset formula.</summary>
    protected override FormattedText Characters(string text) =>
        new(text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Style.Face(Style.MonoFont),
            _scale * 0.6,
            Brushes.Black,   // never used: the mark takes the theme's ink at paint time
            Editing.LayoutText.Density);

    /// <summary>
    /// Declares which cells of a matrix read across/down, so a drag over one behaves like a spreadsheet
    /// selection. Shape comes from the parse tree, not from clustering rectangles — the previous approach
    /// only worked because a matrix's rows are all the same length. Called after the tree is sealed, since
    /// "which piece is a cell" is a question about what was drawn.
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

        // An empty cell is left out rather than standing as a gap: there is nothing to step to.
        void Declare(IEnumerable<Piece> cells, bool vertical) =>
            tree.Runs([.. cells.Where(cell => cell.Exists).Distinct()], vertical);
    }

    /// <summary>The lowest piece holding all the ink in a cell — found by containment, not by name, since the typesetter's per-cell boxes name no source.</summary>
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
