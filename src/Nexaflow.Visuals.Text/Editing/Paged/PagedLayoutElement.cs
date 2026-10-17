using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Visuals.Text.Editing.Paged;

/// <summary>
/// Fixed pages, one under another, read from an <see cref="IPagedLayoutSource"/> as the reader scrolls to them: the
/// paged counterpart of <see cref="ContentElement"/>, drawing and pressing the same layout trees through the same
/// <see cref="LayoutPainter"/> and <see cref="LayoutQuery"/>.
/// <para>
/// It scrolls itself (<see cref="IScrollInfo"/>) inside a <see cref="ScrollViewer"/>, so its extent is every page's
/// height while only the pages near the viewport are laid and drawn. Each shown page is a retained visual painted once
/// from its tree; scrolling moves those visuals and zooming scales them, so neither paints a page again. Selection and
/// search hits are a second visual over each page, the only thing a drag repaints.
/// </para>
/// </summary>
public sealed class PagedLayoutElement : FrameworkElement, IScrollInfo
{
    /// <summary>How far beyond the viewport, in viewports, pages are laid ahead of being scrolled to.</summary>
    private const double LayAhead = 1;

    /// <summary>How far beyond the viewport, in viewports, a shown page is kept before its visual is let go.</summary>
    private const double KeepShown = 2;

    /// <summary>How many laid pages are kept once off screen, newest first, so scrolling back does not lay them again.</summary>
    private const int KeptLaid = 16;

    /// <summary>The smallest and largest zoom, as fractions of full size.</summary>
    public const double MinZoom = 0.1, MaxZoom = 8;

    /// <summary>How much one notch of Ctrl+wheel zooms by.</summary>
    private const double ZoomStep = 1.1;

    /// <summary>How far one line of scrolling moves, in device-independent pixels.</summary>
    private const double LineStep = 48;

    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(IPagedLayoutSource), typeof(PagedLayoutElement),
        new PropertyMetadata(null, (d, _) => ((PagedLayoutElement)d).Reload()));

    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom), typeof(double), typeof(PagedLayoutElement),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, e) => ((PagedLayoutElement)d).Zoomed((double)e.OldValue, null),
            (_, value) => Math.Clamp((double)value, MinZoom, MaxZoom)));

    public static readonly DependencyProperty PageGapProperty = DependencyProperty.Register(
        nameof(PageGap), typeof(double), typeof(PagedLayoutElement),
        new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsMeasure, (d, _) => ((PagedLayoutElement)d).Arranged()));

    public static readonly DependencyProperty BackgroundProperty = Panel.BackgroundProperty.AddOwner(
        typeof(PagedLayoutElement), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PaperProperty = DependencyProperty.Register(
        nameof(Paper), typeof(Brush), typeof(PagedLayoutElement),
        new PropertyMetadata(Brushes.White, (d, _) => ((PagedLayoutElement)d).Repaint()));

    public static readonly DependencyProperty InkProperty = DependencyProperty.Register(
        nameof(Ink), typeof(Brush), typeof(PagedLayoutElement),
        new PropertyMetadata(Brushes.Black, (d, _) => ((PagedLayoutElement)d).Repaint()));

    public static readonly DependencyProperty SelectionProperty = DependencyProperty.Register(
        nameof(Selection), typeof(Brush), typeof(PagedLayoutElement),
        new PropertyMetadata(new SolidColorBrush(Color.FromArgb(0x55, 0x33, 0x99, 0xFF)), (d, _) => ((PagedLayoutElement)d).Overlay()));

    public static readonly DependencyProperty HighlightProperty = DependencyProperty.Register(
        nameof(Highlight), typeof(Brush), typeof(PagedLayoutElement),
        new PropertyMetadata(new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xD7, 0x00)), (d, _) => ((PagedLayoutElement)d).Overlay()));

    public static readonly DependencyProperty CurrentHighlightProperty = DependencyProperty.Register(
        nameof(CurrentHighlight), typeof(Brush), typeof(PagedLayoutElement),
        new PropertyMetadata(new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0x8C, 0x00)), (d, _) => ((PagedLayoutElement)d).Overlay()));

    private static readonly DependencyPropertyKey CurrentPageKey = DependencyProperty.RegisterReadOnly(
        nameof(CurrentPage), typeof(int), typeof(PagedLayoutElement), new PropertyMetadata(0));

    public static readonly DependencyProperty CurrentPageProperty = CurrentPageKey.DependencyProperty;

    private readonly VisualCollection _visuals;
    private readonly Dictionary<int, Shown> _shown = [];
    private readonly Dictionary<int, LaidPage> _laid = [];
    private readonly LinkedList<int> _recent = new();
    private readonly Dictionary<int, CancellationTokenSource> _laying = [];
    private readonly Dictionary<int, Task<string>> _texts = [];

    private Size[] _sizes = [];
    private double[] _tops = [];
    private double _widest;
    private CancellationTokenSource _lifetime = new();

    private Vector _offset;
    private Size _viewport;
    private Size _extent;

    private PagePlace? _anchor;
    private PagePlace? _focus;
    private string? _pressedLink;
    private bool _dragged;
    private IReadOnlyList<PageRange> _highlights = [];
    private int _currentHighlight = -1;

    public PagedLayoutElement()
    {
        _visuals = new VisualCollection(this);
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        Cursor = Cursors.Arrow;

        CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, _) => _ = CopyAsync(),
            (_, e) => e.CanExecute = HasSelection));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.SelectAll, (_, _) => SelectAll(),
            (_, e) => e.CanExecute = _sizes.Length > 0));
    }

    /// <summary>The pages shown.</summary>
    public IPagedLayoutSource? Source
    {
        get => (IPagedLayoutSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>How large the pages are drawn, as a fraction of full size.</summary>
    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    /// <summary>The space between pages, and around them, in device-independent pixels on screen.</summary>
    public double PageGap
    {
        get => (double)GetValue(PageGapProperty);
        set => SetValue(PageGapProperty, value);
    }

    /// <summary>What shows between and around the pages.</summary>
    public Brush Background
    {
        get => (Brush)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>What a page not yet laid is drawn as.</summary>
    public Brush Paper
    {
        get => (Brush)GetValue(PaperProperty);
        set => SetValue(PaperProperty, value);
    }

    /// <summary>The colour of marks that name none of their own.</summary>
    public Brush Ink
    {
        get => (Brush)GetValue(InkProperty);
        set => SetValue(InkProperty, value);
    }

    /// <summary>What selected text is washed with.</summary>
    public Brush Selection
    {
        get => (Brush)GetValue(SelectionProperty);
        set => SetValue(SelectionProperty, value);
    }

    /// <summary>What search hits are washed with.</summary>
    public Brush Highlight
    {
        get => (Brush)GetValue(HighlightProperty);
        set => SetValue(HighlightProperty, value);
    }

    /// <summary>What the search hit being looked at is washed with.</summary>
    public Brush CurrentHighlight
    {
        get => (Brush)GetValue(CurrentHighlightProperty);
        set => SetValue(CurrentHighlightProperty, value);
    }

    /// <summary>The page being read (0-based): the one across the upper third of the viewport.</summary>
    public int CurrentPage => (int)GetValue(CurrentPageProperty);

    /// <summary>A link on a page was pressed: its target, as the page's tree names it.</summary>
    public event EventHandler<string>? LinkActivated;

    /// <summary>The selection changed.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>
    /// The reader asked to copy the selection: the selected text, for the host to put on the clipboard — the clipboard is
    /// the host's, as it is for every content surface.
    /// </summary>
    public event EventHandler<string>? CopyRequested;

    /// <summary>A page was laid: its index, and its tree, now on screen.</summary>
    public event EventHandler<int>? PageLaid;

    /// <summary>Whether any text is selected.</summary>
    public bool HasSelection => _anchor is { } a && _focus is { } f && a != f;

    // ── Pages and their places ───────────────────────────────────────────────────────────────────────────────────

    private void Reload()
    {
        _lifetime.Cancel();
        _lifetime = new CancellationTokenSource();
        foreach (var laying in _laying.Values) laying.Cancel();
        _laying.Clear();
        _laid.Clear();
        _recent.Clear();
        _texts.Clear();
        foreach (var shown in _shown.Values) _visuals.Remove(shown.Visual);
        _shown.Clear();
        _anchor = _focus = null;
        _highlights = [];
        _currentHighlight = -1;

        var source = Source;
        var count = source?.PageCount ?? 0;
        _sizes = new Size[count];
        for (var i = 0; i < count; i++) _sizes[i] = source!.PageSize(i);
        _offset = default;
        Arranged();
    }

    /// <summary>Recomputes where every page sits, at full size, after a page size or the gap changed.</summary>
    private void Arranged()
    {
        _tops = new double[_sizes.Length + 1];
        _widest = 0;
        var gap = PageGap / Math.Max(Zoom, MinZoom);
        var y = gap;
        for (var i = 0; i < _sizes.Length; i++)
        {
            _tops[i] = y;
            y += _sizes[i].Height + gap;
            _widest = Math.Max(_widest, _sizes[i].Width);
        }

        _tops[^1] = y;
        Extended();
    }

    private void Extended()
    {
        var zoom = Zoom;
        _extent = new Size(_widest * zoom + 2 * PageGap, _tops.Length == 0 ? 0 : _tops[^1] * zoom);
        ClampOffset();
        ScrollOwner?.InvalidateScrollInfo();
        Placed();
    }

    /// <summary>Where page <paramref name="index"/>'s top left lands in the element, at the current zoom and scroll.</summary>
    private Point Origin(int index)
    {
        var zoom = Zoom;
        var width = _sizes[index].Width * zoom;
        var x = _extent.Width <= _viewport.Width
            ? (_viewport.Width - width) / 2
            : PageGap + (_widest * zoom - width) / 2 - _offset.X;
        return new Point(x, _tops[index] * zoom - _offset.Y);
    }

    /// <summary>The page under <paramref name="point"/> (in the element), and the point in that page's own coordinates.</summary>
    private bool PageAt(Point point, out int index, out Point local)
    {
        index = -1;
        local = default;
        if (_sizes.Length == 0) return false;

        var zoom = Zoom;
        var y = (point.Y + _offset.Y) / zoom;
        var found = Array.BinarySearch(_tops, 0, _sizes.Length, y);
        var at = found >= 0 ? found : Math.Max(0, ~found - 1);
        if (at >= _sizes.Length) at = _sizes.Length - 1;

        var origin = Origin(at);
        local = new Point((point.X - origin.X) / zoom, (point.Y - origin.Y) / zoom);
        index = at;
        return local.X >= 0 && local.Y >= 0 && local.X <= _sizes[at].Width && local.Y <= _sizes[at].Height;
    }

    /// <summary>
    /// Brings the shown pages into line with the viewport: places them, shows those that came near, lets go of those that
    /// went far, and asks for the ones that are near and not yet laid.
    /// </summary>
    private void Placed()
    {
        if (_sizes.Length == 0 || _viewport.Height <= 0)
        {
            InvalidateVisual();
            return;
        }

        var zoom = Zoom;
        var top = (_offset.Y - _viewport.Height * LayAhead) / zoom;
        var bottom = (_offset.Y + _viewport.Height * (1 + LayAhead)) / zoom;
        var keepTop = (_offset.Y - _viewport.Height * KeepShown) / zoom;
        var keepBottom = (_offset.Y + _viewport.Height * (1 + KeepShown)) / zoom;

        foreach (var index in _shown.Keys.ToList())
        {
            if (_tops[index + 1] >= keepTop && _tops[index] <= keepBottom) continue;
            _visuals.Remove(_shown[index].Visual);
            _shown.Remove(index);
            if (_laying.Remove(index, out var cancel)) cancel.Cancel();
        }

        for (var index = First(top); index < _sizes.Length && _tops[index] <= bottom; index++)
        {
            if (!_shown.TryGetValue(index, out var shown))
            {
                shown = new Shown(index);
                _shown[index] = shown;
                _visuals.Add(shown.Visual);
                Paint(shown);
                OverlayOne(shown);
            }

            if (!_laid.ContainsKey(index) && !_laying.ContainsKey(index)) _ = LayAsync(index);
        }

        foreach (var shown in _shown.Values)
        {
            var origin = Origin(shown.Index);
            shown.Visual.Transform = new MatrixTransform(zoom, 0, 0, zoom, origin.X, origin.Y);
        }

        Current();
        InvalidateVisual();
    }

    /// <summary>The first page whose bottom is below <paramref name="y"/> (full-size coordinates).</summary>
    private int First(double y)
    {
        var found = Array.BinarySearch(_tops, 0, _sizes.Length, y);
        var at = found >= 0 ? found : Math.Max(0, ~found - 1);
        return Math.Min(at, Math.Max(0, _sizes.Length - 1));
    }

    /// <summary>
    /// Which page is being read: the one across the upper third of the viewport — or, scrolled to the very end, the last
    /// page whose top is in view, since a short last page never reaches that third.
    /// </summary>
    private void Current()
    {
        if (_sizes.Length == 0) return;
        var zoom = Zoom;
        var current = First((_offset.Y + _viewport.Height / 3) / zoom);

        if (_offset.Y >= _extent.Height - _viewport.Height - 1)
        {
            var bottom = (_offset.Y + _viewport.Height) / zoom;
            while (current + 1 < _sizes.Length && _tops[current + 1] < bottom) current++;
        }

        if (current != CurrentPage) SetValue(CurrentPageKey, current);
    }

    private async Task LayAsync(int index)
    {
        var source = Source;
        if (source is null) return;

        var cancel = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _laying[index] = cancel;
        LaidPage laid;
        try
        {
            laid = await source.LayAsync(index, cancel.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (_laying.TryGetValue(index, out var mine) && mine == cancel) _laying.Remove(index);
            cancel.Dispose();
        }

        if (!ReferenceEquals(source, Source)) return;

        Keep(index, laid);
        if (laid.Size != _sizes[index])
        {
            // The page is not the size its slot said. Everything below it moves, so the reader's view is held by the page
            // they are reading rather than by the scroll offset.
            var anchorPage = CurrentPage;
            var into = _offset.Y / Zoom - _tops[anchorPage];
            _sizes[index] = laid.Size;
            Arranged();
            SetVerticalOffset((_tops[anchorPage] + into) * Zoom);
        }

        if (_shown.TryGetValue(index, out var shown))
        {
            Paint(shown);
            OverlayOne(shown);
        }

        PageLaid?.Invoke(this, index);
    }

    private void Keep(int index, LaidPage laid)
    {
        _laid[index] = laid;
        _recent.Remove(index);
        _recent.AddFirst(index);
        while (_recent.Count > KeptLaid)
        {
            var oldest = _recent.Last!.Value;
            if (_shown.ContainsKey(oldest)) break;
            _recent.RemoveLast();
            _laid.Remove(oldest);
        }
    }

    /// <summary>The page laid, or the paper it will be laid on.</summary>
    private void Paint(Shown shown)
    {
        using var dc = shown.Page.RenderOpen();
        var size = _sizes[shown.Index];
        if (_laid.TryGetValue(shown.Index, out var laid))
            LayoutPainter.Paint(dc, laid.Tree.Root, Ink);
        else
            dc.DrawRectangle(Paper, null, new Rect(size));
    }

    private void Repaint()
    {
        foreach (var shown in _shown.Values) Paint(shown);
    }

    // ── Overlays: selection and search hits ──────────────────────────────────────────────────────────────────────

    private void Overlay()
    {
        foreach (var shown in _shown.Values) OverlayOne(shown);
    }

    private void OverlayOne(Shown shown)
    {
        using var dc = shown.Overlay.RenderOpen();
        if (!_laid.TryGetValue(shown.Index, out var laid)) return;
        var root = laid.Tree.Root;

        for (var i = 0; i < _highlights.Count; i++)
        {
            var hit = _highlights[i];
            if (hit.Page != shown.Index) continue;
            var wash = root.Wash([new EditRange(hit.Start, hit.Length)], 1);
            dc.DrawGeometry(i == _currentHighlight ? CurrentHighlight : Highlight, null, wash);
        }

        if (SelectedOn(shown.Index, laid.Text.Length) is { Length: > 0 } range)
            dc.DrawGeometry(Selection, null, root.Wash([new EditRange(range.Start, range.Length)], 1));
    }

    /// <summary>The share of the selection on page <paramref name="index"/>, whose text is <paramref name="length"/> long.</summary>
    private (int Start, int Length)? SelectedOn(int index, int length)
    {
        if (_anchor is not { } a || _focus is not { } f || a == f) return null;
        var (from, to) = a < f ? (a, f) : (f, a);
        if (index < from.Page || index > to.Page) return null;

        var start = index == from.Page ? Math.Clamp(from.Offset, 0, length) : 0;
        var end = index == to.Page ? Math.Clamp(to.Offset, 0, length) : length;
        return end > start ? (start, end - start) : null;
    }

    /// <summary>Washes <paramref name="hits"/> over the pages, the one at <paramref name="current"/> (or none) set apart.</summary>
    public void ShowHighlights(IReadOnlyList<PageRange> hits, int current)
    {
        _highlights = hits;
        _currentHighlight = current;
        Overlay();
    }

    /// <summary>Scrolls so <paramref name="range"/> is in view, laying its page first when it has to.</summary>
    public async Task RevealAsync(PageRange range)
    {
        if (range.Page < 0 || range.Page >= _sizes.Length) return;

        if (!_laid.TryGetValue(range.Page, out var laid))
        {
            GoToPage(range.Page);
            if (Source is not { } source) return;
            laid = await source.LayAsync(range.Page, _lifetime.Token);
            Keep(range.Page, laid);
        }

        var rects = laid.Tree.Root.RangeRects(range.Start, range.Length);
        if (rects.Count == 0) return;
        var where = rects[0];
        foreach (var rect in rects) where.Union(rect);
        GoToPage(range.Page, Math.Max(0, where.Top - _viewport.Height / Zoom / 3));
    }

    // ── Navigation and zoom ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Scrolls page <paramref name="index"/> to the top of the viewport, <paramref name="top"/> pixels into it. The page
    /// gone to is the page being read, even when the document is too short to scroll it to the top.
    /// </summary>
    public void GoToPage(int index, double top = 0)
    {
        if (_sizes.Length == 0) return;
        index = Math.Clamp(index, 0, _sizes.Length - 1);
        SetVerticalOffset((_tops[index] + Math.Clamp(top, 0, _sizes[index].Height)) * Zoom - PageGap);
        if (CurrentPage != index) SetValue(CurrentPageKey, index);
    }

    /// <summary>Zooms so the widest page fills the viewport's width.</summary>
    public void FitWidth()
    {
        if (_widest <= 0 || _viewport.Width <= 0) return;
        ZoomAround((_viewport.Width - 2 * PageGap - SystemParameters.VerticalScrollBarWidth) / _widest, null);
    }

    /// <summary>Zooms so the page being read fits the viewport whole.</summary>
    public void FitPage()
    {
        if (_sizes.Length == 0 || _viewport.Width <= 0 || _viewport.Height <= 0) return;
        var page = _sizes[CurrentPage];
        var zoom = Math.Min((_viewport.Width - 2 * PageGap) / page.Width, (_viewport.Height - 2 * PageGap) / page.Height);
        var index = CurrentPage;
        ZoomAround(zoom, null);
        GoToPage(index);
    }

    /// <summary>Zooms to <paramref name="zoom"/>, holding still the point of the document under <paramref name="at"/>.</summary>
    private void ZoomAround(double zoom, Point? at)
    {
        var before = Zoom;
        _zoomAnchor = at;
        Zoom = zoom;
        if (Zoom == before) _zoomAnchor = null;
    }

    private Point? _zoomAnchor;

    private void Zoomed(double before, Point? _)
    {
        var anchor = _zoomAnchor ?? new Point(_viewport.Width / 2, _viewport.Height / 3);
        _zoomAnchor = null;

        // The document point under the anchor, at the old zoom — in full-size coordinates, measured from the extent's origin.
        var docY = (_offset.Y + anchor.Y) / before;
        var docX = (_offset.X + anchor.X - PageGap) / before;

        Arranged();
        var zoom = Zoom;
        _offset = new Vector(docX * zoom + PageGap - anchor.X, docY * zoom - anchor.Y);
        ClampOffset();
        ScrollOwner?.InvalidateScrollInfo();
        Placed();
    }

    // ── Input ────────────────────────────────────────────────────────────────────────────────────────────────────

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        var point = e.GetPosition(this);
        _dragged = false;
        _pressedLink = null;

        if (!PageAt(point, out var index, out var local) || !_laid.TryGetValue(index, out var laid)) return;

        _pressedLink = Link(laid.Tree.Root, local);

        var place = new PagePlace(index, Offset(laid, local));
        if (e.ClickCount == 2 && Words(laid, local) is { } word)
        {
            _anchor = new PagePlace(index, word.Start);
            _focus = new PagePlace(index, word.End);
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && _anchor is not null)
        {
            _focus = place;
        }
        else
        {
            _anchor = _focus = place;
        }

        CaptureMouse();
        Selected();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var point = e.GetPosition(this);
        var over = PageAt(point, out var index, out var local) && _laid.TryGetValue(index, out var laid) ? laid : null;

        if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed && _anchor is not null)
        {
            if (over is not null)
            {
                var place = new PagePlace(index, Offset(over, local));
                if (place != _focus)
                {
                    _focus = place;
                    _dragged = true;
                    Selected();
                }
            }

            // Dragging past the viewport's edge scrolls on, a line at a time, as text does everywhere.
            if (point.Y < 0) SetVerticalOffset(_offset.Y - LineStep / 2);
            else if (point.Y > _viewport.Height) SetVerticalOffset(_offset.Y + LineStep / 2);
            return;
        }

        if (over is null)
        {
            Cursor = Cursors.Arrow;
            return;
        }

        var piece = over.Tree.Root.PieceAt(local);
        Cursor = Link(over.Tree.Root, local) is not null ? Cursors.Hand
            : piece is { Exists: true, Words: not null } ? Cursors.IBeam
            : Cursors.Arrow;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsMouseCaptured) ReleaseMouseCapture();

        if (!_dragged && _pressedLink is { } link)
        {
            _anchor = _focus;
            Selected();
            LinkActivated?.Invoke(this, link);
        }

        _pressedLink = null;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            ZoomAround(Zoom * (e.Delta > 0 ? ZoomStep : 1 / ZoomStep), e.GetPosition(this));
            e.Handled = true;
            return;
        }

        base.OnMouseWheel(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;

        var control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        switch (e.Key)
        {
            case Key.Home when control: GoToPage(0); break;
            case Key.End when control: GoToPage(_sizes.Length - 1); break;
            case Key.Home: SetVerticalOffset(0); break;
            case Key.End: SetVerticalOffset(_extent.Height); break;
            case Key.PageDown: PageDown(); break;
            case Key.PageUp: PageUp(); break;
            case Key.Down: LineDown(); break;
            case Key.Up: LineUp(); break;
            case Key.Left: LineLeft(); break;
            case Key.Right: LineRight(); break;
            case Key.OemPlus or Key.Add when control: ZoomAround(Zoom * ZoomStep, null); break;
            case Key.OemMinus or Key.Subtract when control: ZoomAround(Zoom / ZoomStep, null); break;
            default: return;
        }

        e.Handled = true;
    }

    /// <summary>
    /// What a press at <paramref name="local"/> follows, when it lands on a link: the last-placed piece there that answers a
    /// click by navigating, as the content engine finds what a press means — the uppermost link wins.
    /// </summary>
    private static string? Link(Piece root, Point local)
    {
        string? found = null;
        foreach (var (piece, where) in root.Placed())
            if (where.Contains(local) && piece.Acts?.For(LayoutGesture.Click) is { Verb: LayoutVerbs.Navigate, Target: { } target })
                found = target;
        return found;
    }

    /// <summary>The text offset a point on a laid page means: the letter it lands on, or the nearest place there is one.</summary>
    private static int Offset(LaidPage laid, Point local)
    {
        var root = laid.Tree.Root;
        var piece = root.PieceAt(local);
        return piece is { Exists: true, Words: { Maps: true } } ? root.OffsetAt(local) : root.StopNear(local);
    }

    /// <summary>The word under a point, for a double press.</summary>
    private static (int Start, int End)? Words(LaidPage laid, Point local)
    {
        var piece = laid.Tree.Root.PieceAt(local);
        if (piece is not { Exists: true, Words: { Maps: true } words, Part: { } part }) return null;
        var offset = laid.Tree.Root.OffsetAt(local) - part.Start;
        var (start, end) = words.WordAt(Math.Clamp(offset, 0, Math.Max(0, words.Length - 1)));
        return (part.Start + start, part.Start + end);
    }

    private void Selected()
    {
        Overlay();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>Selects every page's text.</summary>
    public void SelectAll()
    {
        if (_sizes.Length == 0) return;
        _anchor = new PagePlace(0, 0);
        _focus = new PagePlace(_sizes.Length - 1, int.MaxValue);
        Selected();
    }

    /// <summary>Clears the selection.</summary>
    public void ClearSelection()
    {
        _anchor = _focus = null;
        Selected();
    }

    /// <summary>
    /// The selected text, page after page, a line break between pages. Pages the selection spans that are not laid give
    /// their text through <see cref="IPagedLayoutSource.TextAsync"/>.
    /// </summary>
    public async Task<string> SelectedTextAsync()
    {
        if (_anchor is not { } a || _focus is not { } f || a == f || Source is not { } source) return string.Empty;
        var (from, to) = a < f ? (a, f) : (f, a);

        var text = new StringBuilder();
        for (var page = from.Page; page <= to.Page && page < _sizes.Length; page++)
        {
            var whole = _laid.TryGetValue(page, out var laid) ? laid.Text : await Text(source, page);
            if (SelectedOn(page, whole.Length) is not { } range) continue;
            if (text.Length > 0) text.Append('\n');
            text.Append(whole, range.Start, range.Length);
        }

        return text.ToString();
    }

    /// <summary>Page <paramref name="index"/>'s text, read once however many selections and searches ask for it.</summary>
    public Task<string> TextOfPageAsync(int index) =>
        Source is { } source ? Text(source, index) : Task.FromResult(string.Empty);

    private Task<string> Text(IPagedLayoutSource source, int index)
    {
        if (_laid.TryGetValue(index, out var laid)) return Task.FromResult(laid.Text);
        if (!_texts.TryGetValue(index, out var task))
        {
            task = source.TextAsync(index, _lifetime.Token);
            _texts[index] = task;
        }

        return task;
    }

    private async Task CopyAsync()
    {
        var text = await SelectedTextAsync();
        if (text.Length > 0) CopyRequested?.Invoke(this, text);
    }

    // ── Visuals and layout ───────────────────────────────────────────────────────────────────────────────────────

    protected override int VisualChildrenCount => _visuals.Count;

    protected override Visual GetVisualChild(int index) => _visuals[index];

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? _extent.Width : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? _extent.Height : availableSize.Height);

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (finalSize != _viewport)
        {
            _viewport = finalSize;
            ClampOffset();
            ScrollOwner?.InvalidateScrollInfo();
            Placed();
        }

        return finalSize;
    }

    protected override void OnRender(DrawingContext drawingContext) =>
        drawingContext.DrawRectangle(Background, null, new Rect(RenderSize));

    /// <summary>A page on screen: its drawing, and over it its selection and search hits.</summary>
    private sealed class Shown
    {
        public Shown(int index)
        {
            Index = index;
            Visual.Children.Add(Page);
            Visual.Children.Add(Overlay);
        }

        public int Index { get; }
        public ContainerVisual Visual { get; } = new();
        public DrawingVisual Page { get; } = new();
        public DrawingVisual Overlay { get; } = new();
    }

    // ── IScrollInfo ──────────────────────────────────────────────────────────────────────────────────────────────

    public bool CanVerticallyScroll { get; set; } = true;
    public bool CanHorizontallyScroll { get; set; } = true;
    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => _offset.X;
    public double VerticalOffset => _offset.Y;
    public ScrollViewer? ScrollOwner { get; set; }

    public void LineUp() => SetVerticalOffset(_offset.Y - LineStep);
    public void LineDown() => SetVerticalOffset(_offset.Y + LineStep);
    public void LineLeft() => SetHorizontalOffset(_offset.X - LineStep);
    public void LineRight() => SetHorizontalOffset(_offset.X + LineStep);
    public void PageUp() => SetVerticalOffset(_offset.Y - _viewport.Height * 0.9);
    public void PageDown() => SetVerticalOffset(_offset.Y + _viewport.Height * 0.9);
    public void PageLeft() => SetHorizontalOffset(_offset.X - _viewport.Width * 0.9);
    public void PageRight() => SetHorizontalOffset(_offset.X + _viewport.Width * 0.9);
    public void MouseWheelUp() => SetVerticalOffset(_offset.Y - LineStep * SystemParameters.WheelScrollLines / 3);
    public void MouseWheelDown() => SetVerticalOffset(_offset.Y + LineStep * SystemParameters.WheelScrollLines / 3);
    public void MouseWheelLeft() => SetHorizontalOffset(_offset.X - LineStep);
    public void MouseWheelRight() => SetHorizontalOffset(_offset.X + LineStep);

    public void SetHorizontalOffset(double offset)
    {
        _offset.X = offset;
        ClampOffset();
        ScrollOwner?.InvalidateScrollInfo();
        Placed();
    }

    public void SetVerticalOffset(double offset)
    {
        _offset.Y = offset;
        ClampOffset();
        ScrollOwner?.InvalidateScrollInfo();
        Placed();
    }

    public Rect MakeVisible(Visual visual, Rect rectangle) => rectangle;

    private void ClampOffset()
    {
        _offset.X = Math.Max(0, Math.Min(_offset.X, _extent.Width - _viewport.Width));
        _offset.Y = Math.Max(0, Math.Min(_offset.Y, _extent.Height - _viewport.Height));
    }
}
