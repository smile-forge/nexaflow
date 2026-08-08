using Nexaflow.Visuals.Common.Layout;
using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using Nexaflow.Visuals.Text.Markdown.Graphs.Layout;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;

/// <summary>
/// The live surface for a graph-family diagram (flowchart, state, class, ER, requirement): it holds
/// the parsed graph and re-derives the picture whenever what should be visible changes.
/// <para>
/// Everything downstream of it stays a pure function — <see cref="GraphExpansion"/> derives the
/// visible graph, <see cref="SugiyamaLayout"/> places it, <see cref="WpfGraphRenderer"/> draws it —
/// so "the user opened a node" and "the panel got wider" are the same event here: re-derive and
/// re-render. That is what makes expansion cheap enough to be a property of every diagram rather
/// than a feature one host had to build for itself.
/// </para>
/// <para>
/// It implements <see cref="IInteractiveBlock"/> because a diagram embedded in a text container gets
/// its mouse events attributed to the container, the document, or a neighbouring paragraph. The host
/// hit-tests geometrically and hands the gesture here, and this class decides from the hit whether
/// the click landed on a region with an action (a node, a chip) or on empty canvas (a pan).
/// </para>
/// </summary>
public sealed class GraphDiagramView : ContentControl, IInteractiveBlock
{
    /// <summary>Height a diagram is given on the page. Past this it is a window onto itself rather
    /// than a block that grows without limit.</summary>
    public const double MaxDiagramHeight = 600;

    private readonly Graph                _source;
    private readonly NexaflowGraphConfig  _config;
    private readonly MarkdownPalette      _palette;
    private readonly DiagramRenderOptions _options;
    private readonly double               _fallbackWidth;

    /// <summary>Where the reader has got to. Supplied by the host when it wants that to survive the
    /// document being rebuilt (it does not survive on the element — the element is replaced).</summary>
    private readonly DiagramViewState _state;

    private PanZoomSurface? _surface;
    private LayoutedGraph?  _layout;
    private Graph?          _visible;
    private double          _laidOutFor;

    public GraphDiagramView(Graph source, NexaflowGraphConfig config, MarkdownPalette palette,
                            DiagramRenderOptions options, double fallbackWidth)
    {
        _source        = source;
        _config        = config;
        _palette       = palette;
        _options       = options;
        _state         = options.ViewState ?? new DiagramViewState();
        _fallbackWidth = fallbackWidth > 0 ? fallbackWidth : 900;

        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Margin = new Thickness(0, 8, 0, 12);

        // The layout wants to know the width it is laying out into, and only the visual tree knows
        // that. Until it does, the caller's per-diagram default stands in; once the real width
        // arrives — or changes by enough to matter — the graph is laid out again for it.
        SizeChanged += (_, e) =>
        {
            if (e.NewSize.Width <= 40) return;
            if (_laidOutFor > 0 && Math.Abs(e.NewSize.Width - _laidOutFor) < _laidOutFor * 0.2) return;
            Rebuild();
        };

        Rebuild();
    }

    /// <summary>The height a diagram is given here — the host's, when it knows better than the default.</summary>
    private double MaxHeight => _options.MaxHeight > 0 ? _options.MaxHeight : MaxDiagramHeight;

    /// <summary>
    /// Redraws. <paramref name="relayout"/> false reuses the geometry already computed, which is what
    /// a selection change wants: nothing about the graph moved, so nothing on screen should either.
    /// </summary>
    public void Rebuild(bool relayout = true)
    {
        if (relayout || _layout is null || _visible is null)
        {
            double width = ActualWidth > 40 ? ActualWidth : _fallbackWidth;
            _laidOutFor  = width;
            _visible     = GraphExpansion.Apply(_source, _config, _state.Expansion);
            _layout      = SugiyamaLayout.Compute(_visible, width, MaxHeight);
        }

        // Chips are only offered when the diagram actually has something behind a node — otherwise
        // an ordinary flowchart would sprout affordances it cannot honour.
        bool anyChip = _visible.Nodes.Any(n => n.Expansion != NodeExpansion.Leaf);

        var canvas = WpfGraphRenderer.RenderCanvas(_layout, _palette, new GraphRenderOptions
        {
            OnNavigate     = _options.OnNavigate,
            OnToggleExpand = anyChip ? Toggle : null,
            OnNodeClick    = ClickNode,
            SelectedNodeId = SelectedNodeId(),
        });

        if (_options.FitToWidth) { Content = Scaled(canvas); return; }
        Panned(canvas);
    }

    /// <summary>The selected node's id in the graph as it currently stands — the selection is
    /// remembered by key, because ids are renumbered every time the host re-emits the diagram.</summary>
    private string? SelectedNodeId() =>
        _state.Selected is null || _visible is null
            ? null
            : _visible.Nodes.FirstOrDefault(n => KeyOf(n) == _state.Selected)?.Id;

    private string KeyOf(Node node) => node.ExpandKey ?? _config.KeyFor(node.Id);

    /// <summary>
    /// The body of a node was clicked. Selecting is always the first thing it means — that is what
    /// picks the node and its edges out of a dense diagram, which is most of what makes one
    /// followable. Whether it <i>also</i> opens depends on the surface: where opening a node costs
    /// something (the PE inspector spawns a whole tab) the host asks for double-click instead, so a
    /// single click can be spent on looking.
    /// </summary>
    private bool ClickNode(string nodeId)
    {
        // Clicking the selected node again lets it go: the node is the thing the selection is about,
        // so it is the thing that should turn it off. Empty canvas is where a pan starts, and losing
        // your selection to a mis-grabbed drag would be its own small annoyance.
        string key = _visible?.FindNode(nodeId) is { } node ? KeyOf(node) : nodeId;
        Select(string.Equals(_state.Selected, key, StringComparison.OrdinalIgnoreCase) ? null : key);

        return _options.OpenOnDoubleClick || Open(nodeId);
    }

    /// <summary>Selects by key (the producer's name), or clears with null.</summary>
    private void Select(string? key)
    {
        if (string.Equals(_state.Selected, key, StringComparison.OrdinalIgnoreCase)) return;

        // Deliberately not registered with InteractiveSelection: that coordinator exists to stop two
        // blocks holding a *text* selection at once, and a picked-out node is not that. Joining it
        // would mean a click anywhere else on the page silently dropped the node you were tracing.
        _state.Selected = key;

        // Selecting changes what is drawn, never where anything is — so the geometry (and with it
        // the reader's pan and zoom) is left exactly as it was.
        Rebuild(relayout: false);

        if (_options.OnSelect is not { } onSelect) return;
        var node = key is null ? null : _visible?.Nodes.FirstOrDefault(n => KeyOf(n) == key);
        onSelect(new DiagramSelection(node?.Id, key, node?.Label));
    }

    /// <summary>Follows the node's own link, if it has one.</summary>
    private bool Open(string nodeId) =>
        _options.OnNavigate is { } navigate &&
        _visible?.FindNode(nodeId)?.Href is { Length: > 0 } href &&
        navigate(href);

    /// <summary>
    /// The normal chrome: a pan/zoom surface, always — not only once the diagram happens to overflow.
    /// A gesture that appears and disappears with the size of the content is one nobody can learn,
    /// and "it fits" is anyway only true until the next node is opened.
    /// </summary>
    private void Panned(Canvas canvas)
    {
        var size = new Size(canvas.Width, canvas.Height);

        // The surface outlives the drawing on it. Rebuilding it would refit — and re-centring the
        // view under a reader who just clicked something is exactly what they did not ask for.
        if (_surface is { } existing)
        {
            existing.Height = SurfaceHeight(canvas);
            existing.SetSurfaceContent(canvas, size, preserveView: true);
            return;
        }

        _surface = new PanZoomSurface
        {
            Height           = SurfaceHeight(canvas),
            AccentBrush      = _palette.Accent,
            ChromeBrush      = _palette.CodeBg,
            OnChromeBrush    = _palette.Text,
            MiniMapItems     = NodeBoxes,
            ZoomOnPlainWheel = _options.ZoomOnWheel,
        };
        _surface.ViewChanged += _state.RememberViewport;
        _surface.SetSurfaceContent(canvas, size);

        // A reader who was already here goes back where they were rather than being refitted.
        if (_state.HasViewport) _surface.RestoreView(_state.Scale, _state.OffsetX, _state.OffsetY);

        Content = Framed(_surface, canvas.Background);
    }

    /// <summary>This element's own furniture around the surface: the block margin plus the frame.
    /// A host sizing the diagram to a pane gives us the pane's height, so the surface has to leave
    /// room for the rest of the block or the frame lands past the bottom of the panel.</summary>
    private const double ChromeHeight = 8 + 12 + 2;

    private double SurfaceHeight(Canvas canvas) =>
        Math.Min(Math.Max(120, MaxHeight - ChromeHeight), Math.Max(220, canvas.Height + 16));

    /// <summary>
    /// The chrome for a surface that scales diagrams to its own column (the inline editor). Panning
    /// inside an already-scaled picture would fight both the scaling and text selection, so the
    /// diagram stays a plain block and the page scrolls.
    /// </summary>
    private FrameworkElement Scaled(Canvas canvas)
    {
        _surface = null;
        return Framed(new ScrollViewer
        {
            Content                       = canvas,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            MaxHeight                     = MaxHeight,
        }, canvas.Background);
    }

    private FrameworkElement Framed(FrameworkElement inner, Brush background) => new Border
    {
        Background      = background,
        BorderBrush     = _palette.Accent,
        BorderThickness = new Thickness(1),
        CornerRadius    = new CornerRadius(6),
        Child           = inner,
    };

    /// <summary>The node footprints, so the minimap shows the shape of the graph rather than one
    /// blank rectangle you cannot navigate by.</summary>
    private IEnumerable<MiniMapItem> NodeBoxes() =>
        _layout is null
            ? []
            : _layout.AllNodes.Where(n => !n.IsDummy)
                     .Select(n => new MiniMapItem(n.X - n.Width / 2, n.Y - n.Height / 2, n.Width, n.Height));

    /// <summary>
    /// A chip was clicked. The host gets first refusal — a generated diagram (the PE inspector's
    /// import tree) answers by walking further and re-emitting its markdown, which no renderer could
    /// do for it. Only when nobody claims it does the diagram open the node itself, which is what
    /// makes an ordinary markdown flowchart with an <c>expandDepth</c> explorable for free.
    /// </summary>
    private bool Toggle(string nodeId)
    {
        var shown   = _visible?.FindNode(nodeId);
        bool expand = shown?.Expansion != NodeExpansion.Expanded;
        string key  = shown is null ? _config.KeyFor(nodeId) : KeyOf(shown);

        // An overflow stand-in is this renderer's own invention — it is not in the source the host
        // generated, so offering it to the host would hand back an id naming nothing it knows.
        bool synthetic = GraphExpansion.OverflowParent(nodeId) is not null;

        // Recorded either way: the host's answer is to re-emit the whole diagram, and a fold this
        // renderer opened has to survive that or it springs shut the next time anything else moves.
        _state.Expansion[key] = expand;

        if (!synthetic && _options.OnExpand is { } host &&
            host(new DiagramExpandRequest(nodeId, key, shown?.Label ?? nodeId, expand)))
            return true;

        Rebuild();
        return true;
    }

    // ── IInteractiveBlock: the host drives the gesture ─────────────────────────

    void IInteractiveBlock.BeginPointerSelect(Point pointInElement)
    {
        // A click that landed on a region with an action is that action, not the start of a pan.
        var hit = VisualTreeHelper.HitTest(this, pointInElement)?.VisualHit;
        if (DiagramInteraction.Invoke(hit, this)) return;

        // Empty canvas: this is where a pan begins, and it leaves the selection alone. Panning to
        // look at the edges you just picked out should not be what puts them back.
        if (_surface is { } s) s.BeginPointer(TranslatePoint(pointInElement, s));
    }

    void IInteractiveBlock.ExtendPointerSelect(Point pointInElement)
    {
        if (_surface is { } s) s.ExtendPointer(TranslatePoint(pointInElement, s));
    }

    void IInteractiveBlock.EndPointerSelect() => _surface?.EndPointer();

    void IInteractiveBlock.ClearSelection() => Select(null);

    bool IInteractiveBlock.PointerDoubleClick(Point pointInElement)
    {
        if (!_options.OpenOnDoubleClick) return false;

        var hit = VisualTreeHelper.HitTest(this, pointInElement)?.VisualHit;
        return DiagramInteraction.Find(hit, this) is { Kind: DiagramTargetKind.Activate, NodeId: { } id } &&
               Open(id);
    }

    bool IInteractiveBlock.WantsPointerWheel(Point pointInElement) =>
        _options.ZoomOnWheel && _surface is not null;
}
