using Nexaflow.Features.Common;
using Nexaflow.Features.Pdf.Models;
using Nexaflow.Features.Pdf.Rendering;
using Nexaflow.Features.Pdf.ViewModels;
using Nexaflow.Visuals.Common.Behaviors;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Text.Editing.Paged;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Nexaflow.Features.Pdf.Views;

/// <summary>
/// The PDF reader tab: the document's pages drawn natively by the paged host, with the reader's toolbar over them and
/// Nexaflow's document panel beside them.
/// </summary>
public partial class PdfView : UserControl, IPageView, IDisposable
{
    private readonly IShellServices _shell;

    /// <summary>How much one press of a zoom button zooms by.</summary>
    private const double ZoomStep = 1.25;

    public PdfViewModel ViewModel { get; }

    public PdfView(PdfViewModel viewModel, IShellServices shell)
    {
        InitializeComponent();
        ViewModel   = viewModel;
        _shell      = shell;
        DataContext = viewModel;

        ViewModel.NavigateToPage = (page, offsetFromTop) =>
            Pages.GoToPage(page - 1, (offsetFromTop ?? 0) * PdfPageLayout.DipsPerPoint);
        ViewModel.Host           = this;   // what opens beside the document — its scans' Markdown — opens in its window
        Pages.LinkActivated += (_, link) => FollowLink(link);
        Pages.CopyRequested += (_, text) => ViewModel.CopyTextCommand.Execute(text);
        Pages.PageLaid      += (_, _) => ShowFindHits(reveal: false);
        DependencyPropertyDescriptor.FromProperty(PagedLayoutElement.CurrentPageProperty, typeof(PagedLayoutElement))
            .AddValueChanged(Pages, (_, _) => ViewModel.CurrentPage = Pages.CurrentPage + 1);

        // Collapsing the panel Border alone would leave its 300px column behind as a blank gutter, so the
        // grid columns have to collapse with it.
        ViewModel.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(PdfViewModel.IsPanelOpen):
                    if (!_makingRoom) _closedForRoom = false;   // the reader opened or closed it: theirs from now on
                    ApplyPanelWidth();
                    break;
                case nameof(PdfViewModel.FoundRanges): ShowFindHits(reveal: false); break;
                case nameof(PdfViewModel.FindIndex):   ShowFindHits(reveal: true); break;
                case nameof(PdfViewModel.Pages):       OpenPending(); break;
                case nameof(PdfViewModel.CurrentPage): RevealCurrentThumbnail(); break;
            }
        };

        // A view too narrow for the panel and the reader — a pane of a split window — gives the reader the room.
        SizeChanged += (_, _) => MakeRoom();

        Loaded += OnLoaded;
    }

    private bool _loaded;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Loaded fires again on a re-parent (tab torn off to another window); the document is already read, and the
        // reader's place survives the move.
        if (_loaded) return;
        _loaded = true;

        var paper = TryFindResource("Pdf.Paper") as Brush ?? Brushes.White;
        await ViewModel.LoadAsync(paper.IsFrozen ? paper : (Brush)paper.GetCurrentValueAsFrozen());
        Pages.Focus();
    }

    /// <summary>A page asked for before the pages were ready (an initial "page" param), applied once they are.</summary>
    private int? _pendingPage;

    private void OpenPending()
    {
        if (_pendingPage is int page && ViewModel.GoToPage(page)) _pendingPage = null;
    }

    /// <summary>The panel width to restore: whatever the user last dragged the splitter to.</summary>
    private GridLength _panelWidth = new(300);

    /// <summary>
    /// Gives the panel its column back, or takes the column away entirely.
    /// <para>
    /// Hiding the Border is not enough on its own: a collapsed child does not release its grid column, so the
    /// reader would keep a 300px blank gutter where the panel used to be. MinWidth has to go too, or the
    /// column floors at 220 however narrow the width says it is.
    /// </para>
    /// </summary>
    private void ApplyPanelWidth()
    {
        if (ViewModel.IsPanelOpen)
        {
            PanelColumn.MinWidth   = PanelMinWidth;
            PanelColumn.Width      = _panelWidth;
            SplitterColumn.Width   = new GridLength(SplitterWidth);
        }
        else
        {
            _panelWidth            = PanelColumn.Width;   // remember where they had dragged it to
            PanelColumn.MinWidth   = 0;
            PanelColumn.Width      = new GridLength(0);
            SplitterColumn.Width   = new GridLength(0);
        }
    }

    /// <summary>The panel was closed to give the reader room, not by the reader: it opens again once there is room.</summary>
    private bool _closedForRoom;

    private bool _makingRoom;

    /// <summary>
    /// Closes the panel where the view cannot hold it beside the reader at its least, and opens it again once it can: in a
    /// pane of a split window the pages need the room more than the panel does.
    /// </summary>
    private void MakeRoom()
    {
        bool open = ViewModel.IsPanelOpen;
        if (open && ActualWidth < PanelMinWidth + SplitterWidth + ReaderColumn.MinWidth)
        {
            _closedForRoom = true;
            Set(false);
        }
        else if (!open && _closedForRoom && ActualWidth >= _panelWidth.Value + SplitterWidth + ReaderColumn.MinWidth)
        {
            _closedForRoom = false;
            Set(true);
        }

        void Set(bool value)
        {
            _makingRoom = true;
            try { ViewModel.IsPanelOpen = value; }
            finally { _makingRoom = false; }
        }
    }

    private const double PanelMinWidth = 220;
    private const double SplitterWidth = 4;

    /// <summary>Sets the page the document should open at, once its pages are read.</summary>
    public void OpenAtPage(int page)
    {
        _pendingPage = page > 0 ? page : null;
        OpenPending();
    }

    /// <summary>
    /// Jumps to the clicked row's destination.
    /// <para>
    /// Wired to the ListBoxItem's button-DOWN via an EventSetter, and deliberately left unhandled: a
    /// ListBoxItem captures the mouse on button-down, so a handler on a child never sees the matching
    /// button-up, and marking down handled would stop the list selecting the row at all.
    /// </para>
    /// <para>
    /// Not driven off selection changing: the reader may scroll away and want to click the same row again to come
    /// back, which a selection-driven jump would ignore.
    /// </para>
    /// </summary>
    private void OutlineRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PdfOutlineItem item }) return;

        // A row with no destination is a grouping heading. It still selects and copies; it just has nowhere
        // to go.
        if (item.PageNumber is not int page || !item.CanJump) return;

        ViewModel.GoToPage(page, item.OffsetFromTop);
    }

    // ── Toolbar ───────────────────────────────────────────────────────────

    private void PreviousPage_Click(object sender, RoutedEventArgs e) => ViewModel.GoToPage(ViewModel.CurrentPage - 1);

    private void NextPage_Click(object sender, RoutedEventArgs e) => ViewModel.GoToPage(ViewModel.CurrentPage + 1);

    /// <summary>Enter goes to the page typed; anything not a page of the document puts the box back as it was.</summary>
    private void PageBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;

        if (!int.TryParse(PageBox.Text.Trim(), out var page) || !ViewModel.GoToPage(page))
            PageBox.Text = ViewModel.CurrentPage.ToString(System.Globalization.CultureInfo.CurrentCulture);

        Pages.Focus();
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => Pages.Zoom /= ZoomStep;

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => Pages.Zoom *= ZoomStep;

    private void FitWidth_Click(object sender, RoutedEventArgs e) => Pages.FitWidth();

    private void FitPage_Click(object sender, RoutedEventArgs e) => Pages.FitPage();

    /// <summary>Enter finds the next hit, Shift+Enter the previous; Escape clears the search and returns to the pages.</summary>
    private void FindBox_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
                ViewModel.FindPreviousCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Enter:
                ViewModel.FindNextCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                ViewModel.ClearFindCommand.Execute(null);
                Pages.Focus();
                e.Handled = true;
                break;
        }
    }

    /// <summary>The search's hits washed over the pages; the one being looked at brought into view when it moved.</summary>
    private void ShowFindHits(bool reveal)
    {
        var hits = ViewModel.FoundRanges;
        var index = ViewModel.FindIndex;
        Pages.ShowHighlights(hits, index);
        if (reveal && index >= 0 && index < hits.Count) _ = Pages.RevealAsync(hits[index]);
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PrintDialog { UserPageRangeEnabled = false };
        if (dialog.ShowDialog() != true) return;

        var sheet = new Size(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);
        if (ViewModel.Paginator(sheet) is not { } paginator) return;

        try
        {
            dialog.PrintDocument(paginator, ViewModel.FileName);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _shell.ShowError(Str.Format("Pdf.Error.PrintFormat", ex.Message));
        }
    }

    /// <summary>A link on a page: a place in this document is scrolled to, anything else opened as an address.</summary>
    private void FollowLink(PageLink link)
    {
        if (PdfLinks.TryRead(link.Href, out var target))
        {
            ViewModel.GoToTarget(link.Page, target);
            return;
        }

        if (!Uri.TryCreate(link.Href, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "mailto"))
            return;

        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { _shell.ShowError(Str.Format("Pdf.Error.OpenLinkFormat", ex.Message)); }
    }

    /// <summary>
    /// Opens the file in the default PDF application — the file as saved, so with unsaved page edits the reader is asked
    /// first.
    /// </summary>
    private async void OpenExternal_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsDirty
            && !await _shell.ConfirmAsync(Str.Get("Pdf.OpenExternal.UnsavedTitle"), Str.Get("Pdf.OpenExternal.UnsavedMessage")))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(ViewModel.FileUri.LocalPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _shell.ShowError(Str.Format("Pdf.Error.OpenExternalFormat", ex.Message));
        }
    }

    // ── IPageView ─────────────────────────────────────────────────────────

    IPageViewModel? IPageView.ViewModel => ViewModel;

    /// <summary>Re-activating the tab with a "page" param jumps to it rather than opening a second tab.</summary>
    public void Reinitialize(Dictionary<string, string> pageParams)
    {
        if (!pageParams.TryGetValue("page", out var raw) || !int.TryParse(raw, out var page) || page < 1)
            return;

        OpenAtPage(page);
    }

    public void Dispose() => ViewModel.Dispose();

    // ── Pages tab ─────────────────────────────────────────────────────────

    /// <summary>The line drawn in the gap a right-click between two pages names, while its menu is open.</summary>
    private readonly InsertionMarker _gapMarker = new();

    /// <summary>How wide, in pixels, a page's thumbnail is drawn: its sheet at this screen's scale.</summary>
    private int ThumbnailPixels() =>
        (int)Math.Ceiling(PdfPageThumbnail.SheetBox * VisualTreeHelper.GetDpi(this).DpiScaleX);

    /// <summary>
    /// A row recycled for another page while in view: the page it showed is no longer wanted, the one it shows now is. A
    /// row coming into view for the first time has no change of data context to report — WPF gives a template's root its
    /// item before the root is attached — so that is <see cref="Thumbnail_Loaded"/>'s.
    /// </summary>
    private void Thumbnail_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is PdfPageThumbnail old) ViewModel.PagesPanel.Unrealized(old);
        if (e.NewValue is PdfPageThumbnail page && sender is FrameworkElement { IsLoaded: true })
            ViewModel.PagesPanel.Realized(page, ThumbnailPixels());
    }

    /// <summary>A row came into view — first shown, or shown again with its tab — and its page's picture is wanted.</summary>
    private void Thumbnail_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PdfPageThumbnail page }) ViewModel.PagesPanel.Realized(page, ThumbnailPixels());
    }

    private void Thumbnail_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PdfPageThumbnail page }) ViewModel.PagesPanel.Unrealized(page);
    }

    /// <summary>A click on a page with no keys held goes to it; a click with Ctrl or Shift only selects.</summary>
    private void ThumbnailRow_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.None && sender is ListBoxItem { DataContext: PdfPageThumbnail page })
            ViewModel.PagesPanel.GoToCommand.Execute(page);
    }

    /// <summary>
    /// Fills the menu for where it opened: on a page, deleting the pages selected — the page itself, unless it is one of
    /// them — and inserting before or after it; between two pages, inserting there, the gap marked while the menu is open.
    /// </summary>
    private void Thumbnails_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var panel = ViewModel.PagesPanel;
        var menu = Thumbnails.ContextMenu;
        menu.Items.Clear();
        if (!panel.CanEdit || panel.Pages.Count == 0)
        {
            e.Handled = true;
            return;
        }

        if (RowUnder(e.OriginalSource) is { DataContext: PdfPageThumbnail page } row)
        {
            if (!page.IsSelected)
            {
                Thumbnails.SelectedItems.Clear();
                row.IsSelected = true;
            }

            var index = panel.Pages.IndexOf(page);
            var selected = panel.SelectedCount;
            menu.Items.Add(MenuItemFor(
                selected == 1 ? Str.Get("Pdf.Menu.DeletePage") : Str.Format("Pdf.Menu.DeletePagesFormat", selected),
            "Pdf_Thumb_Delete", panel.DeleteSelectedCommand, null));
        menu.Items.Add(MenuItemFor(Str.Get("Pdf.Menu.SplitPages"), "Pdf_Thumb_Split", panel.BeginSplitCommand, null));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItemFor(Str.Get("Pdf.Menu.InsertImageBefore"), "Pdf_Thumb_InsertImageBefore", panel.InsertImagesAtCommand, index));
            menu.Items.Add(MenuItemFor(Str.Get("Pdf.Menu.InsertImageAfter"), "Pdf_Thumb_InsertImageAfter", panel.InsertImagesAtCommand, index + 1));
            menu.Items.Add(MenuItemFor(Str.Get("Pdf.Menu.InsertPdfBefore"), "Pdf_Thumb_InsertPdfBefore", panel.InsertPdfsAtCommand, index));
            menu.Items.Add(MenuItemFor(Str.Get("Pdf.Menu.InsertPdfAfter"), "Pdf_Thumb_InsertPdfAfter", panel.InsertPdfsAtCommand, index + 1));
            return;
        }

        var gap = ItemGaps.GapAt(Thumbnails, Mouse.GetPosition(Thumbnails));
        _gapMarker.Show(Thumbnails, gap);
        menu.Items.Add(MenuItemFor(Str.Get("Pdf.Menu.InsertImageHere"), "Pdf_Gap_InsertImage", panel.InsertImagesAtCommand, gap));
        menu.Items.Add(MenuItemFor(Str.Get("Pdf.Menu.InsertPdfHere"), "Pdf_Gap_InsertPdf", panel.InsertPdfsAtCommand, gap));
    }

    private void ThumbnailsMenu_Closed(object sender, RoutedEventArgs e) => _gapMarker.Clear();

    /// <summary>Delete deletes the pages selected; Alt+Up and Alt+Down move them a place, together.</summary>
    private void Thumbnails_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var panel = ViewModel.PagesPanel;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var alt = Keyboard.Modifiers == ModifierKeys.Alt;

        if (key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (panel.DeleteSelectedCommand.CanExecute(null)) panel.DeleteSelectedCommand.Execute(null);
            e.Handled = true;
        }
        else if (alt && key is Key.Up or Key.Down)
        {
            var step = key == Key.Up ? -1 : 1;
            if (panel.MoveSelectionCommand.CanExecute(step)) panel.MoveSelectionCommand.Execute(step);
            if (panel.Pages.FirstOrDefault(page => page.IsSelected) is { } moved) Thumbnails.ScrollIntoView(moved);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Ctrl+Z and Ctrl+Y (or Ctrl+Shift+Z) undo and redo page edits, Ctrl+S saves and Ctrl+Shift+S saves as — except in a
    /// text box, whose own undo and keys come first.
    /// </summary>
    private void View_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase) return;
        var modifiers = Keyboard.Modifiers;
        if (!modifiers.HasFlag(ModifierKeys.Control)) return;
        var shift = modifiers.HasFlag(ModifierKeys.Shift);

        System.Windows.Input.ICommand? command = e.Key switch
        {
            Key.Z when !shift => ViewModel.UndoCommand,
            Key.Z             => ViewModel.RedoCommand,
            Key.Y             => ViewModel.RedoCommand,
            Key.S when !shift => ViewModel.SaveCommand,
            Key.S             => ViewModel.SaveAsCommand,
            _                 => null,
        };

        if (command is null) return;
        if (command.CanExecute(null)) command.Execute(null);
        e.Handled = true;
    }

    /// <summary>The page being read, shown in the Pages tab while it is open.</summary>
    private void RevealCurrentThumbnail()
    {
        if (!PagesTab.IsSelected || ViewModel.CurrentPage < 1 || ViewModel.CurrentPage > ViewModel.PagesPanel.Pages.Count) return;
        Thumbnails.ScrollIntoView(ViewModel.PagesPanel.Pages[ViewModel.CurrentPage - 1]);
    }

    private static ListBoxItem? RowUnder(object source)
    {
        for (var at = source as DependencyObject; at is not null; at = VisualTreeHelper.GetParent(at) ?? LogicalTreeHelper.GetParent(at))
        {
            if (at is ListBoxItem row) return row;
            if (at is ListBox) return null;
        }

        return null;
    }

    private static MenuItem MenuItemFor(string header, string automationId, System.Windows.Input.ICommand command, object? parameter)
    {
        var item = new MenuItem { Header = header, Command = command, CommandParameter = parameter };
        AutomationProperties.SetAutomationId(item, automationId);
        return item;
    }
}
