using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Nexaflow.Features.Solver.Views;

/// <summary>One tile of an <see cref="OctagonNavigator"/>.</summary>
/// <param name="Id">Stable id, reported when the tile is clicked.</param>
/// <param name="Label">What the tile reads.</param>
/// <param name="Tooltip">Spelled out on hover — for a symbol, the command it types.</param>
/// <param name="Fill">Its colour. The caller owns the palette so a theme can retune it.</param>
/// <param name="HasChildren">Whether clicking it drills in rather than selecting a leaf.</param>
public sealed record OctagonNode(string Id, string Label, string Tooltip, Brush Fill, bool HasChildren);

/// <summary>
/// A one-ring navigator: eight tiles packed around a central octagon that steps back up.
/// <para>
/// It fills a rectangle rather than inscribing a circle in one, because the panel it lives in is a
/// rectangle and a circle throws away the four corners — which in a band this short is most of the
/// room a label had. The nine regions are a rectangle cut by an octagon: four corner pentagons, four
/// edge bars, and the centre. Every region is a straight-edged box you can write a whole word in.
/// </para>
/// <para>
/// The proportions are not eyeballed. <see cref="Inset"/> is what makes the nine areas as near equal
/// as the shape allows: exact equality forces the mitre to zero (a plain 3×3 grid), so the cut is
/// fixed at a size that reads, and the inset then minimises the largest ratio between any two
/// regions — about 1.5, against the 3:1 the obvious proportions give.
/// </para>
/// <para>
/// One ring at a time, not two: eight readable tiles beat sixteen unreadable ones, and the way back
/// is the centre. Depth is handled by drilling, and the breadcrumb outside says where you are. The
/// ring is always drawn whole — a level with fewer than eight groups shows vacant tiles rather than
/// eight-sevenths-sized ones, so the shape never moves as you navigate.
/// </para>
/// </summary>
public sealed class OctagonNavigator : FrameworkElement
{
    /// <summary>The tiles to draw, clockwise from the top. Eight or fewer; more are ignored.</summary>
    public static readonly DependencyProperty NodesProperty =
        DependencyProperty.Register(nameof(Nodes), typeof(IReadOnlyList<OctagonNode?>), typeof(OctagonNavigator),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>What the centre reads — where you are now.</summary>
    public static readonly DependencyProperty CentreLabelProperty =
        DependencyProperty.Register(nameof(CentreLabel), typeof(string), typeof(OctagonNavigator),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Whether the centre can step back up; drawn differently when it cannot.</summary>
    public static readonly DependencyProperty CanGoUpProperty =
        DependencyProperty.Register(nameof(CanGoUp), typeof(bool), typeof(OctagonNavigator),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Line colour between tiles — normally the panel behind, so they read as separate.</summary>
    public static readonly DependencyProperty StrokeBrushProperty =
        DependencyProperty.Register(nameof(StrokeBrush), typeof(Brush), typeof(OctagonNavigator),
            new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Text colour for the centre.</summary>
    public static readonly DependencyProperty CentreForegroundProperty =
        DependencyProperty.Register(nameof(CentreForeground), typeof(Brush), typeof(OctagonNavigator),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Fill for the centre.</summary>
    public static readonly DependencyProperty CentreFillProperty =
        DependencyProperty.Register(nameof(CentreFill), typeof(Brush), typeof(OctagonNavigator),
            new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// Fill for a position the current level has nothing for. Null — the default — draws nothing at
    /// all, which is the right answer: a painted tile with no label reads as something that failed to
    /// load, and three of them side by side read as a broken control, where plain space reads as a
    /// ring that happens not to be full.
    /// </summary>
    public static readonly DependencyProperty VacantFillProperty =
        DependencyProperty.Register(nameof(VacantFill), typeof(Brush), typeof(OctagonNavigator),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>How many tiles the ring holds. The tree is authored to this.</summary>
    public const int MaxNodes = 8;

    /// <summary>
    /// Where the outer cuts sit, as a fraction of width and of height. See the class remarks: this is
    /// the inset that evens out the nine areas once the mitre is fixed at <see cref="BevelRatio"/>.
    /// </summary>
    private const double Inset = 0.365;

    /// <summary>
    /// The corner cut, as a fraction of the geometric mean of width and height — which is what makes
    /// it the same number of pixels across as down, so it reads as a mitre rather than as a line that
    /// has slipped.
    /// </summary>
    private const double BevelRatio = 0.05;

    /// <summary>Type size for a tile that opens a level — a word, read left to right.</summary>
    private const double CategorySize = 11;

    /// <summary>Type size for a tile that types a symbol — a glyph, read as a shape.</summary>
    private const double SymbolSize = 17;

    private int _hover = -2;   // -2 nothing, -1 the centre, 0.. a tile

    /// <inheritdoc cref="NodesProperty"/>
    public IReadOnlyList<OctagonNode?>? Nodes
    {
        get => (IReadOnlyList<OctagonNode?>?)GetValue(NodesProperty);
        set => SetValue(NodesProperty, value);
    }

    /// <inheritdoc cref="CentreLabelProperty"/>
    public string CentreLabel
    {
        get => (string)GetValue(CentreLabelProperty);
        set => SetValue(CentreLabelProperty, value);
    }

    /// <inheritdoc cref="CanGoUpProperty"/>
    public bool CanGoUp
    {
        get => (bool)GetValue(CanGoUpProperty);
        set => SetValue(CanGoUpProperty, value);
    }

    /// <inheritdoc cref="StrokeBrushProperty"/>
    public Brush? StrokeBrush
    {
        get => (Brush?)GetValue(StrokeBrushProperty);
        set => SetValue(StrokeBrushProperty, value);
    }

    /// <inheritdoc cref="CentreForegroundProperty"/>
    public Brush? CentreForeground
    {
        get => (Brush?)GetValue(CentreForegroundProperty);
        set => SetValue(CentreForegroundProperty, value);
    }

    /// <inheritdoc cref="CentreFillProperty"/>
    public Brush? CentreFill
    {
        get => (Brush?)GetValue(CentreFillProperty);
        set => SetValue(CentreFillProperty, value);
    }

    /// <inheritdoc cref="VacantFillProperty"/>
    public Brush? VacantFill
    {
        get => (Brush?)GetValue(VacantFillProperty);
        set => SetValue(VacantFillProperty, value);
    }

    /// <summary>A tile was clicked; the argument is its <see cref="OctagonNode.Id"/>.</summary>
    public event EventHandler<string>? NodeClicked;

    /// <summary>The centre was clicked — step back up.</summary>
    public event EventHandler? CentreClicked;

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        var was = _hover;
        _hover = IndexAt(e.GetPosition(this));
        if (was != _hover)
        {
            // One element, eight targets: the tooltip has to follow the pointer between tiles, which
            // is the whole reason a symbol tile can say what it types without a cap wide enough to
            // spell it out.
            var tip = _hover >= 0 && Nodes is { } n && _hover < n.Count ? n[_hover]?.Tooltip : null;
            ToolTip = string.IsNullOrEmpty(tip) ? null : tip;
            InvalidateVisual();
        }
        base.OnMouseMove(e);
    }

    /// <inheritdoc/>
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_hover != -2) { _hover = -2; InvalidateVisual(); }
        base.OnMouseLeave(e);
    }

    /// <inheritdoc/>
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        var hit = IndexAt(e.GetPosition(this));
        if (hit == -1) CentreClicked?.Invoke(this, EventArgs.Empty);
        else if (hit >= 0 && Nodes is { } nodes && hit < nodes.Count && nodes[hit] is { } node)
            NodeClicked?.Invoke(this, node.Id);
        base.OnMouseLeftButtonUp(e);
    }

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        // Transparent backdrop so the whole control takes the mouse, not just the painted tiles.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

        var nodes = Nodes;
        var pen = StrokeBrush is { } sb ? new Pen(sb, 1.5) : null;
        var m = Measurements();

        for (var i = 0; i < MaxNodes; i++)
        {
            var node = nodes is not null && i < nodes.Count ? nodes[i] : null;
            if (node is null)
            {
                if (VacantFill is { } vacant) dc.DrawGeometry(vacant, pen, TileGeometry(i, m));
                continue;
            }

            var fill = i == _hover ? Lighten(node.Fill) : node.Fill;
            dc.DrawGeometry(fill, pen, TileGeometry(i, m));

            // A category is a word and a symbol is a glyph, so the size says which one you are
            // looking at without a badge: the ring you are on is legible before you read it.
            DrawLabel(dc, node.Label, LabelBox(i, m), Contrast(node.Fill),
                      node.HasChildren ? CategorySize : SymbolSize);
        }

        // Centre: where you are, and the way back out.
        dc.DrawGeometry(_hover == -1 && CanGoUp ? Lighten(CentreFill ?? Brushes.DimGray) : CentreFill,
                        pen, CentreGeometry(m));
        DrawLabel(dc, CanGoUp ? "▲ " + CentreLabel : CentreLabel,
                  LabelBox(-1, m), CentreForeground ?? Brushes.White, CategorySize);
    }

    /// <summary>
    /// The four numbers the whole tiling is built from: where the outer cuts sit (<c>A</c> across,
    /// <c>B</c> down) and where the octagon's flat faces sit (<c>L</c>, <c>T</c>). The gap between
    /// each pair is the corner mitre.
    /// </summary>
    private (double A, double B, double L, double T) Measurements()
    {
        var (w, h) = (ActualWidth, ActualHeight);
        var a = w * Inset;
        var b = h * Inset;

        // One mitre length in pixels, applied to both axes so the cut comes out at 45 degrees.
        // Capped at a third of the smaller inset, or a very flat control would bevel the octagon away.
        var d = Math.Min(BevelRatio * Math.Sqrt(w * h), Math.Min(a, b) / 3);
        return (a, b, a - d, b - d);
    }

    /// <summary>Which tile (or the centre) a point falls in.</summary>
    private int IndexAt(Point p)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return -2;

        var nodes = Nodes;
        if (nodes is null || nodes.Count == 0) return -2;

        var m = Measurements();
        if (CentreGeometry(m).FillContains(p)) return -1;

        for (var i = 0; i < nodes.Count && i < MaxNodes; i++)
            if (nodes[i] is not null && TileGeometry(i, m).FillContains(p)) return i;

        return -2;
    }

    /// <summary>
    /// One tile, clockwise from the top: 0 top, 1 top-right, 2 right, 3 bottom-right, 4 bottom,
    /// 5 bottom-left, 6 left, 7 top-left. Edge tiles are rectangles; corner tiles are pentagons whose
    /// inner corner is the octagon's mitre.
    /// </summary>
    private StreamGeometry TileGeometry(int index, (double A, double B, double L, double T) m)
    {
        var (w, h) = (ActualWidth, ActualHeight);
        var (a, b, l, t) = m;

        return index switch
        {
            0 => Polygon(new(a, 0), new(w - a, 0), new(w - a, t), new(a, t)),
            1 => Polygon(new(w - a, 0), new(w, 0), new(w, b), new(w - l, b), new(w - a, t)),
            2 => Polygon(new(w - l, b), new(w, b), new(w, h - b), new(w - l, h - b)),
            3 => Polygon(new(w - l, h - b), new(w, h - b), new(w, h), new(w - a, h), new(w - a, h - t)),
            4 => Polygon(new(a, h - t), new(w - a, h - t), new(w - a, h), new(a, h)),
            5 => Polygon(new(0, h - b), new(l, h - b), new(a, h - t), new(a, h), new(0, h)),
            6 => Polygon(new(0, b), new(l, b), new(l, h - b), new(0, h - b)),
            _ => Polygon(new(0, 0), new(a, 0), new(a, t), new(l, b), new(0, b)),
        };
    }

    private StreamGeometry CentreGeometry((double A, double B, double L, double T) m)
    {
        var (w, h) = (ActualWidth, ActualHeight);
        var (a, b, l, t) = m;

        return Polygon(new(a, t), new(w - a, t), new(w - l, b), new(w - l, h - b),
                       new(w - a, h - t), new(a, h - t), new(l, h - b), new(l, b));
    }

    /// <summary>
    /// The axis-aligned box a tile's label is centred in. For a corner tile this is the full
    /// rectangle: the mitre only bites the corner within one bevel of the octagon, which a centred
    /// line of text never reaches.
    /// </summary>
    private Rect LabelBox(int index, (double A, double B, double L, double T) m)
    {
        var (w, h) = (ActualWidth, ActualHeight);
        var (a, b, l, t) = m;

        return index switch
        {
            0 => new Rect(a, 0, w - 2 * a, t),
            1 => new Rect(w - a, 0, a, b),
            2 => new Rect(w - l, b, l, h - 2 * b),
            3 => new Rect(w - a, h - b, a, b),
            4 => new Rect(a, h - t, w - 2 * a, t),
            5 => new Rect(0, h - b, a, b),
            6 => new Rect(0, b, l, h - 2 * b),
            7 => new Rect(0, 0, a, b),
            _ => new Rect(l, t, w - 2 * l, h - 2 * t),
        };
    }

    private static StreamGeometry Polygon(params Point[] points)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(points[0], isFilled: true, isClosed: true);
            for (var i = 1; i < points.Length; i++) ctx.LineTo(points[i], true, false);
        }
        g.Freeze();
        return g;
    }

    private void DrawLabel(DrawingContext dc, string text, Rect box, Brush brush, double size)
    {
        if (string.IsNullOrEmpty(text) || box.Width <= 0 || box.Height <= 0) return;

        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(SystemFonts.MessageFontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(16, box.Width - 8),
            MaxTextHeight = Math.Max(12, box.Height - 4),
            MaxLineCount = 2,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
        };

        // Horizontally the text has already centred itself inside MaxTextWidth, so the origin is just
        // the padding — offsetting by the natural width as well centres it twice and pushes every
        // label off to one side.
        dc.DrawText(ft, new Point(box.X + (box.Width - ft.MaxTextWidth) / 2,
                                  box.Y + (box.Height - ft.Height) / 2));
    }

    /// <summary>Black or white, whichever the tile's own colour can be read against.</summary>
    private static Brush Contrast(Brush fill)
    {
        if (fill is not SolidColorBrush s) return Brushes.White;
        var c = s.Color;
        var luma = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
        return luma > 0.6 ? Brushes.Black : Brushes.White;
    }

    private static Brush Lighten(Brush fill)
    {
        if (fill is not SolidColorBrush s) return fill;
        var c = s.Color;
        var b = new SolidColorBrush(Color.FromArgb(c.A,
            (byte)Math.Min(255, c.R + 38), (byte)Math.Min(255, c.G + 38), (byte)Math.Min(255, c.B + 38)));
        b.Freeze();
        return b;
    }
}
