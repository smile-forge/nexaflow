using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Matrix;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Matrix;

/// <summary>The pieces every 2D code's layout has, whatever the code is made of.</summary>
public static class MatrixPiece
{
    /// <summary>The whole symbol, quiet zone included — and the reason beneath it, when there is one.</summary>
    public const string Symbol = "Symbol";

    /// <summary>The modules a code does not name as a part of its own.</summary>
    public const string Modules = "Modules";

    /// <summary>The line through a stand-in symbol.</summary>
    public const string Strike = "Strike";

    /// <summary>Why the block did not draw as itself.</summary>
    public const string Trouble = "Trouble";
}

/// <summary>
/// What the 2D-code builders share: laying a grid of modules down as a layout tree, and what to draw when a block
/// cannot be understood.
///
/// <para>
/// Each code is its own builder, because each reads its own fields, encodes its own way and is made of its own
/// parts — a QR code's finders, an Aztec code's bullseye, the start and stop columns of PDF417. What they have in
/// common sits under that: a module is a rectangle of ink on a light ground with a quiet zone around it, and a
/// symbol is those rectangles grouped into the parts it is made of.
/// </para>
/// <para>
/// <b>Nothing drawn here is anything a reader typed</b>, so no piece carries a part. The source is the fields and
/// the picture is what they encode to; the caret has nowhere to stand in it, which is what lets the host arrow
/// over a code the way it arrows over a word.
/// </para>
/// <para>
/// <b>It always draws a symbol.</b> A block that will not read or will not encode draws a valid one of its kind,
/// faint and struck through, with the reason set beneath it. A code-shaped absence reads as "this is a code, and
/// it is wrong"; an empty gap reads as a rendering fault.
/// </para>
/// </summary>
internal abstract class MatrixBuilder<TSymbol> : ContentBuilder where TSymbol : IModuleMatrix
{
    private static readonly FontFamily ReasonFont = new("Segoe UI, Arial, sans-serif");
    private static readonly FontFamily SourceFont = new("Cascadia Code, Consolas, monospace");

    private const double ReasonSize = 12;

    /// <summary>Clear air between a stand-in symbol and the reason beneath it.</summary>
    private const double ReasonGap = 4;

    /// <summary>The narrowest a reason is set to, so a small symbol does not stack it a word to a line.</summary>
    private const double ReasonRoom = 240;

    protected MatrixBuilder(ContentReading reading, MarkdownPalette palette) : base(reading)
    {
        Palette = palette;
    }

    protected MarkdownPalette Palette { get; }

    /// <summary>
    /// An encoded symbol and how to draw it. <paramref name="RowHeight"/> is a module's height as a multiple of its
    /// width: one for a true matrix, more for a stacked code whose rows are drawn taller than they are wide.
    /// </summary>
    protected sealed record Drawn(TSymbol Modules, MatrixSettings Settings, double RowHeight = 1);

    /// <summary>One of the parts a symbol is made of: what it is called, and which modules are its.</summary>
    protected readonly record struct Region(string Kind, Func<int, int, bool> Holds);

    /// <summary>
    /// What the parser's tree encodes to, or null with the reason — a block that does not read, or a payload the
    /// code cannot carry.
    /// </summary>
    protected abstract Drawn? Encode(ContentNode tree, out string? trouble);

    /// <summary>A valid symbol of this kind, to stand in faintly for one that could not be drawn.</summary>
    protected abstract Drawn StandIn(MatrixSettings settings);

    /// <summary>
    /// The parts <paramref name="symbol"/> is made of. A module goes to the first region that holds it, and one
    /// that none does is <see cref="MatrixPiece.Modules"/>.
    /// </summary>
    protected abstract IReadOnlyList<Region> Regions(TSymbol symbol);

    /// <summary>The element a 2D-code block is shown in: read-only, because there is nothing in it to edit.</summary>
    protected static Editing.ContentElement Host(string source, DiagramRenderOptions options,
                                                 Func<string, MarkdownPalette, Laid> build)
    {
        var element = new Editing.ContentElement(source, options.Palette,
            (state, _) => build(state.Source, options.Palette))
        {
            IsReadOnly = true,

            // Where the fields sit inside the fence, so the host never mistakes the block for one that is its
            // content and nothing else.
            SourceStart = options.SourceOffset,
            SourceLength = source.Length,

            Cursor = Cursors.Arrow,
            UseLayoutRounding = true,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 10),
        };

        // Module edges land on device pixels. A half-pixel seam between two dark modules reads as a light line to
        // a camera, which is the whole difference between a code that scans and one that does not.
        RenderOptions.SetEdgeMode(element, EdgeMode.Aliased);

        return element;
    }

    protected sealed override Laid Build()
    {
        var tree = Reading.Root.Node;

        return Encode(tree, out string? trouble) is { } drawn
            ? Lay(drawn, trouble: null)
            : Lay(StandIn(SettingsOf(tree)), trouble ?? "This block could not be read.");
    }

    /// <summary>
    /// The block's own drawing settings, where they read, so a stand-in is the size and colour that was asked for.
    /// </summary>
    private static MatrixSettings SettingsOf(ContentNode tree) =>
        MatrixBlockReader.TryReadFields(tree, out var fields, out _)
        && MatrixBlockReader.TrySettings(fields, out var settings, out _)
            ? settings
            : MatrixSettings.Default;

    private Laid Lay(Drawn drawn, string? trouble)
    {
        var symbol = drawn.Modules;
        double cell = drawn.Settings.CellSize;
        double row = cell * drawn.RowHeight;
        double quiet = drawn.Settings.Margin * cell;

        var ground = new Size(symbol.Width * cell + 2 * quiet, symbol.Height * row + 2 * quiet);

        var dark = Brush(drawn.Settings.Dark, Palette.QrDark);
        var ink = trouble is null ? dark : Faded(dark);

        var reason = trouble is null ? null : Reason(trouble, Math.Max(ground.Width, ReasonRoom));
        var size = reason is null
            ? ground
            : new Size(Math.Max(ground.Width, reason.Width), ground.Height + ReasonGap + reason.Height);

        var build = new LayoutBuilder();
        build.Open(MatrixPiece.Symbol);

        // A code paints its own light field whatever the theme, because a scanner needs dark modules on a light one.
        build.Draw(new RuleMark(new Rect(ground), Brush(drawn.Settings.Light, Palette.QrLight)));

        LayRegions(build, drawn, new Point(quiet, quiet), cell, row, ink);

        if (reason is not null)
        {
            build.Open(MatrixPiece.Strike, part: null, new Point(quiet, quiet));
            var across = symbol.Width * cell;
            var middle = symbol.Height * row / 2;
            var thickness = Math.Max(cell, 3);
            build.Draw(new RuleMark(new Rect(0, middle - thickness / 2, across, thickness), Palette.Danger));
            build.Close();

            build.Open(MatrixPiece.Trouble, part: null, new Point(0, ground.Height + ReasonGap));
            build.Draw(new TextMark(reason, default, Palette.Danger));
            build.Close();
        }

        build.Close();

        return new Laid(build.Seal(), size, trouble is null
            ? []
            : [new Diagnostic(0, Math.Max(Source.Length, 1), DiagnosticSeverity.Error, trouble)]);
    }

    /// <summary>
    /// Each region as a piece of its own, its dark modules one geometry with the horizontal runs merged — a symbol
    /// of a hundred thousand modules is a few thousand rectangles, not a hundred thousand.
    /// </summary>
    private void LayRegions(LayoutBuilder into, Drawn drawn, Point at, double cell, double row, Brush ink)
    {
        var symbol = drawn.Modules;
        var regions = Regions(symbol);

        // Which region each module belongs to, settled once: the region count is the remainder.
        var owner = new int[symbol.Width * symbol.Height];
        for (int y = 0; y < symbol.Height; y++)
            for (int x = 0; x < symbol.Width; x++)
            {
                int held = regions.Count;
                for (int r = 0; r < regions.Count; r++)
                    if (regions[r].Holds(x, y)) { held = r; break; }
                owner[y * symbol.Width + x] = held;
            }

        for (int r = 0; r <= regions.Count; r++)
        {
            var geometry = Geometry(symbol, owner, r, cell, row);
            if (geometry is null) continue;

            into.Open(r < regions.Count ? regions[r].Kind : MatrixPiece.Modules, part: null, at);
            into.Draw(GeometryMark.Filled(geometry, ink));
            into.Close();
        }
    }

    /// <summary>The dark modules of one region as a frozen geometry, or null where it has none.</summary>
    private static StreamGeometry? Geometry(TSymbol symbol, int[] owner, int region, double cell, double row)
    {
        var geometry = new StreamGeometry();
        var any = false;

        using (var ctx = geometry.Open())
        {
            for (int y = 0; y < symbol.Height; y++)
            {
                int x = 0;
                while (x < symbol.Width)
                {
                    if (!Inked(x, y)) { x++; continue; }

                    int run = 1;
                    while (x + run < symbol.Width && Inked(x + run, y)) run++;

                    Rectangle(ctx, x * cell, y * row, run * cell, row);
                    any = true;
                    x += run;
                }
            }
        }

        if (!any) return null;

        geometry.Freeze();
        return geometry;

        bool Inked(int x, int y) => symbol[x, y] && owner[y * symbol.Width + x] == region;
    }

    private static void Rectangle(StreamGeometryContext ctx, double x, double y, double w, double h)
    {
        ctx.BeginFigure(new Point(x, y), isFilled: true, isClosed: true);
        ctx.LineTo(new Point(x + w, y), isStroked: false, isSmoothJoin: false);
        ctx.LineTo(new Point(x + w, y + h), isStroked: false, isSmoothJoin: false);
        ctx.LineTo(new Point(x, y + h), isStroked: false, isSmoothJoin: false);
    }

    // ── Painting ──────────────────────────────────────────────────────────

    private FormattedText Reason(string trouble, double room) =>
        new(trouble,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(ReasonFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            ReasonSize,
            Palette.Danger,
            Editing.LayoutText.Density)
        {
            MaxTextWidth = room,
        };

    private static Brush Brush(HexColor? explicitColor, Brush fallback)
    {
        if (explicitColor is not { } c) return fallback;

        var brush = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
        brush.Freeze();
        return brush;
    }

    /// <summary>The same colour at a quarter strength — for a stand-in symbol.</summary>
    private static Brush Faded(Brush brush)
    {
        var faded = brush.Clone();
        faded.Opacity = 0.25;
        faded.Freeze();
        return faded;
    }

    /// <summary>How a code sets the source it could not lay out at all: as the fields it was written as.</summary>
    protected override FormattedText Characters(string text) =>
        new(text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(SourceFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            ReasonSize,
            Brushes.Black,
            Editing.LayoutText.Density);
}
