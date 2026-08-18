using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Nexaflow.Features.AIChat.ViewModels;
using Nexaflow.Features.Common;
using Page = Nexaflow.Features.Common.Page;

namespace Nexaflow.Features.AIChat.Views;

public partial class ConversationView : UserControl, IPageView
{
    public ConversationViewModel ViewModel { get; }
    IPageViewModel? IPageView.ViewModel => ViewModel;

    public ConversationView(ConversationViewModel viewModel)
    {
        InitializeComponent();
        ViewModel   = viewModel;
        DataContext = ViewModel;

        ViewModel.Timeline.CollectionChanged += OnTimelineChanged;
        ViewModel.PropertyChanged            += OnVmPropertyChanged;

        ContextBanner.Drop     += OnContextBannerDrop;
        ContextBanner.DragOver += OnContextBannerDragOver;

        // One menu per anchor: a ContextMenu becomes the logical child of whatever it's attached to,
        // so the banner and the add button can't share an instance.
        ContextBanner.ContextMenu    = NewContextSourceMenu();
        AddContextButton.ContextMenu = NewContextSourceMenu();

        RefreshFooter();
    }

    // ── Context-source menu (the [+] button, and right-clicking the banner) ─

    private ContextMenu NewContextSourceMenu()
    {
        var menu = new ContextMenu();
        menu.Opened += OnContextMenuOpened;
        return menu;
    }

    /// <summary>Drops the same menu below the [+] button. Right-click discovered nothing — the button is
    /// the visible half of the affordance, so it opens on a plain left click.</summary>
    private void OnAddContextClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement anchor || anchor.ContextMenu is not { } menu) return;
        menu.PlacementTarget = anchor;
        menu.Placement       = PlacementMode.Bottom;
        menu.IsOpen          = true;
    }

    /// <summary>Rebuilds the menu of addable context sources on each open, so it reflects
    /// current availability: the open tabs first (a submenu — the no-drag route to what the banner's drop
    /// target already accepts), then the context-free pages this workspace can create.</summary>
    private void OnContextMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        menu.Items.Clear();

        var tabs = ViewModel.AvailableOpenTabs;
        var openTabs = new MenuItem { Header = "Open tabs", IsEnabled = tabs.Count > 0 };
        foreach (var tab in tabs)
            openTabs.Items.Add(new MenuItem
            {
                Header           = Label(tab),
                Command          = ViewModel.AddOpenTabCommand,
                CommandParameter = tab,
            });
        menu.Items.Add(openTabs);

        var pages = ViewModel.AvailableContextPages;
        if (pages.Count == 0) return;

        menu.Items.Add(new Separator());
        foreach (var page in pages)
        {
            menu.Items.Add(new MenuItem
            {
                Header           = Label(page),
                Command          = ViewModel.AddContextPageCommand,
                CommandParameter = page,
            });
        }
    }

    private static string Label(Page page)
        => string.IsNullOrEmpty(page.Icon) ? page.Title : $"{page.Icon}  {page.Title}";

    public void Reinitialize(Dictionary<string, string> pageParams)
    {
        if (pageParams.TryGetValue("conversationId", out var id))
            _ = ViewModel.LoadAsync(id);
    }

    // ── Message rendering ─────────────────────────────────────────────────

    private void OnTimelineChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Dispatcher.Invoke(() => { ScrollToBottom(); RefreshFooter(); });

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConversationViewModel.EstimatedTokens)
                           or nameof(ConversationViewModel.ContextWindow))
            Dispatcher.Invoke(RefreshFooter);

        if (e.PropertyName == nameof(ConversationViewModel.IsPreviewOpen))
            Dispatcher.Invoke(UpdatePreviewColumn);

        if (e.PropertyName == nameof(ConversationViewModel.ScrollToTimelineIndex))
            Dispatcher.Invoke(ScrollToSearchHit);
    }

    /// <summary>Brings the search's current hit into view. The thread's ItemsControl isn't virtualized, so
    /// its container exists for every message — but it is generated on layout, hence the Loaded-priority
    /// post rather than reading it straight away.</summary>
    private void ScrollToSearchHit()
    {
        var index = ViewModel.ScrollToTimelineIndex;
        if (index < 0) return;

        Dispatcher.InvokeAsync(() =>
        {
            if (MessageList.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement container)
                container.BringIntoView();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>The width the preview had when last open, so a user's drag survives close/reopen within
    /// the session (default 35% on first open).</summary>
    private GridLength _lastPreviewWidth = new(35, GridUnitType.Star);

    /// <summary>
    /// Opens/closes the preview column. A ColumnDefinition's width isn't a bindable target worth a converter
    /// for one call site. MinWidth is toggled with it: a closed column must reach 0, but an open one should
    /// resist being dragged to nothing.
    /// </summary>
    private void UpdatePreviewColumn()
    {
        if (ViewModel.IsPreviewOpen)
        {
            PreviewColumn.MinWidth = 220;
            PreviewColumn.Width    = _lastPreviewWidth;
        }
        else
        {
            // Remember a real dragged width (not the collapsed 0) before tearing the column down.
            if (PreviewColumn.Width.IsStar && PreviewColumn.Width.Value > 0)
                _lastPreviewWidth = PreviewColumn.Width;
            PreviewColumn.MinWidth = 0;
            PreviewColumn.Width    = new GridLength(0);
        }
    }

    private void ScrollToBottom()
    {
        Dispatcher.InvokeAsync(MessageScroller.ScrollToEnd,
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void RefreshFooter()
    {
        var used = ViewModel.EstimatedTokens;
        var win  = ViewModel.ContextWindow;
        TokenFooter.Text = win is null
            ? $"{used:N0} tokens / unknown context"
            : $"{used:N0} / {win.Value:N0} tokens";
    }

    // ── Drop target ───────────────────────────────────────────────────────

    private void OnContextBannerDragOver(object sender, DragEventArgs e)
    {
        // TabStrip starts its drag with DragDropEffects.Move only — responding
        // with Copy would intersect to None and show the no-drop cursor. We
        // don't actually move the tab on drop, but the Effects flag has to
        // overlap with what the source permits.
        if (e.Data.GetDataPresent(typeof(Page)))
            e.Effects = DragDropEffects.Move;
        else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            e.Effects = DragDropEffects.Copy;
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void OnContextBannerDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(Page)))
        {
            if (e.Data.GetData(typeof(Page)) is Page page)
                ViewModel.AddContextItem(page);
            e.Handled = true;
            return;
        }
        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            foreach (var f in files) ViewModel.AddAttachment(f);
            e.Handled = true;
        }
    }
}
