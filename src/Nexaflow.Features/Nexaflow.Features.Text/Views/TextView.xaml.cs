using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using Nexaflow.Features.Common;
using Nexaflow.Features.Text.Rendering;
using Nexaflow.Visuals.Text.Editor;
using Nexaflow.Features.Text.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Nexaflow.Visuals.Common.Theming;

namespace Nexaflow.Features.Text.Views;

public partial class TextView : UserControl, IPageView
{
    private readonly TextViewModel            _vm;
    private readonly SearchHighlightRenderer  _renderer = new();
    private readonly IReadOnlySectionProvider _windowReadOnly;
    private ScrollViewer? _sv;

    IPageViewModel? IPageView.ViewModel => _vm;

    public TextView(TextViewModel vm)
    {
        InitializeComponent();

        _vm        = vm;
        DataContext = vm;
        // Editable only while editing (and not mid-save); otherwise nothing is editable. AvalonEdit gates
        // edits on this provider, so this — not IsReadOnly — is what makes the viewer read-only by default.
        _windowReadOnly = new WindowReadOnlyProvider(EditableStart, EditableEnd);

        // Wire AvalonEdit after init — document is owned by the ViewModel
        Editor.Document = vm.Document;
        // Edits are confined to the resident window (a small file's whole document is the window).
        Editor.TextArea.ReadOnlySectionProvider = _windowReadOnly;
        // Give the ViewModel access to the visible text region + first visible line (rendering-layer API)
        vm.GetVisibleText      = GetVisibleText;
        vm.GetFirstVisibleLine = GetFirstVisibleLine;
        // Find bar: seed from the editor selection, and focus the box when it opens.
        vm.GetEditorSelection    = () => Editor.SelectedText;
        vm.FindBarFocusRequested += FocusFindBox;
        Editor.TextArea.TextView.BackgroundRenderers.Add(_renderer);
        Editor.ShowLineNumbers = vm.ShowLineNumbers;
        Editor.WordWrap        = vm.WordWrap;
        Editor.PreviewMouseWheel += OnEditorPreviewMouseWheel; // Ctrl+wheel zoom
        // The zoom's own notifications rather than the VM's: TextZoom.FontSize also moves when the shell's
        // text size changes, which the VM never hears about.
        vm.Zoom.PropertyChanged += OnZoomChanged;
        Unloaded += (_, _) => vm.Zoom.PropertyChanged -= OnZoomChanged;
        ApplyZoom();

        // Clipboard buttons bind to AvalonEdit's editing commands (targeting the TextArea, where the
        // handlers live) so WPF drives their enabled state: Cut/Copy need a selection, Paste needs an
        // editable target + clipboard content. Disabling whole-line cut/copy makes an empty selection
        // disable Cut/Copy rather than acting on the current line.
        Editor.Options.CutCopyWholeLine = false;
        CutButton.Command   = ApplicationCommands.Cut;   CutButton.CommandTarget   = Editor.TextArea;
        CopyButton.Command  = ApplicationCommands.Copy;  CopyButton.CommandTarget  = Editor.TextArea;
        PasteButton.Command = ApplicationCommands.Paste; PasteButton.CommandTarget = Editor.TextArea;
        // Undo/Redo drive AvalonEdit's editing commands, so WPF enables them from the document's undo stack.
        UndoButton.Command  = ApplicationCommands.Undo;  UndoButton.CommandTarget  = Editor.TextArea;
        RedoButton.Command  = ApplicationCommands.Redo;  RedoButton.CommandTarget  = Editor.TextArea;

        // User edits flow to the ViewModel, which maps them to the overlay engine.
        Editor.Document.Changed += (_, e) => _vm.OnUserEdit(e.Offset, e.RemovalLength, e.InsertedText.Text);
        Editor.PreviewKeyDown   += OnEditorKeyDown;

        vm.PropertyChanged += OnVmPropertyChanged;
        Editor.TextArea.Caret.PositionChanged += (_, _) => vm.CurrentCaretOffset = Editor.CaretOffset;
        // A window slide rebuilds the document and resets the editor caret; the view-model has kept the
        // logical position, so put the caret back. Caret only — deliberately no scroll.
        vm.CaretRestoreRequested += RestoreCaret;
        Unloaded += OnUnloaded;

        Loaded += async (_, _) =>
        {
            await vm.LoadAsync(CancellationToken.None);

            _sv = FindDescendant<ScrollViewer>(Editor);
            if (_sv is not null)
            {
                SyncMinimapBottomPadding(_sv);
                _sv.ScrollChanged += (_, _) => SyncMinimapBottomPadding(_sv);
            }
        };
    }

    private void OnEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (_vm.SaveCommand.CanExecute(null)) _vm.SaveCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void SyncMinimapBottomPadding(ScrollViewer sv)
    {
        var hBarHeight = sv.ComputedHorizontalScrollBarVisibility == Visibility.Visible
            ? FindDescendant<ScrollBar>(Editor, sb => sb.Orientation == Orientation.Horizontal)?.ActualHeight ?? 6.0
            : 0.0;
        MiniMapCanvas.BottomPadding = 8.0 + hBarHeight;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TextViewModel.SearchHighlights):
                _renderer.Highlights = _vm.SearchHighlights;
                Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
                break;

            case nameof(TextViewModel.ScrollToOffset):
                if (_vm.ScrollToOffset >= 0)
                    ScrollToOffset(_vm.ScrollToOffset);
                break;

            case nameof(TextViewModel.MiniMapMarks):
                MiniMapCanvas.Marks = _vm.MiniMapMarks;
                break;

            case nameof(TextViewModel.ShowLineNumbers):
                Editor.ShowLineNumbers = _vm.ShowLineNumbers;
                break;

            case nameof(TextViewModel.WordWrap):
                Editor.WordWrap = _vm.WordWrap;
                break;
        }
    }

    // ── Find bar + zoom ─────────────────────────────────────────────────────────

    /// <summary>Pushes the zoom's effective point size onto the editor. The size already folds in the
    /// shell's text setting, so this is also what a live Options change lands through.</summary>
    private void ApplyZoom() => Editor.FontSize = _vm.Zoom.FontSize;

    private void FocusFindBox() =>
        Dispatcher.InvokeAsync(() => { FindBox.Focus(); FindBox.SelectAll(); },
                               System.Windows.Threading.DispatcherPriority.Input);

    private void FindBox_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                    _vm.FindPreviousCommand.Execute(null);
                else
                    _vm.FindNextCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                _vm.CloseFindBarCommand.Execute(null);
                Editor.Focus();
                e.Handled = true;
                break;
        }
    }

    private void OnZoomChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TextZoom.FontSize) or null) ApplyZoom();
    }

    private void OnEditorPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        => e.Handled = _vm.Zoom.TryWheel(e);

    private void FindMenu_Click(object sender, RoutedEventArgs e) => FindMenuPopup.IsOpen = true;

    private void FindMenuFind_Click(object sender, RoutedEventArgs e)
    {
        FindMenuPopup.IsOpen = false;
        _vm.OpenFindCommand.Execute(null);
    }

    private void FindMenuReplace_Click(object sender, RoutedEventArgs e)
    {
        FindMenuPopup.IsOpen = false;
        _vm.OpenReplaceCommand.Execute(null);
    }

    // The editable span the read-only-section provider allows: the resident window while editing (and not
    // mid-save), otherwise an empty span so the whole document is read-only.
    private bool Editable => _vm.IsEditing && !_vm.IsBusy;
    private int EditableStart() => Editable ? _vm.LoadedRealStart : 0;
    private int EditableEnd()   => Editable ? _vm.LoadedRealEnd   : -1;

    // Moves only the caret (no scroll) — used to keep the cursor put across a window recomposition.
    private void RestoreCaret(int offset)
    {
        if (Editor.Document is null) return;
        if (offset < 0 || offset > Editor.Document.TextLength) return;
        if (Editor.CaretOffset != offset) Editor.CaretOffset = offset;
    }

    private void ScrollToOffset(int offset)
    {
        if (Editor.Document is null) return;
        if (offset < 0 || offset >= Editor.Document.TextLength) return;

        var line = Editor.Document.GetLineByOffset(offset);
        Editor.CaretOffset = offset;

        Dispatcher.InvokeAsync(() =>
        {
            Editor.ScrollToLine(line.LineNumber);

            var sv = FindDescendant<ScrollViewer>(Editor);
            if (sv is null) return;

            // After ScrollToLine the target line is at or near top; pull it to centre.
            var lineHeight  = Editor.TextArea.TextView.DefaultLineHeight;
            var targetY     = (line.LineNumber - 1) * lineHeight;
            var centred     = Math.Max(0, targetY - sv.ViewportHeight / 2.0);
            sv.ScrollToVerticalOffset(centred);
        }, System.Windows.Threading.DispatcherPriority.Render);
    }

    private static T? FindDescendant<T>(DependencyObject parent, Func<T, bool>? predicate = null) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match && (predicate is null || predicate(match))) return match;
            var found = FindDescendant<T>(child, predicate);
            if (found is not null) return found;
        }
        return null;
    }

    private async void Editor_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!_vm.IsLargeFile) return;

        var sv = _sv ??= FindDescendant<ScrollViewer>(Editor);
        if (sv is null) return;

        var lineHeight = Editor.TextArea.TextView.DefaultLineHeight;
        if (lineHeight <= 0) return;

        // 0-based file line numbers at the top and bottom of the current viewport (Document line == file line).
        var topLine    = (int)(sv.VerticalOffset / lineHeight);
        var bottomLine = (int)Math.Ceiling((sv.VerticalOffset + sv.ViewportHeight) / lineHeight);

        await _vm.EnsureWindowAsync(topLine, bottomLine);
    }

    private int GetFirstVisibleLine()
    {
        var sv = _sv ??= FindDescendant<ScrollViewer>(Editor);
        var lineHeight = Editor.TextArea.TextView.DefaultLineHeight;
        if (sv is null || lineHeight <= 0) return 1;
        return (int)(sv.VerticalOffset / lineHeight) + 1; // 1-based
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _vm.PropertyChanged      -= OnVmPropertyChanged;
        _vm.FindBarFocusRequested -= FocusFindBox;
        _vm.CaretRestoreRequested -= RestoreCaret;
        Editor.PreviewMouseWheel  -= OnEditorPreviewMouseWheel;
        _vm.Dispose();
    }

    private string GetVisibleText()
    {
        var tv    = Editor.TextArea.TextView;
        var start = tv.GetPosition(new Point(0, 0) + tv.ScrollOffset);
        var end   = tv.GetPosition(new Point(tv.ActualWidth, tv.ActualHeight) + tv.ScrollOffset);

        if (start is null || end is null) return Editor.Text;

        var startOffset = Editor.Document.GetOffset(start.Value.Location);
        var endOffset   = Editor.Document.GetOffset(end.Value.Location);

        if (startOffset >= endOffset) return string.Empty;
        return Editor.Document.GetText(startOffset, endOffset - startOffset);
    }
}
