using Nexaflow.Features.Common;
using Nexaflow.Features.Markdown.ViewModels;
using Nexaflow.Visuals.Text.Markdown;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nexaflow.Features.Markdown.Views;

public partial class MarkdownView : UserControl, IPageView
{
    public MarkdownViewModel ViewModel { get; }

    // ── Construction ──────────────────────────────────────────────────────

    public MarkdownView(MarkdownViewModel viewModel)
    {
        InitializeComponent();
        ViewModel   = viewModel;
        DataContext = viewModel;

        // Resolve relative ![](img.png) images against the file's own folder.
        Editor.BaseDirectory = Path.GetDirectoryName(viewModel.FilePath);

        // A rendered block's own toolbar: its picture, as it is shown on the page, copied or saved.
        Editor.BlockActions =
        [
            new BlockAction("Copy", "Markdown_BlockCopyPicture", CopyPicture) { ToolTip = "Copy as a picture" },
            new BlockAction("Save", "Markdown_BlockSavePicture", block => _ = SavePictureAsync(block)) { ToolTip = "Save as a PNG picture" },
        ];

        // Lay the surfaces out for the mode chosen, and move focus to whichever one that reveals.
        viewModel.PropertyChanged += OnViewModelChanged;

        // Search collaboration: the rendered surface (the inline editor) owns its own highlighting; the
        // source box's match is shown by selecting it. The VM decides which is active.
        viewModel.FindInRendered        = Editor.FindInRendered;
        viewModel.StepRendered          = Editor.StepSearch;
        viewModel.ClearRendered         = Editor.ClearSearch;
        viewModel.RenderedMarkPositions = Editor.SearchMarkPositions;
        viewModel.SourceSelectionRequested += SelectInSource;
        viewModel.PropertyChanged += OnSearchPropertyChanged;
        Unloaded += (_, _) =>
        {
            viewModel.SourceSelectionRequested -= SelectInSource;
            viewModel.PropertyChanged -= OnSearchPropertyChanged;
        };

        // Ctrl+S → save (fires in either mode — it's on the UserControl).
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (viewModel.SaveCommand.CanExecute(null))
                    viewModel.SaveCommand.Execute(null);
                e.Handled = true;
            }
        };

        // Ctrl+wheel zooms over either surface. Preview, so it beats the surface's own scroll handling; a
        // plain wheel is left alone and still scrolls.
        PreviewMouseWheel += (_, e) => e.Handled = viewModel.Zoom.TryWheel(e);

        Focusable = true;

        // Opened from a snaplink with a heading → scroll there once the document has rendered + laid out.
        if (viewModel.InitialHeading is { Count: > 0 } heading)
            Loaded += (_, _) => Dispatcher.BeginInvoke(
                () => Editor.ScrollToHeading(heading),
                System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>What a block's picture is drawn on: the page it is shown on, so it reads the same wherever it is pasted.</summary>
    private Brush Ground => (Brush)FindResource("BgBrush");

    /// <summary>A block's picture onto the clipboard — as a picture, and as a PNG for whatever reads one.</summary>
    private void CopyPicture(RenderedBlock block)
    {
        var picture = block.Picture(Ground);

        var data = new DataObject();
        data.SetImage(picture);
        data.SetData("PNG", new MemoryStream(Png(picture)));

        try { Clipboard.SetDataObject(data, copy: true); }
        catch (COMException) { /* the clipboard is held by something else for the moment; pressing again will do */ }
    }

    private Task SavePictureAsync(RenderedBlock block) => ViewModel.SavePictureAsync(Png(block.Picture(Ground)), block.Language);

    private static byte[] Png(BitmapSource picture)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(picture));

        using var png = new MemoryStream();
        encoder.Save(png);
        return png.ToArray();
    }

    /// <summary>
    /// Follows the view-mode slider: lays the surfaces out, then puts the caret where the slider was going.
    /// Sliding toward Source lands in the raw box and back toward Rendered in the rendered editor — which in
    /// the split is whichever half just appeared — so typing works the moment the pane arrives.
    /// </summary>
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MarkdownViewModel.ViewMode)) return;

        var towardsSource = ViewModel.ViewMode > _laidOut;
        ApplyViewMode();
        Dispatcher.BeginInvoke(() =>
        {
            if (towardsSource) SourceBox.Focus();
            else               Editor.Focus();
        });
    }

    /// <summary>What the columns were last laid out for. The XAML starts them on <see cref="MarkdownViewMode.Rendered"/>.</summary>
    private MarkdownViewMode _laidOut = MarkdownViewMode.Rendered;

    /// <summary>The split's ratio, kept while it is not showing, so returning to the split lands where the
    /// drag handle was left rather than back at half and half.</summary>
    private GridLength _sourceShare   = new(1, GridUnitType.Star);
    private GridLength _renderedShare = new(1, GridUnitType.Star);

    private static readonly GridLength Gone  = new(0);
    private static readonly GridLength Whole = new(1, GridUnitType.Star);

    /// <summary>
    /// Gives the columns the widths the chosen mode wants: one surface filling the pane, or both of them
    /// either side of the drag handle. A hidden surface's column goes to zero — collapsing the surface alone
    /// would leave its share of the width behind as a gap.
    /// </summary>
    private void ApplyViewMode()
    {
        var mode = ViewModel.ViewMode;
        if (mode == _laidOut) return;

        if (_laidOut is MarkdownViewMode.Split)
        {
            _sourceShare   = SourceColumn.Width;
            _renderedShare = RenderedColumn.Width;
        }
        _laidOut = mode;

        SourceColumn.Width = mode switch
        {
            MarkdownViewMode.Source => Whole,
            MarkdownViewMode.Split  => _sourceShare,
            _                       => Gone,
        };
        RenderedColumn.Width = mode switch
        {
            MarkdownViewMode.Rendered => Whole,
            MarkdownViewMode.Split    => _renderedShare,
            _                         => Gone,
        };
        SplitterColumn.Width = mode is MarkdownViewMode.Split ? GridLength.Auto : Gone;
    }

    // The rendered surface reports its match positions only after it has laid out, so read them on a queued
    // pass rather than the instant the count changes.
    private void OnSearchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MarkdownViewModel.MiniMapMarks)) return;
        MiniMapCanvas.Marks = ViewModel.MiniMapMarks;
    }

    /// <summary>Selects a span of the raw source and scrolls it into view. Queued at Loaded priority because
    /// the search switches to the source surface in the same beat — the box has no layout until that lands.</summary>
    private void SelectInSource(int offset, int length)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var max = SourceBox.Text.Length;
            if (offset < 0 || offset > max) return;

            SourceBox.Focus();
            SourceBox.Select(offset, Math.Min(length, max - offset));
            SourceBox.ScrollToLine(SourceBox.GetLineIndexFromCharacterIndex(offset));
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // ── IPageView ─────────────────────────────────────────────────────────

    IPageViewModel? IPageView.ViewModel => ViewModel;
}
