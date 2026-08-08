using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Nexaflow.Features.Common.Search;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Read-only, drag-selectable markdown display. Renders <see cref="Markdown"/>
/// into a transparent read-only <see cref="RichTextBox"/> via
/// <see cref="MarkdownFlowDocument"/> so the user can select all or part of the
/// text. Copy (Ctrl+C or the right-click menu) routes through
/// <see cref="MarkdownClipboard"/>, putting plain text + rendered HTML + markdown
/// source on the clipboard at once.
/// </summary>
public class SelectableMarkdownView : UserControl
{
    private readonly RichTextBox _rtb;

    public SelectableMarkdownView()
    {
        _rtb = new RichTextBox
        {
            IsReadOnly                    = true,
            IsDocumentEnabled             = true,
            BorderThickness               = new Thickness(0),
            Background                    = Brushes.Transparent,
            Padding                       = new Thickness(0),
            VerticalScrollBarVisibility   = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Document                      = new FlowDocument(),
        };

        // Intercept Copy from both Ctrl+C and the default context menu.
        CommandManager.AddPreviewExecutedHandler(_rtb, OnPreviewExecuted);
        _rtb.ContextMenu = BuildContextMenu();

        // When the editor isn't scrolling its own content (auto-size mode), let
        // the wheel bubble to the host scroller instead of being swallowed.
        _rtb.PreviewMouseWheel += OnPreviewMouseWheel;

        // Interactive blocks (e.g. a music score) own their own click/drag. Mouse events never reach an
        // embedded UIElement island reliably (the text container attributes them to the container, the
        // document, or a neighbouring paragraph), so locate the element with a geometric visual hit-test
        // and drive the whole gesture (down/move/up) from here — same contract as the editor.
        _rtb.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        _rtb.PreviewMouseLeftButtonUp   += OnPreviewMouseLeftButtonUp;
        _rtb.PreviewMouseMove           += OnPreviewMouseMove;

        Background = Brushes.Transparent;
        Content    = _rtb;
    }

    private IInteractiveBlock? _pointerBlock;   // block owning the in-progress click/drag gesture

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var (block, element) = BlockAt(e.GetPosition(_rtb));
        if (block is null || element is null)
        {
            if (e.ClickCount == 1) InteractiveSelection.ClearActive();   // a click on text drops any block selection
            return;
        }

        // A double-click is the block's only if it says so; otherwise it stays with the host
        // (source-edit mode), which is what it has always meant.
        if (e.ClickCount > 1)
        {
            if (block.PointerDoubleClick(e.GetPosition(element!))) e.Handled = true;
            return;
        }

        _pointerBlock = block;
        block.BeginPointerSelect(e.GetPosition(element!));
        Mouse.Capture(_rtb);   // keep the drag flowing to our move/up handlers
        e.Handled = true;
    }

    /// <summary>The interactive block under <paramref name="pointInHost"/>, and the element to give
    /// it coordinates in.</summary>
    private (IInteractiveBlock? Block, UIElement? Element) BlockAt(Point pointInHost)
    {
        var hit = VisualTreeHelper.HitTest(_rtb, pointInHost)?.VisualHit;
        for (DependencyObject? d = hit; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is IInteractiveBlock ib and UIElement uie) return (ib, uie);
            if (ReferenceEquals(d, _rtb)) break;   // stop at the host
        }
        return (null, null);
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_pointerBlock is not { } pb) return;
        if (e.LeftButton != MouseButtonState.Pressed) { EndPointerGesture(); return; }
        pb.ExtendPointerSelect(e.GetPosition((UIElement)pb));
        e.Handled = true;
    }

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_pointerBlock is null) return;
        EndPointerGesture();
        e.Handled = true;
    }

    private void EndPointerGesture()
    {
        var pb = _pointerBlock;
        _pointerBlock = null;
        if (Mouse.Captured == _rtb) Mouse.Capture(null);
        pb?.EndPointerSelect();
    }

    // ── Markdown ────────────────────────────────────────────────────────────

    public static readonly DependencyProperty MarkdownProperty =
        DependencyProperty.Register(nameof(Markdown), typeof(string), typeof(SelectableMarkdownView),
            new PropertyMetadata(string.Empty, OnMarkdownChanged));

    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((SelectableMarkdownView)d).Rebuild();

    /// <summary>Colour scheme for rendering. When unset, follows the active theme via
    /// <see cref="MarkdownPalette.FromTheme"/> (light themes get dark text, etc.); set
    /// <see cref="MarkdownPalette.Light"/> for fixed light surfaces (e.g. scratchpad post-its).</summary>
    public static readonly DependencyProperty PaletteProperty =
        DependencyProperty.Register(nameof(Palette), typeof(MarkdownPalette), typeof(SelectableMarkdownView),
            new PropertyMetadata(null, (d, _) => ((SelectableMarkdownView)d).Rebuild()));

    public MarkdownPalette? Palette
    {
        get => (MarkdownPalette?)GetValue(PaletteProperty);
        set => SetValue(PaletteProperty, value);
    }

    /// <summary>In-app link handler. Return true to mark the link handled (the
    /// renderer then skips opening the OS browser). When null, links open externally.</summary>
    public Func<string, bool>? LinkNavigate { get; set; }

    /// <summary>
    /// A diagram node's expand chip was clicked. Return true to claim it — a host that generated the
    /// diagram re-emits <see cref="Markdown"/> with more of the tree walked. Left null, the diagram
    /// opens the node itself from what its source already describes.
    /// </summary>
    public Func<DiagramExpandRequest, bool>? DiagramExpand { get; set; }

    /// <summary>A diagram's selected node changed — for a host showing detail beside the diagram.
    /// The key is null when the selection was dropped.</summary>
    public Action<DiagramSelection>? DiagramSelect { get; set; }

    /// <summary>In a diagram, a single click selects a node and a double-click opens it. Set it on a
    /// pane where opening a node costs something the user may not have meant.</summary>
    public bool DiagramOpenOnDoubleClick { get; set; }

    /// <summary>A plain wheel over a diagram zooms it rather than scrolling this surface. Only for a
    /// pane whose whole content is the diagram — in a flowing document it would trap the wheel.</summary>
    public bool DiagramZoomOnWheel { get; set; }

    /// <summary>
    /// Height a diagram may take, for a pane that is entirely one diagram: bind it to the pane and
    /// the diagram fills it instead of running past the bottom with its own chrome — the minimap and
    /// the frame — off the end. Zero (the default) uses the built-in cap.
    /// </summary>
    public static readonly DependencyProperty MaxDiagramHeightProperty =
        DependencyProperty.Register(nameof(MaxDiagramHeight), typeof(double), typeof(SelectableMarkdownView),
            new PropertyMetadata(0.0, OnMaxDiagramHeightChanged));

    public double MaxDiagramHeight
    {
        get => (double)GetValue(MaxDiagramHeightProperty);
        set => SetValue(MaxDiagramHeightProperty, value);
    }

    // A pane resize walks this through every intermediate pixel; rebuilding the document on each
    // would be absurd, and a diagram does not care about a few pixels either way.
    private static void OnMaxDiagramHeightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (Math.Abs((double)e.NewValue - (double)e.OldValue) >= 24) ((SelectableMarkdownView)d).Rebuild();
    }

    /// <summary>Where each diagram's expansion, selection and pan/zoom live between renders — on the
    /// view rather than on the rendered element, which a host-driven re-emit replaces.</summary>
    private readonly DiagramViewStates _diagramStates = new();

    /// <summary>
    /// Forgets what the reader had opened, selected and zoomed to in every diagram here, so the next
    /// render starts fitted. For a host command that means "start over" — collapsing a whole tree —
    /// where keeping the old viewport would leave the reader zoomed in on something that is gone.
    /// </summary>
    public void ResetDiagramViews() => _diagramStates.Clear();

    /// <summary>Base directory for resolving relative <c>![](file.png)</c> image paths to a local
    /// file. When null, only absolute/<c>file:</c> images render (remote images stay text).</summary>
    public string? BaseDirectory { get; set; }

    /// <summary>When true, a diagram renders at full height (no inner scrollbar) and scales down to the
    /// control width instead of getting its own scrollbars — so only this surface's scrollbar moves. Off by
    /// default. Set it on surfaces that already scroll (e.g. the "As Code" structure panel).</summary>
    public bool FitContentToWidth { get; set; }

    /// <summary>When true (with <see cref="FitContentToWidth"/>), a too-wide diagram keeps its natural size and
    /// gets a horizontal scrollbar instead of being scaled down to fit — readable at full size. For read-only
    /// selectable surfaces where the scrollbar can be grabbed (the "As Code" structure panel).</summary>
    public bool ScrollWideDiagrams { get; set; }

    private void Rebuild()
    {
        _search?.Clear();
        _diagramStates.Rewind();
        _rtb.Document = MarkdownFlowDocument.Build(
            Markdown, new MarkdownRenderContext { Palette = Palette ?? MarkdownPalette.FromTheme(), OnNavigate = LinkNavigate, OnDiagramExpand = DiagramExpand, OnDiagramSelect = DiagramSelect, BaseDirectory = BaseDirectory, FitContentToWidth = FitContentToWidth, ScrollWideDiagrams = ScrollWideDiagrams, DiagramOpenOnDoubleClick = DiagramOpenOnDoubleClick, DiagramZoomOnWheel = DiagramZoomOnWheel, MaxDiagramHeight = MaxDiagramHeight, DiagramStates = _diagramStates });
    }

    // ── Search (rendered text) ────────────────────────────────────────────────

    private RenderedMarkdownSearch? _search;
    private RenderedMarkdownSearch Search => _search ??= new RenderedMarkdownSearch(_rtb);

    /// <summary>Highlights every match of <paramref name="matcher"/> in the rendered text and focuses the
    /// first. Returns the matches so the caller can report a count or hand ids to the model.</summary>
    public IReadOnlyList<RenderedMatch> FindInRendered(TextSearchMatcher matcher) => Search.Run(matcher);

    /// <summary>Removes the search highlights (no rebuild — scroll is preserved).</summary>
    public void ClearSearch() => _search?.Clear();

    /// <summary>Steps to the next (<paramref name="delta"/> = +1) or previous match.</summary>
    public void StepSearch(int delta) => _search?.Step(delta);

    /// <summary>Narrows the painted matches to the given ordinals; returns how many survived.</summary>
    public int RestrictSearch(IReadOnlySet<int> keep) => _search?.Restrict(keep) ?? 0;

    /// <summary>
    /// Inner editor's vertical scrollbar. Default <see cref="ScrollBarVisibility.Disabled"/>
    /// so the control auto-sizes to content (e.g. chat bubbles inside an outer
    /// scroller). Set to <see cref="ScrollBarVisibility.Auto"/> when the control
    /// fills a fixed region and should scroll itself (e.g. the response overlay).
    /// </summary>
    public ScrollBarVisibility VerticalScrollBarVisibility
    {
        get => _rtb.VerticalScrollBarVisibility;
        set => _rtb.VerticalScrollBarVisibility = value;
    }

    // ── Copy ──────────────────────────────────────────────────────────────

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        var copy = new MenuItem { Header = "Copy" };
        copy.Click += (_, _) => DoCopy();

        var selectAll = new MenuItem { Header = "Select All" };
        selectAll.Click += (_, _) => _rtb.SelectAll();

        menu.Items.Add(copy);
        menu.Items.Add(selectAll);
        return menu;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Ctrl+wheel is not a scroll request — it belongs to whatever is under the pointer (a diagram
        // zooms with it). Redirecting it to the host would swallow it before it ever got there.
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) return;

        // Likewise a block that claims the plain wheel: the interception below happens on the way
        // down, so a block that never gets asked never sees a wheel event at all.
        var (block, element) = BlockAt(e.GetPosition(_rtb));
        if (block is not null && element is not null && block.WantsPointerWheel(e.GetPosition(element)))
            return;

        if (e.Handled || _rtb.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled) return;

        e.Handled = true;
        if (Parent is UIElement host)
            host.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = MouseWheelEvent,
                Source      = this,
            });
    }

    private void OnPreviewExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Command != ApplicationCommands.Copy) return;
        if (DoCopy()) e.Handled = true;
    }

    private bool DoCopy()
    {
        try
        {
            MarkdownClipboard.CopySelection(_rtb.Selection, _rtb.Document, Markdown ?? string.Empty);
            return true;
        }
        catch
        {
            return false;   // fall back to the editor's default copy
        }
    }
}
