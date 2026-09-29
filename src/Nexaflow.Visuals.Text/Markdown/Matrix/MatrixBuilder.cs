using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Matrix;
using Nexaflow.Visuals.Text.Editing;
using System.Linq;
using Nexaflow.Markdown.Settings;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Matrix;

/// <summary>The pieces every 2D code's layout has, whatever the code is made of.</summary>
public static class MatrixPiece
{
    /// <summary>The whole symbol, quiet zone included — and the reason beneath it, when there is one.</summary>
    public const string Symbol = "Symbol";

    /// <summary>The modules a code does not name as a part of its own.</summary>
    public const string Modules = "Modules";
}

/// <summary>
/// Lays out a <c>qr</c>, <c>aztec</c>, <c>datamatrix</c> or <c>pdf417</c> block: the symbol its stage encoded it to
/// (<see cref="MatrixSymbolNode"/>) as a layout tree — a light ground with a quiet zone round it, and each of the parts the code is
/// made of as a piece of its own, its dark modules one geometry.
///
/// <para>
/// <b>Nothing drawn here is anything a reader typed</b>, so no piece carries a part. The source is the fields and the picture is
/// what they encode to; the caret has nowhere to stand in it, which is what lets the host arrow over a code the way it arrows
/// over a word.
/// </para>
/// <para>
/// <b>A block that will not read or will not encode is shown as written</b>, each part at fault marked and why: a code is only
/// ever read where it is drawn, so its source is the only place it can be put right.
/// </para>
/// </summary>
internal sealed class MatrixBuilder : ContentBuilder
{
    private static readonly FontFamily SourceFont = new("Cascadia Code, Consolas, monospace");

    internal MatrixBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly, Nesting nesting)
        : base(reading, state, style, isReadOnly, nesting) { }

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

        var ground = new Size(modules.Width * cell + 2 * quiet, modules.Height * row + 2 * quiet);

        var build = new LayoutBuilder();
        build.Open(MatrixPiece.Symbol);

        // A code paints its own light field whatever the theme, because a scanner needs dark modules on a light one.
        build.Draw(new RuleMark(new Rect(ground), Brush(symbol.Settings.Light, Style.QrLight)));
        LayParts(build, symbol, new Point(quiet, quiet), cell, row, Brush(symbol.Settings.Dark, Style.QrDark));

        build.Close();
        return new Laid(build.Seal(), ground, []);
    }

    /// <summary>
    /// Each part as a piece of its own, its dark modules one geometry with the horizontal runs merged — a symbol of a hundred
    /// thousand modules is a few thousand rectangles, not a hundred thousand.
    /// </summary>
    private static void LayParts(LayoutBuilder into, MatrixSymbolNode symbol, Point at, double cell, double row, Brush ink)
    {
        for (int part = 0; part <= symbol.Parts.Count; part++)
        {
            var geometry = Geometry(symbol, part, cell, row);
            if (geometry is null) continue;

            into.Open(part < symbol.Parts.Count ? symbol.Parts[part] : MatrixPiece.Modules, part: null, at);
            into.Draw(GeometryMark.Filled(geometry, ink));
            into.Close();
        }
    }

    /// <summary>The dark modules of one part as a frozen geometry, or null where it has none.</summary>
    private static StreamGeometry? Geometry(MatrixSymbolNode symbol, int part, double cell, double row)
    {
        var modules = symbol.Modules;
        var geometry = new StreamGeometry();
        var any = false;

        using (var ctx = geometry.Open())
        {
            for (int y = 0; y < modules.Height; y++)
            {
                int x = 0;
                while (x < modules.Width)
                {
                    if (!Inked(x, y)) { x++; continue; }

                    int run = 1;
                    while (x + run < modules.Width && Inked(x + run, y)) run++;

                    Rectangle(ctx, x * cell, y * row, run * cell, row);
                    any = true;
                    x += run;
                }
            }
        }

        if (!any) return null;

        geometry.Freeze();
        return geometry;

        bool Inked(int x, int y) => modules[x, y] && symbol.Owner(x, y) == part;
    }

    private static void Rectangle(StreamGeometryContext ctx, double x, double y, double w, double h)
    {
        ctx.BeginFigure(new Point(x, y), isFilled: true, isClosed: true);
        ctx.LineTo(new Point(x + w, y), isStroked: false, isSmoothJoin: false);
        ctx.LineTo(new Point(x + w, y + h), isStroked: false, isSmoothJoin: false);
        ctx.LineTo(new Point(x, y + h), isStroked: false, isSmoothJoin: false);
    }

    // ── Painting ──────────────────────────────────────────────────────────

    private static Brush Brush(HexColor? explicitColor, Brush fallback)
    {
        if (explicitColor is not { } c) return fallback;

        var brush = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
        brush.Freeze();
        return brush;
    }

    /// <summary>How a code sets the source it could not lay out at all: as the fields it was written as.</summary>
    protected override FormattedText Characters(string text) =>
        new(text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Style.Face(SourceFont),
            SourceSize,
            Style.Text,
            Editing.LayoutText.Density);

    /// <summary>How big the characters of a block shown as written are set.</summary>
    private const double SourceSize = 13;

    /// <summary>A code is drawn at the size its modules say, whatever room it lands in — and so is its source, where it will not draw.</summary>
    protected override double Within(double room) => double.PositiveInfinity;
}
