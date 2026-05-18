using Nexaflow.Features.Common;
using Nexaflow.Features.Markdown.Markdown;
using Nexaflow.Features.Markdown.ViewModels;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Nexaflow.Features.Markdown.Views;

public partial class MarkdownView : UserControl, IPageView
{
    public MarkdownViewModel ViewModel { get; }

    // Keeps block model → its container Border so we can reuse containers
    // across re-parses without destroying TextBoxes that are mid-edit.
    private readonly Dictionary<MarkdownBlockModel, Border> _containers = new();

    // ── Construction ──────────────────────────────────────────────────────

    public MarkdownView(MarkdownViewModel viewModel)
    {
        InitializeComponent();
        ViewModel   = viewModel;
        DataContext = viewModel;

        Loaded += (_, _) =>
        {
            viewModel.Blocks.CollectionChanged += OnBlocksChanged;
            RebuildPanel();
        };

        // Ctrl+S → save
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (viewModel.SaveCommand.CanExecute(null))
                    viewModel.SaveCommand.Execute(null);
                e.Handled = true;
            }
        };

        Focusable = true;
    }

    // ── Collection change handler ─────────────────────────────────────────

    private void OnBlocksChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RebuildPanel();

    // ── Panel rebuild ─────────────────────────────────────────────────────

    /// <summary>
    /// Synchronises the StackPanel with the current Blocks list.
    /// Reuses existing container Borders for blocks that are still present
    /// (preserving any live TextBox inside them).
    /// </summary>
    private void RebuildPanel()
    {
        // Purge container cache for blocks no longer in the list
        var active = new HashSet<MarkdownBlockModel>(ViewModel.Blocks);
        foreach (var stale in _containers.Keys.Except(active).ToList())
            _containers.Remove(stale);

        BlocksPanel.Children.Clear();

        foreach (var block in ViewModel.Blocks)
        {
            if (!_containers.TryGetValue(block, out var container))
            {
                container = BuildContainer(block);
                _containers[block] = container;
            }
            BlocksPanel.Children.Add(container);
        }
    }

    // ── Block container ───────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="Border"/> that wraps a block and switches its child
    /// between the rendered view and a source TextBox based on
    /// <see cref="MarkdownBlockModel.IsEditing"/>.
    /// </summary>
    private Border BuildContainer(MarkdownBlockModel block)
    {
        var container = new Border();

        void SwitchContent()
        {
            container.Child = block.IsEditing
                ? BuildSourceView(block)
                : BuildRenderedView(block);
        }

        block.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MarkdownBlockModel.IsEditing))
                Dispatcher.Invoke(SwitchContent);
        };

        SwitchContent();
        return container;
    }

    // ── Rendered view ─────────────────────────────────────────────────────

    private FrameworkElement BuildRenderedView(MarkdownBlockModel block)
    {
        FrameworkElement element;
        try   { element = BlockRenderer.Render(block.ParsedBlock, block.RawMarkdown); }
        catch { element = FallbackTextBlock(block.RawMarkdown); }

        element.Cursor = Cursors.IBeam;

        // Left-click → enter edit mode for this block
        element.PreviewMouseLeftButtonDown += (_, e) =>
        {
            ViewModel.BeginEdit(block);
            e.Handled = true;
        };

        // Context menu: "Show source" pins the block to source mode
        element.ContextMenu = MakeRenderedContextMenu(block);

        return element;
    }

    private ContextMenu MakeRenderedContextMenu(MarkdownBlockModel block)
    {
        var menu    = new ContextMenu();
        var showSrc = new MenuItem { Header = "Show source" };
        showSrc.Click += (_, _) => ViewModel.TogglePin(block);
        menu.Items.Add(showSrc);
        return menu;
    }

    // ── Source view ───────────────────────────────────────────────────────

    private static readonly Brush SourceTextBrush  = Frozen(Color.FromRgb(0xE8, 0xEA, 0xF2));
    private static readonly Brush SourceBgBrush    = Frozen(Color.FromRgb(0x0D, 0x10, 0x1A));
    private static readonly Brush SourceBorderBrush= Frozen(Color.FromRgb(0x4F, 0x8E, 0xF7));
    private static readonly Brush SourceCaretBrush = Frozen(Color.FromRgb(0x4F, 0x8E, 0xF7));

    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    private Border BuildSourceView(MarkdownBlockModel block)
    {
        var tb = new TextBox
        {
            Text         = block.RawMarkdown,
            AcceptsReturn = true,
            AcceptsTab    = true,
            TextWrapping  = TextWrapping.NoWrap,
            FontFamily    = new FontFamily("Consolas, Courier New"),
            FontSize      = 13,
            Foreground    = SourceTextBrush,
            Background    = SourceBgBrush,
            CaretBrush    = SourceCaretBrush,
            BorderThickness = new Thickness(0),
            Padding         = new Thickness(8, 6, 8, 6),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Disabled,
        };

        // Suppress spell-check squiggles
        SpellCheck.SetIsEnabled(tb, false);

        // Context menu: "Render" / "Stop editing" — unpins if pinned
        tb.ContextMenu = MakeSourceContextMenu(block);

        // Propagate text edits to the view-model (no re-render while typing)
        tb.TextChanged += (_, _) => ViewModel.OnBlockTextEdited(block, tb.Text);

        // On blur: if not pinned, exit source mode and re-render
        tb.LostFocus += (_, _) => ViewModel.OnBlockBlurred(block);

        // Ctrl+S should still save even when a TextBox has focus
        tb.KeyDown += (_, e) =>
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (ViewModel.SaveCommand.CanExecute(null))
                    ViewModel.SaveCommand.Execute(null);
                e.Handled = true;
            }
        };

        // Give the TextBox focus after the layout pass
        Dispatcher.BeginInvoke(() =>
        {
            tb.Focus();
            tb.CaretIndex = tb.Text.Length;
        });

        return new Border
        {
            Background      = SourceBgBrush,
            BorderBrush     = SourceBorderBrush,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Margin          = new Thickness(0, 2, 0, 2),
            Child           = tb
        };
    }

    private ContextMenu MakeSourceContextMenu(MarkdownBlockModel block)
    {
        var menu = new ContextMenu();

        // Header changes dynamically: "Render" if pinned, "Stop editing" if just clicked
        var item = new MenuItem();
        menu.Opened += (_, _) =>
            item.Header = block.IsPinned ? "Render" : "Stop editing";
        item.Click += (_, _) =>
        {
            if (block.IsPinned)
                ViewModel.TogglePin(block);  // unpin + render
            else
            {
                block.IsEditing = false;     // just exit without pinning
                ViewModel.OnBlockBlurred(block);
            }
        };
        menu.Items.Add(item);

        // Always offer "Pin to source" when not yet pinned
        var pin = new MenuItem { Header = "Pin to source" };
        pin.Click += (_, _) =>
        {
            if (!block.IsPinned) ViewModel.TogglePin(block);
        };
        menu.Opened += (_, _) => pin.Visibility =
            block.IsPinned ? Visibility.Collapsed : Visibility.Visible;
        menu.Items.Add(pin);

        return menu;
    }

    // ── IPageView ─────────────────────────────────────────────────────────

    IPageViewModel? IPageView.ViewModel => ViewModel;

    // ── Helpers ───────────────────────────────────────────────────────────

    private static TextBlock FallbackTextBlock(string text) =>
        new()
        {
            Text         = text,
            Foreground   = new SolidColorBrush(Color.FromRgb(0x78, 0x80, 0xA0)),
            FontSize     = 13.5,
            TextWrapping = TextWrapping.Wrap
        };
}
