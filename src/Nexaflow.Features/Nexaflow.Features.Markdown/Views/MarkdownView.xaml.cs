using Nexaflow.Features.Common;
using Nexaflow.Features.Markdown.ViewModels;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using System;
using System.Collections.Generic;
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

        // A link out of the document is the shell's to route, the same as one pressed on a post-it or in a code
        // comment. Unhandled leaves it to open the way any other link does.
        Editor.LinkNavigate += (_, e) => e.Handled = viewModel.FollowLink(e.Url);

        // What a block's corner offers to save: its picture, as it is shown on the page, kept where the reader says.
        Editor.BlockSaving += OnBlockSaving;

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

        // In the split both halves show the one document, so they are held at the same place in it: whichever one
        // the reader moves says which character it now starts at, and the other is put there too.
        SourceBox.AddHandler(ScrollViewer.ScrollChangedEvent,
                             new ScrollChangedEventHandler((_, _) => Paired(fromSource: true)));
        Editor.PlaceChanged += (_, _) => Paired(fromSource: false);

        Focusable = true;

        // Opened from a snaplink with a heading → scroll there once the document has rendered + laid out.
        if (viewModel.InitialHeading is { Count: > 0 } heading)
            Loaded += (_, _) => Dispatcher.BeginInvoke(
                () => Editor.ScrollToHeading(heading),
                System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>What a block's picture is drawn on: the page it is shown on, so it reads the same wherever it is pasted.</summary>
    private Brush Ground => (Brush)FindResource("BgBrush");

    /// <summary>
    /// A picture of one block, kept where the reader says. Asked for by the block's own corner, which the engine draws and offers
    /// wherever the language it is written in says a picture of it is worth keeping.
    /// </summary>
    private void OnBlockSaving(object? sender, ContentBlockSavingEventArgs e)
    {
        if (e.Handled || Editor.CapturePicture(e.Block, Ground) is not { } picture) return;

        _ = ViewModel.SavePictureAsync(Png(picture), ContentNested.Language(e.Block));

        e.Handled = true;
    }

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

        // Arriving at the split, the half the reader was already on says where both of them stand. Queued at Loaded
        // priority because neither half has a layout to be anywhere in until the new column widths have been through one.
        if (ViewModel.ViewMode is MarkdownViewMode.Split)
            Dispatcher.BeginInvoke(() => Paired(fromSource: !towardsSource),
                                   System.Windows.Threading.DispatcherPriority.Loaded);
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

    // ── Split: both halves held at one place in the document ──────────────

    /// <summary>
    /// Holds the halves of the split at the same place in the document. A place in the source is what carries
    /// across, since it is the only thing the two of them agree on — ten lines of a diagram's source are one
    /// picture, and a paragraph that wraps twice in the raw box wraps once in the rendered one.
    /// </summary>
    /// <param name="fromSource">Which half moved, and so which of them the other is being put beside.</param>
    private void Paired(bool fromSource)
    {
        if (_pairing || !ViewModel.IsSplit) return;

        _pairing = true;
        try
        {
            var at = fromSource ? SourceTop() : Editor.ShownFrom;
            if (at < 0) return;

            var block = Editor.BlockAt(at);
            var moved = fromSource ? SourceShowsWhole(block) : Editor.ShowsWhole(block);
            var following = fromSource ? Editor.ShowsWhole(block) : SourceShowsWhole(block);

            // Lines carry across while both halves show the block as lines. Where the half that moved cannot show
            // the whole of its own drawing of the block and the other can, they do not: a diagram drawn a page and
            // a half tall was written in ten lines, and what the reader wants beside it is those ten lines, however
            // far into the picture they have read. So the other half is given the block rather than the line.
            if (!moved && following) at = block.Start;

            if (fromSource) Editor.ShowFrom(at);
            else            ShowSourceFrom(at);
        }
        finally
        {
            // The half just moved says so on the layout pass that move causes, and that report is this view's own
            // doing rather than the reader's. Input priority is below the pass, so it has been and gone by here.
            Dispatcher.BeginInvoke(() => _pairing = false, System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    /// <summary>Whether a half is being put where the other one stands, so its own report is not read as the reader moving.</summary>
    private bool _pairing;

    /// <summary>The character the raw box's top line starts at, or -1 before it has a line to be on.</summary>
    private int SourceTop()
    {
        var line = SourceBox.GetFirstVisibleLineIndex();

        return line < 0 ? -1 : SourceBox.GetCharacterIndexFromLineIndex(line);
    }

    /// <summary>Puts the line the character at <paramref name="offset"/> is on at the top of the raw box.</summary>
    private void ShowSourceFrom(int offset)
    {
        if (offset < 0 || offset > SourceBox.Text.Length) return;

        var at = SourceBox.GetRectFromCharacterIndex(offset);
        if (at.IsEmpty) return;

        SourceBox.ScrollToVerticalOffset(SourceBox.VerticalOffset + at.Top - SourceBox.Padding.Top);
    }

    /// <summary>Whether the raw box can show the whole of a stretch of source at once.</summary>
    private bool SourceShowsWhole((int Start, int Length) block)
    {
        if (block.Length <= 0 || SourceBox.ViewportHeight <= 0) return false;

        var opens = SourceBox.GetRectFromCharacterIndex(block.Start);
        var closes = SourceBox.GetRectFromCharacterIndex(Math.Min(block.Start + block.Length, SourceBox.Text.Length));

        return !opens.IsEmpty && !closes.IsEmpty && closes.Bottom - opens.Top <= SourceBox.ViewportHeight;
    }

    // ── IPageView ─────────────────────────────────────────────────────────

    IPageViewModel? IPageView.ViewModel => ViewModel;
}
