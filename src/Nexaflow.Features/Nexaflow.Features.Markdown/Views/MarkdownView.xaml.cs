using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;
using Nexaflow.Features.Common;
using Nexaflow.Features.Markdown.ViewModels;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Editor;
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

        // Resolve relative ![](img.png) images against the document's folder — the file's own, or where a package is unpacked.
        Editor.BaseDirectory = viewModel.DocumentFolder;

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
        viewModel.RenderedFound         = () => (Editor.Found, Editor.At);
        viewModel.StepRendered          = Editor.StepSearch;
        viewModel.ClearRendered         = Editor.ClearSearch;
        viewModel.RenderedMarkPositions = Editor.SearchMarkPositions;
        viewModel.SourceSelectionRequested += SelectInSource;
        viewModel.PropertyChanged += OnSearchPropertyChanged;

        // Laid out again — folded, opened, laid further, written in — so where the matches stand is read again.
        Editor.PreRender += (_, _) => viewModel.RenderedLaidOut();

        // The raw half: the document's text, kept in step with it both ways; the theme's caret; and the wash a search paints.
        TakeDocument(viewModel.Markdown ?? string.Empty);
        SourceBox.Document.UndoStack.ClearAll();
        SourceBox.TextChanged += (_, _) => TakeSourceEdit();
        SourceBox.TextArea.Caret.CaretBrush = (Brush)FindResource("AccentBrush");
        SourceBox.TextArea.TextView.BackgroundRenderers.Add(_sourceWash);
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

        // Ctrl+P → print, likewise in either mode: both surfaces hold the one document.
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.P || Keyboard.Modifiers != ModifierKeys.Control) return;

            Print();
            e.Handled = true;
        };

        // Ctrl+Shift+S → save as, likewise in either mode.
        InputBindings.Add(new KeyBinding(viewModel.SaveAsCommand, Key.S, ModifierKeys.Control | ModifierKeys.Shift));

        // Ctrl+wheel zooms over either surface. Preview, so it beats the surface's own scroll handling; a
        // plain wheel is left alone and still scrolls.
        PreviewMouseWheel += (_, e) => e.Handled = viewModel.Zoom.TryWheel(e);

        // In the split both halves show the one document, so they are held at the same place in it: whichever one
        // the reader moves says which character it now starts at, and the other is put there too.
        SourceBox.TextArea.TextView.ScrollOffsetChanged += (_, _) => Paired(fromSource: true);
        Editor.PlaceChanged += (_, _) => Paired(fromSource: false);

        Focusable = true;

        // Opened from a snaplink with a heading → scroll there once the document has rendered + laid out.
        if (viewModel.InitialHeading is { Count: > 0 } heading)
            Loaded += (_, _) => Dispatcher.BeginInvoke(
                () => Editor.ScrollToHeading(heading),
                System.Windows.Threading.DispatcherPriority.Loaded);

        // Opened at a saved reference — a summary's heading leading back here → land there once laid out.
        if (viewModel.InitialPlace is { } place)
            Loaded += (_, _) => Dispatcher.BeginInvoke(
                () => Editor.GoTo(ContentPath.Read(place)),
                System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>
    /// The tab, already showing its document, asked to land somewhere in it: at a saved reference, or at a heading named by its
    /// titles. Asked before the document is first laid out, it waits for that (see the constructor).
    /// </summary>
    public void Reinitialize(Dictionary<string, string> pageParams)
    {
        if (!IsLoaded) return;

        if (pageParams.GetValueOrDefault(MarkdownTabRegistration.PlaceParameter) is { Length: > 0 } place)
            Editor.GoTo(ContentPath.Read(place));
        else if (pageParams.GetValueOrDefault("heading") is { Length: > 0 } heading)
            Editor.ScrollToHeading(heading.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
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

    private void Print_Click(object sender, RoutedEventArgs e) => Print();

    /// <summary>
    /// The document on paper: the printer and its sheet are asked for, then the whole document is laid out for that sheet and
    /// printed as it is drawn here — whichever surface is showing, since both hold the one document.
    /// </summary>
    private void Print()
    {
        var dialog = new PrintDialog { UserPageRangeEnabled = false };
        if (dialog.ShowDialog() != true) return;

        try
        {
            dialog.PrintDocument(Editor.Paginator(new Size(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight)), ViewModel.FileName);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ViewModel.PrintFailed(ex.Message);
        }
    }

    /// <summary>
    /// Follows the view-mode slider: lays the surfaces out, then puts the caret where the slider was going.
    /// Sliding toward Source lands in the raw box and back toward Rendered in the rendered editor — which in
    /// the split is whichever half just appeared — so typing works the moment the pane arrives. The reader stays where they
    /// were in the document, and a live search goes on from the match they were on.
    /// </summary>
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Written in the rendered half, or read from the file again: the raw half is given it.
        if (e.PropertyName == nameof(MarkdownViewModel.Markdown))
        {
            TakeDocument(ViewModel.Markdown ?? string.Empty);
            return;
        }

        // Saved as a file somewhere else: its pictures are read beside that one now.
        if (e.PropertyName == nameof(MarkdownViewModel.DocumentFolder))
        {
            Editor.BaseDirectory = ViewModel.DocumentFolder;
            return;
        }
        if (e.PropertyName != nameof(MarkdownViewModel.ViewMode)) return;

        var was = _laidOut;
        var towardsSource = ViewModel.ViewMode > was;

        // Read before the columns change: the surface going away has no layout left to say it from afterwards.
        var at = ReaderAt();
        var fromSource = was is MarkdownViewMode.Source || (was is MarkdownViewMode.Split && _ledBySource);

        ApplyViewMode();
        Dispatcher.BeginInvoke(() =>
        {
            if (towardsSource) SourceBox.Focus();
            else               Editor.Focus();
        });

        // Neither surface has a layout to be anywhere in until the new column widths have been through one, so the reader is
        // put back at Loaded priority. A search carried across goes on after that, once the halves have let go of each other
        // (ArriveAt): bringing its match into view moves a half, which the other then follows as it follows the reader.
        Dispatcher.BeginInvoke(() =>
        {
            ArriveAt(at, fromSource);
            Dispatcher.BeginInvoke(ViewModel.CarrySearchToShownSurface, System.Windows.Threading.DispatcherPriority.Background);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
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

    // The marks, and the places the raw half washes, come from the view-model whenever the search or the page under it moves.
    private void OnSearchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MarkdownViewModel.MiniMapMarks):
                MiniMapCanvas.Marks = ViewModel.MiniMapMarks;
                break;

            case nameof(MarkdownViewModel.SourceSearch):
                (_sourceWash.Highlights, _sourceWash.Current) = ViewModel.SourceSearch;
                SourceBox.TextArea.TextView.InvalidateLayer(_sourceWash.Layer);
                break;
        }
    }

    /// <summary>Washes what a search found in the raw half — the renderer every editor in the app paints its matches with.</summary>
    private readonly SearchHighlightRenderer _sourceWash = new();

    // ── The raw half, kept in step with the document ──────────────────────

    /// <summary>
    /// Puts what the document now says into the raw half, where it changed somewhere else — written in the rendered half, read
    /// from the file again. Only the stretch that differs is replaced, so the caret, the place and what can be undone outside it
    /// stay as they were, and a keystroke in a long document is not the whole of it written out again.
    /// </summary>
    private void TakeDocument(string markdown)
    {
        var document = SourceBox.Document;
        var text = document.Text;
        if (string.Equals(text, markdown, StringComparison.Ordinal)) return;

        var same = text.AsSpan().CommonPrefixLength(markdown);
        var room = Math.Min(text.Length, markdown.Length) - same;
        var tail = 0;
        while (tail < room && text[text.Length - 1 - tail] == markdown[markdown.Length - 1 - tail]) tail++;

        _takingDocument = true;
        try
        {
            document.Replace(same, text.Length - same - tail, markdown.Substring(same, markdown.Length - same - tail));
        }
        finally
        {
            _takingDocument = false;
        }
    }

    /// <summary>What the reader wrote in the raw half, said to the document — unless it is the raw half being given the document.</summary>
    private void TakeSourceEdit()
    {
        if (_takingDocument) return;

        ViewModel.Markdown = SourceBox.Text;
    }

    /// <summary>Whether the raw half is being given what the document says, so its own report of that change is not taken for writing.</summary>
    private bool _takingDocument;

    /// <summary>
    /// Puts the raw half's caret on a match, picked out, and scrolls it into view. The keyboard stays where the reader left it:
    /// the wash shows every match whether or not the half has it. Queued at Loaded priority because a change of view lays the
    /// half out in the same beat, and it has no layout to scroll until that lands.
    /// </summary>
    private void SelectInSource(int offset, int length)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var max = SourceBox.Document.TextLength;
            if (offset < 0 || offset > max) return;

            SourceBox.Select(offset, Math.Min(length, max - offset));

            var at = SourceBox.Document.GetLocation(offset);
            SourceBox.ScrollTo(at.Line, at.Column);
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

        _ledBySource = fromSource;

        var at = fromSource ? SourceTop() : Editor.ShownFrom;
        if (at >= 0) WhilePairing(() => PutBeside(at, fromSource));
    }

    /// <summary>Puts the half that did not move beside <paramref name="at"/>, where the one that did now stands.</summary>
    private void PutBeside(int at, bool fromSource)
    {
        var block = Editor.BlockAt(at);
        var mine = fromSource ? SourceShowsWhole(block) : Editor.ShowsWhole(block);
        var theirs = fromSource ? Editor.ShowsWhole(block) : SourceShowsWhole(block);

        // Lines carry across while both halves show the block as lines. Where one of them draws it far bigger
        // than the other writes it — a diagram a page and a half tall from ten lines of source — no place
        // inside it answers to a place inside the other, and only its edges mean anything to both.
        if (!mine && theirs)
        {
            // What the reader wants beside a drawing they are partway down is the whole of the short source it
            // was made from, however far into it they have read.
            at = block.Start;
        }
        else if (mine && !theirs && at > block.Start)
        {
            // And the other way about: past the first line of that source, the drawing it makes is behind the
            // reader, so what both halves can still mean is the block after it.
            if (Editor.BlockAfter(at) is { Length: > 0 } next) at = next.Start;
        }

        if (fromSource) Editor.ShowFrom(at);
        else            ShowSourceFrom(at);
    }

    /// <summary>
    /// Moves a half while holding the halves apart, so the report its move makes is not read as the reader moving it.
    /// </summary>
    private void WhilePairing(Action move)
    {
        _pairing = true;
        try
        {
            move();
        }
        finally
        {
            // The half just moved says so on the layout pass that move causes, and that report is this view's own
            // doing rather than the reader's. Input priority is below the pass, so it has been and gone by here.
            Dispatcher.BeginInvoke(() => _pairing = false, System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    /// <summary>
    /// Where the reader is, as a place in the source: the top of whichever surface is showing — in the split, the half they
    /// moved last — or -1 where it has no layout to be anywhere in. Read before a change of view lays the surfaces out again.
    /// </summary>
    private int ReaderAt() => _laidOut switch
    {
        MarkdownViewMode.Source => SourceTop(),
        MarkdownViewMode.Split when _ledBySource => SourceTop(),
        _ => Editor.ShownFrom,
    };

    /// <summary>
    /// Puts the surfaces a change of view has just shown at <paramref name="at"/>, where the reader was before it — a surface
    /// that has been out of sight, or laid again at another width, is otherwise wherever it was left, which is usually the top.
    /// In the split the half the reader came from is put there and the other beside it, as the split always holds them.
    /// </summary>
    /// <param name="fromSource">Whether the reader came from the raw box rather than the rendered surface.</param>
    private void ArriveAt(int at, bool fromSource)
    {
        if (at < 0) return;

        WhilePairing(() =>
        {
            switch (ViewModel.ViewMode)
            {
                case MarkdownViewMode.Source:
                    ShowSourceFrom(at);
                    break;

                case MarkdownViewMode.Rendered:
                    Editor.ShowFrom(at);
                    break;

                default:
                    _ledBySource = fromSource;
                    if (fromSource) ShowSourceFrom(at);
                    else            Editor.ShowFrom(at);

                    PutBeside(at, fromSource);
                    break;
            }
        });
    }

    /// <summary>Whether a half is being put where the other one stands, so its own report is not read as the reader moving.</summary>
    private bool _pairing;

    /// <summary>Whether the half of the split the reader moved last is the raw box.</summary>
    private bool _ledBySource;

    /// <summary>
    /// The character the raw half's first whole row starts at, or -1 before it has a row to be on. The row the top edge cuts
    /// through is not it: a drag of the scrollbar comes to rest anywhere, so half the row above is usually still showing, and
    /// answering with it has the other half display the whole of what this one has all but scrolled past. A line wrapped onto
    /// several rows is read row by row, so a long paragraph scrolled half past is answered from where its showing rows start.
    /// </summary>
    private int SourceTop()
    {
        var view = SourceBox.TextArea.TextView;
        view.EnsureVisualLines();

        var top = view.ScrollOffset.Y;
        if (view.GetVisualLineFromVisualTop(top) is not { } line) return -1;

        foreach (var row in line.TextLines)
            if (line.GetTextLineVisualYPosition(row, VisualYPosition.LineTop) >= top - Hair)
                return line.FirstDocumentLine.Offset + line.GetRelativeOffset(line.GetTextLineVisualStartColumn(row));

        return line.LastDocumentLine.NextLine?.Offset ?? line.FirstDocumentLine.Offset;
    }

    /// <summary>Closer than this and two places on the page are the one place.</summary>
    private const double Hair = 0.5;

    /// <summary>Puts the row the character at <paramref name="offset"/> is on at the top of the raw half.</summary>
    private void ShowSourceFrom(int offset)
    {
        if (offset < 0 || offset > SourceBox.Document.TextLength) return;

        var at = SourceTopOf(offset);
        if (!double.IsNaN(at)) SourceBox.ScrollToVerticalOffset(at);
    }

    /// <summary>How far down the raw half's text the row holding <paramref name="offset"/> starts.</summary>
    private double SourceTopOf(int offset) =>
        SourceBox.TextArea.TextView.GetVisualPosition(new TextViewPosition(SourceBox.Document.GetLocation(offset)), VisualYPosition.LineTop).Y;

    /// <summary>Whether the raw half can show the whole of a stretch of source at once.</summary>
    private bool SourceShowsWhole((int Start, int Length) block)
    {
        if (block.Length <= 0 || SourceBox.ViewportHeight <= 0) return false;

        var view = SourceBox.TextArea.TextView;
        var closes = Math.Min(block.Start + block.Length, SourceBox.Document.TextLength);
        var bottom = view.GetVisualPosition(new TextViewPosition(SourceBox.Document.GetLocation(closes)), VisualYPosition.LineBottom).Y;

        return bottom - SourceTopOf(block.Start) <= SourceBox.ViewportHeight;
    }

    // ── IPageView ─────────────────────────────────────────────────────────

    IPageViewModel? IPageView.ViewModel => ViewModel;
}
