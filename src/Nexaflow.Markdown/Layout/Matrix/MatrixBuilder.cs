using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Matrix;
using Nexaflow.Markdown.Settings;

namespace Nexaflow.Markdown.Layout.Matrix;

/// <summary>
/// Lays out a <c>qr</c>, <c>aztec</c>, <c>datamatrix</c> or <c>pdf417</c> block onto the renderer-free layout tree: the symbol its stage
/// encoded it to (<see cref="MatrixSymbolNode"/>) — a light ground with a quiet zone round it, and each of the parts the code is made of
/// as a piece of its own, its dark modules one outline.
///
/// <para>
/// <b>Nothing drawn here is anything a reader typed</b>, so no piece carries a part. The source is the fields and the picture is what
/// they encode to; the caret has nowhere to stand in it, which is what lets the host arrow over a code the way it arrows over a word.
/// </para>
/// <para>
/// <b>A block that will not read or will not encode is shown as written</b>, each part at fault marked and why: a code is only ever
/// read where it is drawn, so its source is the only place it can be put right.
/// </para>
/// </summary>
public sealed class MatrixBuilder(ContentReading reading, EditState state, LayoutStyle style, bool isReadOnly, ILayoutMedium medium,
                                          LayoutHost host)
    : ContentBuilder(reading, state, style, isReadOnly, medium, host)
{
    private const string SourceFont = "Cascadia Code, Consolas, monospace";

    /// <summary>How big the characters of a block shown as written are set.</summary>
    private const double SourceSize = 13;

    protected override Laid Build() =>
        Reading.Root.Node is MatrixSymbolNode symbol
            ? Lay(symbol)
            : AsSource([.. Reading.Root.SelfAndDescendants()
                                  .Where(part => part.Trouble is not null && !part.Derived)
                                  .Select(part => (part, part.Trouble!))]);

    private Laid Lay(MatrixSymbolNode symbol)
    {
        var modules = symbol.Modules;
        double cell = symbol.Settings.CellSize;
        double row = cell * symbol.RowHeight;
        double quiet = symbol.Settings.Margin * cell;

        var (wide, tall) = ((modules.Width * cell) + (2 * quiet), (modules.Height * row) + (2 * quiet));

        var build = new LayoutBuilder(Medium);
        build.Open(MatrixPiece.Symbol);

        // A code paints its own light field whatever the theme, because a scanner needs dark modules on a light one.
        build.Draw(new RuleMark(new LayoutRect(0, 0, wide, tall), Ink(symbol.Settings.Light, Style.QrLight)));
        LayParts(build, symbol, new LayoutPoint(quiet, quiet), cell, row, Ink(symbol.Settings.Dark, Style.QrDark));

        build.Close();
        return new Laid(build.Seal(), wide, tall, []);
    }

    /// <summary>
    /// Each part as a piece of its own, its dark modules one outline with the horizontal runs merged — a symbol of a hundred thousand
    /// modules is a few thousand rectangles, not a hundred thousand.
    /// </summary>
    private void LayParts(LayoutBuilder into, MatrixSymbolNode symbol, LayoutPoint at, double cell, double row, LayoutInk ink)
    {
        for (int part = 0; part <= symbol.PartNames.Count; part++)
        {
            if (Outline(symbol, part, cell, row) is not { } outline) continue;

            into.Open(part < symbol.PartNames.Count ? symbol.PartNames[part] : MatrixPiece.Modules, part: null, at);
            into.Draw(new GeometryMark(outline, ink, null));
            into.Close();
        }
    }

    /// <summary>The dark modules of one part as an outline, or null where it has none.</summary>
    private LayoutShape? Outline(MatrixSymbolNode symbol, int part, double cell, double row)
    {
        var modules = symbol.Modules;
        ShapeBuilder? outline = null;

        for (int y = 0; y < modules.Height; y++)
        {
            int x = 0;
            while (x < modules.Width)
            {
                if (!Inked(x, y)) { x++; continue; }

                int run = 1;
                while (x + run < modules.Width && Inked(x + run, y)) run++;

                outline ??= Medium.Outline(evenOdd: true);
                Rectangle(outline, x * cell, y * row, run * cell, row);
                x += run;
            }
        }

        return outline?.Close();

        bool Inked(int x, int y) => modules[x, y] && symbol.Owner(x, y) == part;
    }

    private static void Rectangle(ShapeBuilder outline, double x, double y, double w, double h)
    {
        outline.Begin(new LayoutPoint(x, y), filled: true, closed: true, stroked: false);
        outline.LineTo(new LayoutPoint(x + w, y));
        outline.LineTo(new LayoutPoint(x + w, y + h));
        outline.LineTo(new LayoutPoint(x, y + h));
    }

    private LayoutInk Ink(HexColor? written, LayoutInk otherwise) =>
        written is { } colour ? Medium.Ink(new LayoutColor(colour.A, colour.R, colour.G, colour.B)) : otherwise;

    /// <summary>How a code sets the source it could not lay out at all: as the fields it was written as.</summary>
    protected override LayoutCharacters Characters => new(Style.Face(SourceFont), SourceSize, Style.Text);

    /// <summary>A code is drawn at the size its modules say, whatever room it lands in — and so is its source, where it will not draw.</summary>
    protected override double Within(double room) => double.PositiveInfinity;
}
