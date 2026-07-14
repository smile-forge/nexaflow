using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
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

        var contextMenu = new ContextMenu();
        contextMenu.Opened += OnContextMenuOpened;
        ContextBanner.ContextMenu = contextMenu;

        RefreshFooter();
    }

    // ── Context-source menu (right-click the banner) ──────────────────────

    /// <summary>Rebuilds the right-click menu of addable context pages on each open, so it reflects
    /// current availability.</summary>
    private void OnContextMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        menu.Items.Clear();

        var pages = ViewModel.AvailableContextPages;
        if (pages.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "No context sources available", IsEnabled = false });
            return;
        }

        foreach (var page in pages)
        {
            menu.Items.Add(new MenuItem
            {
                Header           = string.IsNullOrEmpty(page.Icon) ? page.Title : $"{page.Icon}  {page.Title}",
                Command          = ViewModel.AddContextPageCommand,
                CommandParameter = page,
            });
        }
    }

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
    }

    /// <summary>A ColumnDefinition's width isn't a bindable target worth a converter for one call site —
    /// the panel is 35% of the width when open and collapsed to nothing when not.</summary>
    private void UpdatePreviewColumn()
        => PreviewColumn.Width = ViewModel.IsPreviewOpen
            ? new GridLength(35, GridUnitType.Star)
            : new GridLength(0);

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
