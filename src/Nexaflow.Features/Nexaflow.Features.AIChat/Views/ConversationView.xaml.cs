using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Nexaflow.Features.AIChat.ViewModels;
using Nexaflow.Features.Common;
using Nexaflow.Visuals.Text.Markdown;
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

        ViewModel.Messages.CollectionChanged += OnMessagesChanged;
        ViewModel.PropertyChanged            += OnVmPropertyChanged;

        ContextBanner.Drop     += OnContextBannerDrop;
        ContextBanner.DragOver += OnContextBannerDragOver;

        RebuildMessages();
        RefreshFooter();
    }

    public void Reinitialize(Dictionary<string, string> pageParams)
    {
        if (pageParams.TryGetValue("conversationId", out var id))
            _ = ViewModel.LoadAsync(id);
    }

    // ── Message rendering ─────────────────────────────────────────────────

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Always rebuild — message edits are rare and the list is small.
        Dispatcher.Invoke(() =>
        {
            RebuildMessages();
            ScrollToBottom();
            RefreshFooter();
        });
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConversationViewModel.EstimatedTokens)
                           or nameof(ConversationViewModel.ContextWindow))
            Dispatcher.Invoke(RefreshFooter);
    }

    private void RebuildMessages()
    {
        MessageList.Items.Clear();
        foreach (var msg in ViewModel.Messages)
            MessageList.Items.Add(BuildMessageElement(msg));
    }

    private static FrameworkElement BuildMessageElement(ConversationMessage msg)
    {
        // Selectable markdown — drag-select all or part and copy (Ctrl+C / right-click)
        // in plain text + HTML + markdown formats.
        var body = new SelectableMarkdownView { Markdown = msg.Text ?? string.Empty };

        var bubble = new Border
        {
            Padding         = new Thickness(14, 10, 14, 10),
            Margin          = new Thickness(0, 6, 0, 6),
            CornerRadius    = msg.IsUser
                ? new CornerRadius(14, 14, 4, 14)
                : new CornerRadius(14, 14, 14, 4),
            MaxWidth            = 720,
            HorizontalAlignment = msg.IsUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Child               = body,
        };

        if (msg.IsUser)
        {
            bubble.SetResourceReference(Border.BackgroundProperty, "UserBubbleBrush");
        }
        else
        {
            // Use a resource brush at runtime so theme changes propagate.
            bubble.SetResourceReference(Border.BackgroundProperty, "Surface2Brush");
            bubble.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            bubble.BorderThickness = new Thickness(1);
        }

        return bubble;
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
