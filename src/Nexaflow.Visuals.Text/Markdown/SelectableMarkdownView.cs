using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

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
        if (e.ClickCount != 1) return;
        var hit = VisualTreeHelper.HitTest(_rtb, e.GetPosition(_rtb))?.VisualHit;
        for (DependencyObject? d = hit; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is IInteractiveBlock ib && ib is UIElement uie)
            {
                _pointerBlock = ib;
                ib.BeginPointerSelect(e.GetPosition(uie));
                Mouse.Capture(_rtb);   // keep the drag flowing to our move/up handlers
                e.Handled = true;
                return;
            }
            if (ReferenceEquals(d, _rtb)) break;   // stop at the host
        }
        InteractiveSelection.ClearActive();        // a click on text drops any music selection
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

    private void Rebuild() => _rtb.Document = MarkdownFlowDocument.Build(
        Markdown, new MarkdownRenderContext { Palette = Palette ?? MarkdownPalette.FromTheme(), OnNavigate = LinkNavigate, BaseDirectory = BaseDirectory, FitContentToWidth = FitContentToWidth, ScrollWideDiagrams = ScrollWideDiagrams });

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
