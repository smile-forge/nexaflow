using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Nexaflow.Visuals.Common.Behaviors;

/// <summary>
/// Rubber-band ("marquee") selection for a multi-select <see cref="ListBox"/>/<see cref="ListView"/>:
/// press in the empty space no row occupies, drag, and every row the band crosses is selected.
/// Switch it on with <c>beh:MarqueeSelect.IsEnabled="True"</c>.
/// <para>
/// A press that lands on a row is left entirely alone — that gesture already means "select this, and
/// maybe drag it somewhere" — so this arms only where the list would otherwise have nothing to say.
/// Ctrl adds to the selection that was already there instead of replacing it.
/// </para>
/// <para>
/// The band is resolved to rows by index rather than by hit-testing (see <see cref="MarqueeRange"/>),
/// which is what lets it cover rows virtualisation has never realised, and horizontal extent is
/// ignored: a details-view row spans the full width, so a band that crosses one vertically crosses it
/// at all. Both together make a marquee here a run of rows, which is also what Explorer's details
/// view does.
/// </para>
/// </summary>
public static class MarqueeSelect
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled", typeof(bool), typeof(MarqueeSelect),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject element, bool value)
        => element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element)
        => (bool)element.GetValue(IsEnabledProperty);

    /// <summary>Per-list state, hung off the list itself so the behaviour stays static and stateless.</summary>
    private static readonly DependencyProperty ControllerProperty =
        DependencyProperty.RegisterAttached(
            "Controller", typeof(Controller), typeof(MarqueeSelect), new PropertyMetadata(null));

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox list) return;

        if (d.GetValue(ControllerProperty) is Controller running)
        {
            running.Detach();
            d.ClearValue(ControllerProperty);
        }

        if (e.NewValue is true)
            d.SetValue(ControllerProperty, new Controller(list));
    }

    private sealed class Controller
    {
        private readonly ListBox _list;

        private ScrollViewer?    _scroll;
        private ItemsPresenter?  _presenter;
        private BandAdorner?     _adorner;
        private AdornerLayer?    _layer;
        private DispatcherTimer? _autoScroll;

        private bool   _armed;
        private bool   _active;
        private bool   _additive;
        private Point  _pressedAt;             // list coordinates, for the drag threshold
        private Point  _current;               // list coordinates, latest pointer position
        private double _anchorDocY;            // where the press landed, in whole-list coordinates
        private int    _anchorIndex;
        private (int First, int Last)? _applied;
        private HashSet<object> _keep = [];    // the Ctrl-preserved selection; never unselected by the band

        public Controller(ListBox list)
        {
            _list = list;
            _list.PreviewMouseLeftButtonDown += OnPress;
            _list.PreviewMouseMove           += OnMove;
            _list.PreviewMouseLeftButtonUp   += OnRelease;
            _list.LostMouseCapture           += OnLostCapture;
        }

        public void Detach()
        {
            End();
            _list.PreviewMouseLeftButtonDown -= OnPress;
            _list.PreviewMouseMove           -= OnMove;
            _list.PreviewMouseLeftButtonUp   -= OnRelease;
            _list.LostMouseCapture           -= OnLostCapture;
        }

        // ── Gesture ───────────────────────────────────────────────────────────

        private void OnPress(object sender, MouseButtonEventArgs e)
        {
            _armed = false;
            if (e.ClickCount > 1) return;
            if (_list.SelectionMode == SelectionMode.Single) return;
            if (BlocksMarquee(e.OriginalSource as DependencyObject)) return;

            _scroll    = FindDescendant<ScrollViewer>(_list);
            _presenter = FindDescendant<ItemsPresenter>(_list);
            if (_scroll is null || _presenter is null || _list.Items.Count == 0) return;

            double rowHeight = RowHeight();
            if (rowHeight <= 0) return;

            int firstVisible = FirstVisibleIndex();
            double y = e.GetPosition(_presenter).Y;

            _anchorDocY  = y + firstVisible * rowHeight;
            _anchorIndex = MarqueeRange.IndexAt(y, rowHeight, firstVisible, _list.Items.Count);
            _pressedAt   = e.GetPosition(_list);
            _current     = _pressedAt;
            _additive    = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            _armed       = true;

            // The event is deliberately not handled: a host that clears the selection on a press into
            // empty space (the file browser does) must still get to run.
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (!_armed) return;
            if (e.LeftButton != MouseButtonState.Pressed) { End(); return; }

            _current = e.GetPosition(_list);

            if (!_active)
            {
                if (Math.Abs(_current.X - _pressedAt.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(_current.Y - _pressedAt.Y) < SystemParameters.MinimumVerticalDragDistance)
                    return;
                Begin();
            }

            Update();
            e.Handled = true;
        }

        private void OnRelease(object sender, MouseButtonEventArgs e)
        {
            bool wasActive = _active;
            End();
            if (wasActive) e.Handled = true;   // a band is not a click; nothing downstream should read it as one
        }

        private void OnLostCapture(object sender, MouseEventArgs e) => End();

        private void Begin()
        {
            _active  = true;
            _applied = null;
            _keep    = _additive ? [.. _list.SelectedItems.Cast<object>()] : [];

            if (!_additive) _list.SelectedItems.Clear();

            _list.CaptureMouse();

            _layer = AdornerLayer.GetAdornerLayer(_list);
            if (_layer is not null)
            {
                _adorner = new BandAdorner(_list);
                _layer.Add(_adorner);
            }

            _autoScroll = new DispatcherTimer(
                TimeSpan.FromMilliseconds(40), DispatcherPriority.Input, OnAutoScrollTick, _list.Dispatcher);
            _autoScroll.Start();
        }

        private void End()
        {
            _armed = false;
            if (!_active) return;
            _active = false;                       // before releasing capture: that raises LostMouseCapture

            _autoScroll?.Stop();
            _autoScroll = null;

            if (_adorner is not null) _layer?.Remove(_adorner);
            _adorner = null;
            _layer   = null;

            if (_list.IsMouseCaptured) _list.ReleaseMouseCapture();
        }

        // ── Band → selection ──────────────────────────────────────────────────

        private void Update()
        {
            if (_scroll is null || _presenter is null) return;

            double rowHeight = RowHeight();
            if (rowHeight <= 0) return;

            int firstVisible = FirstVisibleIndex();
            int count        = _list.Items.Count;

            double y = _list.TranslatePoint(_current, _presenter).Y;
            Apply(MarqueeRange.Resolve(
                _anchorIndex, MarqueeRange.IndexAt(y, rowHeight, firstVisible, count), count));

            // The anchor is pinned to the list, not to the screen, so an auto-scroll grows the band
            // rather than dragging it along.
            double anchorY = _presenter
                .TranslatePoint(new Point(0, _anchorDocY - firstVisible * rowHeight), _list).Y;

            var band = new Rect(new Point(_pressedAt.X, anchorY), _current);
            band.Intersect(PresenterBounds());
            _adorner?.Show(band);
        }

        /// <summary>
        /// Moves the selection to <paramref name="range"/>, touching only the rows that entered or
        /// left it — a band dragged down a long list would otherwise re-select everything it already
        /// had on every mouse move.
        /// </summary>
        private void Apply((int First, int Last)? range)
        {
            if (_applied == range) return;

            var selected = _list.SelectedItems;
            int count    = _list.Items.Count;

            if (_applied is { } was)
            {
                for (int i = was.First; i <= was.Last && i < count; i++)
                {
                    if (range is { } still && i >= still.First && i <= still.Last) continue;
                    var item = _list.Items[i];
                    if (!_keep.Contains(item)) selected.Remove(item);
                }
            }

            if (range is { } now)
            {
                for (int i = now.First; i <= now.Last && i < count; i++)
                {
                    if (_applied is { } had && i >= had.First && i <= had.Last) continue;
                    var item = _list.Items[i];
                    if (!_keep.Contains(item)) selected.Add(item);
                }
            }

            _applied = range;
        }

        private void OnAutoScrollTick(object? sender, EventArgs e)
        {
            if (!_active || _scroll is null || _presenter is null) return;

            double y = _list.TranslatePoint(_current, _presenter).Y;
            if (y < 0)                            _scroll.LineUp();
            else if (y > _presenter.ActualHeight) _scroll.LineDown();
            else return;

            Update();
        }

        // ── Measuring the list ────────────────────────────────────────────────

        /// <summary>
        /// The list scrolls by item (<c>CanContentScroll</c> plus the default
        /// <c>VirtualizingPanel.ScrollUnit</c>), so the offset already is an index. A list that
        /// scrolls by pixel reports something else entirely, hence the row-height division.
        /// </summary>
        private int FirstVisibleIndex()
        {
            if (_scroll is null) return 0;
            if (_scroll.CanContentScroll) return (int)_scroll.VerticalOffset;

            double rowHeight = RowHeight();
            return rowHeight > 0 ? (int)(_scroll.VerticalOffset / rowHeight) : 0;
        }

        /// <summary>Height of one row, read off whichever container is realised — they are uniform.</summary>
        private double RowHeight()
        {
            int first = _scroll is not null && _scroll.CanContentScroll ? (int)_scroll.VerticalOffset : 0;
            int last  = Math.Min(first + 4, _list.Items.Count);

            for (int i = first; i < last; i++)
                if (_list.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement { ActualHeight: > 0 } row)
                    return row.ActualHeight;

            return 0;
        }

        private Rect PresenterBounds()
        {
            if (_presenter is null) return new Rect(new Size(_list.ActualWidth, _list.ActualHeight));
            var origin = _presenter.TranslatePoint(new Point(0, 0), _list);
            return new Rect(origin, new Size(_presenter.ActualWidth, _presenter.ActualHeight));
        }

        /// <summary>
        /// True where a press already means something else: a row (click it, or drag it somewhere), a
        /// column header (sort or resize), or a scrollbar.
        /// </summary>
        private static bool BlocksMarquee(DependencyObject? source)
        {
            while (source is not null)
            {
                if (source is ListBoxItem or GridViewColumnHeader or ScrollBar or Thumb) return true;
                if (source is not Visual and not System.Windows.Media.Media3D.Visual3D) return false;
                source = VisualTreeHelper.GetParent(source);
            }
            return false;
        }

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T hit) return hit;
                if (FindDescendant<T>(child) is { } deeper) return deeper;
            }
            return null;
        }
    }

    /// <summary>
    /// The band itself. An adorner so no host has to make room for it, and because the adorner layer
    /// already clips to the list.
    /// </summary>
    private sealed class BandAdorner : Adorner
    {
        private Rect _band;

        public BandAdorner(UIElement adorned) : base(adorned) => IsHitTestVisible = false;

        public void Show(Rect band)
        {
            _band = band;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (_band.IsEmpty || _band.Width <= 0 || _band.Height <= 0) return;

            var accent = Application.Current?.TryFindResource("AccentBrush") as Brush
                         ?? SystemColors.HighlightBrush;

            var fill = (Brush)accent.Clone();
            fill.Opacity = 0.18;
            fill.Freeze();

            var edge = new Pen(accent, 1);
            edge.Freeze();

            // Half-pixel inset so the 1px edge lands on a device pixel instead of straddling two.
            var crisp = Rect.Inflate(_band, -0.5, -0.5);
            if (crisp.IsEmpty || crisp.Width <= 0 || crisp.Height <= 0)
            {
                dc.DrawRectangle(fill, null, _band);
                return;
            }

            dc.DrawRectangle(fill, edge, crisp);
        }
    }
}
