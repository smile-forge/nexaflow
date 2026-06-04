using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Inline ("as you type") markdown editor built on a single <see cref="RichTextBox"/>.
/// The note is modelled as a list of block <em>contents</em> (separated by blank lines
/// in the markdown); the document renders every block via
/// <see cref="MarkdownFlowDocument"/> except the block the caret is in, which is shown
/// as its raw markdown source. Because it is one RichTextBox, the caret, selection and
/// copy are native and span the whole note — rendered blocks, tables and the source
/// block alike.
///
/// All text-modifying input is intercepted and applied to the block model; the document
/// is then rebuilt. WPF never edits the document itself (which keeps the model
/// authoritative and avoids the re-entrancy crash from mutating a RichTextBox inside its
/// own change block). Caret/selection navigation stays native.
///
/// Editing semantics: Enter = new block (the block you leave renders); Ctrl+Enter = a
/// markdown hard break inside the current block; Tab = a tab. Block separators are NOT
/// part of a block's content, so editing a block never shows or doubles the blank-line
/// separator.
///
/// Offsets within the active block are measured at the run level
/// (<see cref="TextPointer.GetTextRunLength"/>), NOT via <c>TextRange.Text</c>, because
/// the latter trims trailing whitespace and would drop spaces (e.g. "## " → "##").
/// </summary>
public class InlineMarkdownEditor : UserControl
{
    private readonly RichTextBox _rtb;
    private readonly TextBlock   _placeholder;

    private List<string> _blocks = [""];
    private int        _active = -1;          // block shown as source, or -1 when fully rendered
    private Paragraph? _activePara;
    private bool       _suppress;             // guard around programmatic document/caret changes
    private bool       _navQueued;
    private bool       _menuOpen;             // a context menu is open → don't treat the focus loss as leaving edit mode
    private Point?     _pendingClickPoint;    // where the last left-click landed, consumed by the deferred activation

    // Block-model undo: a snapshot of (blocks, active block, caret-in-block) taken at the start of each
    // editing session. Edits within one block coalesce into a single undo step (block-level, not per key).
    private readonly List<(List<string> Blocks, int Active, int Caret)> _undo = [];
    private int       _undoGroupBlock = -2;   // block the current coalesced undo group covers (-2 = none)
    private const int UndoLimit = 200;

    private MenuItem _cutItem   = null!;
    private MenuItem _copyItem  = null!;
    private MenuItem _pasteItem = null!;

    public InlineMarkdownEditor()
    {
        _rtb = new RichTextBox
        {
            AcceptsTab                    = false,   // Tab is intercepted to insert a tab
            AllowDrop                     = true,    // a drop target, but its native text-drop is overridden below
            IsUndoEnabled                 = false,   // we rebuild the document; WPF undo would fight it
            IsInactiveSelectionHighlightEnabled = true,  // keep the selection visible while the context menu has focus
            BorderThickness               = new Thickness(0),
            Background                    = Brushes.Transparent,
            Padding                       = new Thickness(0),
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Document                      = new FlowDocument(),
        };
        SpellCheck.SetIsEnabled(_rtb, true);

        _rtb.PreviewTextInput  += OnPreviewTextInput;
        _rtb.PreviewKeyDown    += OnPreviewKeyDown;
        _rtb.PreviewMouseLeftButtonDown  += OnPreviewMouseLeftButtonDown;
        _rtb.PreviewMouseRightButtonDown += OnPreviewMouseRightButtonDown;
        _rtb.PreviewMouseRightButtonUp   += OnPreviewMouseRightButtonUp;
        _rtb.PreviewDragEnter  += OnPreviewDrag;   // override the RichTextBox's native drag-drop, which
        _rtb.PreviewDragOver   += OnPreviewDrag;   // otherwise shows "no drop" for files/images and would
        _rtb.PreviewDrop       += OnPreviewDrop;   // insert dragged text itself
        _rtb.SelectionChanged  += OnSelectionChanged;
        _rtb.GotKeyboardFocus  += (_, _) => ScheduleNavigate();
        _rtb.LostKeyboardFocus += OnLostFocus;
        _rtb.ContextMenu = BuildContextMenu();
        DataObject.AddPastingHandler(_rtb, OnPasting);
        CommandManager.AddPreviewExecutedHandler(_rtb, OnPreviewExecuted);

        _placeholder = new TextBlock
        {
            FontStyle           = FontStyles.Italic,
            IsHitTestVisible    = false,
            VerticalAlignment   = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left,
            Visibility          = Visibility.Collapsed,
        };

        Content    = new Grid { Children = { _rtb, _placeholder } };
        Background = Brushes.Transparent;
        Focusable  = false;
    }

    // ── Dependency properties ─────────────────────────────────────────────

    public static readonly DependencyProperty MarkdownProperty =
        DependencyProperty.Register(nameof(Markdown), typeof(string), typeof(InlineMarkdownEditor),
            new FrameworkPropertyMetadata(string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnMarkdownChanged));

    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public static readonly DependencyProperty PaletteProperty =
        DependencyProperty.Register(nameof(Palette), typeof(MarkdownPalette), typeof(InlineMarkdownEditor),
            new PropertyMetadata(null, (d, _) => ((InlineMarkdownEditor)d).OnPaletteChanged()));

    public MarkdownPalette? Palette
    {
        get => (MarkdownPalette?)GetValue(PaletteProperty);
        set => SetValue(PaletteProperty, value);
    }

    /// <summary>Palette in effect — an explicit <see cref="Palette"/>, else the active theme.</summary>
    private MarkdownPalette Pal => Palette ?? MarkdownPalette.FromTheme();

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(InlineMarkdownEditor),
            new PropertyMetadata(string.Empty, (d, e) => ((InlineMarkdownEditor)d).OnPlaceholderChanged((string?)e.NewValue)));

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>In-app link handler. Return true to mark the link handled (the renderer
    /// then skips opening the OS browser). When null, links open externally.</summary>
    public Func<string, bool>? LinkNavigate { get; set; }

    /// <summary>Raised when content is dropped onto the editor (the data object + the drop point in the
    /// editor's coordinates). The host turns it into markdown and calls <see cref="InsertMarkdownAt"/>.</summary>
    public event Action<IDataObject, Point>? ContentDropped;

    /// <summary>Lets the host claim a paste of rich content (image / file / URL): return true if it
    /// handled it (typically by calling <see cref="InsertMarkdownAtCaret"/>); false falls back to the
    /// editor's plain text/markdown paste.</summary>
    public Func<IDataObject, bool>? ContentPasted { get; set; }

    /// <summary>Base directory for resolving relative <c>![](file.png)</c> image paths to a local
    /// file (e.g. a post-it's attachment folder). When null, only absolute/<c>file:</c> images render.</summary>
    public static readonly DependencyProperty BaseDirectoryProperty =
        DependencyProperty.Register(nameof(BaseDirectory), typeof(string), typeof(InlineMarkdownEditor),
            new PropertyMetadata(null, (d, _) => { var e = (InlineMarkdownEditor)d; if (!e._rtb.IsKeyboardFocusWithin) e.RenderAll(); }));

    public string? BaseDirectory
    {
        get => (string?)GetValue(BaseDirectoryProperty);
        set => SetValue(BaseDirectoryProperty, value);
    }

    private MarkdownRenderContext Context => new() { Palette = Pal, OnNavigate = LinkNavigate, BaseDirectory = BaseDirectory };

    private void OnPaletteChanged()
    {
        _rtb.Foreground = Pal.Text;
        _rtb.CaretBrush = Pal.Text;
        _placeholder.Foreground = Pal.TextMuted;
        if (!_rtb.IsKeyboardFocusWithin) RenderAll();
    }

    private void OnPlaceholderChanged(string? text)
    {
        _placeholder.Text = text ?? string.Empty;
        UpdatePlaceholder();
    }

    // ── Markdown DP ↔ block model ─────────────────────────────────────────

    private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (InlineMarkdownEditor)d;
        if (self._suppress) return;                  // our own push
        if (self._rtb.IsKeyboardFocusWithin) return; // don't clobber an in-progress edit
        self._blocks = MarkdownBlocks.Split((string?)e.NewValue);
        self._active = -1;
        self._undo.Clear();                          // a new document → discard the old undo history
        self._undoGroupBlock = -2;
        self.RenderAll();
    }

    private void PushMarkdown()
    {
        _suppress = true;
        try { SetValue(MarkdownProperty, MarkdownBlocks.Join(_blocks)); }
        finally { _suppress = false; }
    }

    /// <summary>Enters edit mode at the end of the note (for freshly created notes).</summary>
    public void BeginEdit()
    {
        Activate(_blocks.Count - 1, _blocks[^1].Length);
        _rtb.Focus();
        Keyboard.Focus(_rtb);
    }

    /// <summary>
    /// Inserts <paramref name="markdown"/> as new block(s) at a drop point: after the block under
    /// <paramref name="pointInEditor"/>, or appended when dropped in the empty space below the text.
    /// Any in-progress edit is committed first. Used for drag-and-drop onto a note.
    /// </summary>
    public void InsertMarkdownAt(string markdown, Point pointInEditor)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return;

        // Resolve the insertion point against the current document, before committing.
        int after = -1;
        if (!IsBelowContent(pointInEditor)
            && _rtb.GetPositionFromPoint(pointInEditor, snapToText: true) is { } pos)
            after = PointerToModel(pos, preferEnd: false).block;

        InsertBlocksAfter(markdown, after);
    }

    /// <summary>Inserts <paramref name="markdown"/> as new block(s) after the block being edited (or the
    /// caret's block), committing any in-progress edit first. Used for pasting rich content (image /
    /// file / URL) while editing.</summary>
    public void InsertMarkdownAtCaret(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return;
        int after = _active >= 0 ? _active : BlockIndexAtPointer(_rtb.CaretPosition);
        InsertBlocksAfter(markdown, after);
    }

    private void InsertBlocksAfter(string markdown, int after)
    {
        var parts = MarkdownBlocks.Split(markdown);

        if (_active >= 0) { _blocks = MarkdownBlocks.Compact(_blocks); _active = -1; }   // commit any edit
        _undo.Clear();
        _undoGroupBlock = -2;

        int index = after < 0 ? _blocks.Count : Math.Min(after + 1, _blocks.Count);
        _blocks.InsertRange(index, parts);

        PushMarkdown();
        RenderAll();
    }

    /// <summary>Swaps the first block whose content equals <paramref name="from"/> for
    /// <paramref name="to"/> (used to replace a pasted/dropped URL with its fetched preview). No-op if
    /// the block is gone (e.g. the user deleted it).</summary>
    public void ReplaceBlock(string from, string to)
    {
        int i = _blocks.IndexOf(from);
        if (i < 0) return;
        _blocks[i] = to;
        PushMarkdown();
        if (_active < 0) RenderAll();
        else Activate(_active, CaretInActiveBlock());   // keep editing; re-render the changed block
    }

    // ── Edits (applied to the block model) ────────────────────────────────

    /// <summary>Replaces text in the active block between two in-block offsets, then rebuilds.</summary>
    private void EditActive(int from, int to, string insert)
    {
        Snapshot();
        insert = insert.Replace("\r\n", "\n").Replace("\r", "\n");
        var block = _blocks[_active];
        from = Math.Clamp(from, 0, block.Length);
        to   = Math.Clamp(to,   from, block.Length);
        _blocks[_active] = block[..from] + insert + block[to..];
        Activate(_active, from + insert.Length);
        PushMarkdown();
    }

    /// <summary>
    /// A click strictly inside a rendered link follows it. Any other click is left to WPF's native
    /// handling (focus + caret); the block under the caret is then activated by the deferred
    /// <see cref="ScheduleNavigate"/> — which runs after the mouse is released, so it never disturbs the
    /// RichTextBox's mouse capture, and it skips activation when a drag produced a selection.
    /// </summary>
    private void OnPreviewMouseLeftButtonDown(object? sender, MouseButtonEventArgs e)
    {
        _pendingClickPoint = null;
        if (e.ClickCount != 1) return;   // let double-click select a word for editing

        var glyph = _rtb.GetPositionFromPoint(e.GetPosition(_rtb), snapToText: false);
        if (glyph is not null && FindHyperlink(glyph) is { NavigateUri: { } uri } link
            && glyph.CompareTo(link.ContentStart) > 0 && glyph.CompareTo(link.ContentEnd) < 0)
        {
            e.Handled = true;
            var url = uri.ToString();
            if (LinkNavigate?.Invoke(url) == true) return;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { }
            return;
        }

        // Remember where the click landed so the deferred activation can start a fresh trailing block
        // when the click is below the text (rather than editing the last line).
        _pendingClickPoint = e.GetPosition(_rtb);
    }

    /// <summary>True when <paramref name="pt"/> (in RichTextBox coordinates) is below the last line of text.</summary>
    private bool IsBelowContent(Point pt)
    {
        var last = _rtb.Document.Blocks.LastBlock;
        if (last is null) return true;
        try { return pt.Y > last.ContentEnd.GetCharacterRect(LogicalDirection.Backward).Bottom; }
        catch { return false; }
    }

    // ── Drag-and-drop onto the editor ─────────────────────────────────────

    private void OnPreviewDrag(object? sender, DragEventArgs e)
    {
        e.Effects  = AcceptsDropData(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled  = true;   // suppress the RichTextBox's own drag handling (which rejects files/images)
    }

    private void OnPreviewDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        ContentDropped?.Invoke(e.Data, e.GetPosition(_rtb));
    }

    private static bool AcceptsDropData(IDataObject d)
        => d.GetDataPresent(DataFormats.FileDrop) || d.GetDataPresent(DataFormats.Bitmap)
        || d.GetDataPresent(DataFormats.UnicodeText) || d.GetDataPresent(DataFormats.Text);

    /// <summary>Leaves edit mode: drops transient empty blocks, re-renders, and pushes the markdown.
    /// When not editing (a render-mode drag-select / Select All), collapses the selection so it doesn't
    /// stay highlighted after focus moves away (inactive-selection highlight is on).</summary>
    private void CommitEdit()
    {
        if (_active >= 0)
        {
            _blocks = MarkdownBlocks.Compact(_blocks);
            _active = -1;
            _undoGroupBlock = -2;
            RenderAll();           // a fresh document → the selection is cleared
            PushMarkdown();
        }
        else if (!_rtb.Selection.IsEmpty)
        {
            _suppress = true;
            try { _rtb.Selection.Select(_rtb.Selection.Start, _rtb.Selection.Start); }
            finally { _suppress = false; }
        }
    }

    /// <summary>The <see cref="Hyperlink"/> containing <paramref name="pos"/>, or null.</summary>
    private static Hyperlink? FindHyperlink(TextPointer pos)
    {
        for (DependencyObject? el = pos.Parent; el is not null;
             el = el is FrameworkContentElement fce ? fce.Parent : null)
            if (el is Hyperlink h) return h;
        return null;
    }

    // ── Right-click menu (Cut / Copy / Paste / Select All) ────────────────
    // The RichTextBox's built-in editing menu is unstyled and a right-click would clear the
    // selection and steal focus (rebuilding the doc into render mode). We supply our own themed
    // menu, preserve the selection, and keep edit mode while it is open.

    private ContextMenu BuildContextMenu()
    {
        _cutItem   = NewMenuItem("Cut",   DoCut);
        _copyItem  = NewMenuItem("Copy",  DoCopy);
        _pasteItem = NewMenuItem("Paste", DoPaste);

        var menu = new ContextMenu();
        menu.Items.Add(_cutItem);
        menu.Items.Add(_copyItem);
        menu.Items.Add(_pasteItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(NewMenuItem("Select All", SelectAll));
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            // Resume editing, or take focus so a render-mode selection (e.g. Select All) stays highlighted.
            if (_active >= 0 || !_rtb.Selection.IsEmpty) { _rtb.Focus(); Keyboard.Focus(_rtb); }
        };
        return menu;
    }

    private static MenuItem NewMenuItem(string header, Action onClick)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => onClick();
        return item;
    }

    private void OnPreviewMouseRightButtonDown(object? sender, MouseButtonEventArgs e)
    {
        // A right-click must NOT focus/activate this editor (that would drop the post-it into edit mode)
        // and must NOT disturb an existing selection. Swallowing the event keeps WPF from doing either;
        // the menu is opened on button-up.
        e.Handled = true;
    }

    private void OnPreviewMouseRightButtonUp(object? sender, MouseButtonEventArgs e)
    {
        UpdateMenuState();
        _menuOpen = true;
        var menu = _rtb.ContextMenu!;
        menu.PlacementTarget = _rtb;
        menu.Placement       = PlacementMode.MousePoint;
        menu.IsOpen          = true;
        e.Handled = true;
    }

    private void UpdateMenuState()
    {
        bool hasSel = !_rtb.Selection.IsEmpty;
        _cutItem.IsEnabled   = hasSel && _active >= 0;
        _copyItem.IsEnabled  = true;                       // copies the selection, or the whole note
        _pasteItem.IsEnabled = _active >= 0 && ClipboardHasContent();
    }

    private static bool ClipboardHasContent()
    {
        try { return Clipboard.ContainsText() || Clipboard.ContainsImage() || Clipboard.ContainsFileDropList(); }
        catch { return false; }
    }

    /// <summary>Copies the selection (or the whole note when nothing is selected) as markdown, with the
    /// blank-line separators between blocks preserved. Acts on THIS editor's model — never the one that
    /// happens to hold focus.</summary>
    private void DoCopy()
    {
        var md = SelectedMarkdown();
        if (string.IsNullOrEmpty(md)) return;
        try { Clipboard.SetText(md); } catch { }
    }

    /// <summary>The markdown for the current selection (the whole note when nothing is selected). Both
    /// endpoints are mapped to (block, source-offset) — exact for the block being edited, and via each
    /// rendered run's source span for rendered blocks — so a partial selection across rendered blocks
    /// copies just the selected text with the blank-line separators preserved.</summary>
    private string SelectedMarkdown()
    {
        var sel = _rtb.Selection;
        if (sel.IsEmpty) return MarkdownBlocks.Join(_blocks);

        var (sb, so) = PointerToModel(sel.Start, preferEnd: false);
        var (eb, eo) = PointerToModel(sel.End,   preferEnd: true);
        if (sb < 0 || eb < 0) return sel.Text;                       // fallback: rendered text
        if (sb > eb || (sb == eb && so > eo)) ((sb, so), (eb, eo)) = ((eb, eo), (sb, so));

        so = Math.Clamp(so, 0, _blocks[sb].Length);
        eo = Math.Clamp(eo, 0, _blocks[eb].Length);
        if (sb == eb) return _blocks[sb][so..eo];

        var parts = new List<string> { _blocks[sb][so..] };
        for (int i = sb + 1; i < eb; i++) parts.Add(_blocks[i]);
        parts.Add(_blocks[eb][..eo]);
        return MarkdownBlocks.Join(parts);
    }

    /// <summary>Maps a document pointer to a (block, in-block source offset). For a rendered block it uses
    /// the run's <see cref="Markdig.Syntax.SourceSpan"/>; where no source-mapped run is under the pointer
    /// (block boundary / link / image) it snaps to the block's start or end per <paramref name="preferEnd"/>.</summary>
    private (int block, int offset) PointerToModel(TextPointer p, bool preferEnd)
    {
        if (_activePara is not null
            && p.CompareTo(_activePara.ContentStart) >= 0 && p.CompareTo(_activePara.ContentEnd) <= 0)
            return (_active, Math.Clamp(CharCount(_activePara.ContentStart, p), 0, _blocks[_active].Length));

        int b = BlockIndexAtPointer(p);
        if (b < 0) return (-1, 0);
        if (p.Parent is Run { Tag: Markdig.Syntax.SourceSpan span } run && !span.IsEmpty)
            return (b, Math.Clamp(span.Start + CharCount(run.ContentStart, p), 0, _blocks[b].Length));
        return (b, preferEnd ? _blocks[b].Length : 0);
    }

    private void DoCut()
    {
        if (_active < 0) return;
        var (from, to) = SelectionInActiveBlock();
        if (from == to) return;
        DoCopy();
        EditActive(from, to, string.Empty);
    }

    private void DoPaste()
    {
        if (_active < 0) return;
        IDataObject? data;
        try { data = Clipboard.GetDataObject(); } catch { return; }
        if (data is null) return;

        // Same path as Ctrl+V: let the host claim rich content (image / file / URL); else inline text.
        if (ContentPasted?.Invoke(data) == true) return;

        var md = MarkdownClipboard.ReadBestMarkdown(data);
        if (string.IsNullOrEmpty(md)) return;
        var (from, to) = SelectionInActiveBlock();
        InsertPaste(md, from, to);
    }

    /// <summary>Selects the whole note and focuses the editor so the selection is visible even when the
    /// note isn't being edited (an unfocused selection wouldn't paint).</summary>
    private void SelectAll()
    {
        _rtb.SelectAll();
        _rtb.Focus();
        Keyboard.Focus(_rtb);
    }

    // ── Undo (block-model) ────────────────────────────────────────────────

    /// <summary>Records the pre-edit state at the start of an editing session so <see cref="Undo"/> can
    /// restore it. Edits within the same block coalesce (one undo step per block session), so undo is
    /// block-level, not per keystroke. No-op when not editing.</summary>
    private void Snapshot()
    {
        if (_active < 0) return;
        if (_active == _undoGroupBlock) return;   // already snapshotted this block's session
        _undo.Add(([.. _blocks], _active, CaretInActiveBlock()));
        if (_undo.Count > UndoLimit) _undo.RemoveAt(0);
        _undoGroupBlock = _active;
    }

    private void Undo()
    {
        if (_undo.Count == 0) return;
        var (blocks, active, caret) = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _blocks = blocks;
        _undoGroupBlock = -2;                       // next edit starts a fresh undo group
        Activate(Math.Clamp(active, 0, _blocks.Count - 1), caret);
        PushMarkdown();
    }

    private void OnPreviewTextInput(object? sender, TextCompositionEventArgs e)
    {
        if (_active < 0) return;
        var text = e.Text;
        if (string.IsNullOrEmpty(text) || text is "\r" or "\n") return;  // Enter handled in keydown
        var (from, to) = SelectionInActiveBlock();
        EditActive(from, to, text);
        e.Handled = true;
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_active < 0) return;
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var block  = _blocks[_active];

        switch (e.Key)
        {
            case Key.Enter when ctrl:
            {
                var (from, to) = SelectionInActiveBlock();
                EditActive(from, to, "  \n");   // markdown hard break, stay in block
                e.Handled = true;
                break;
            }
            case Key.Z when ctrl:
                Undo();
                e.Handled = true;
                break;
            case Key.Enter:
            {
                // Split the block at the caret → a new block (the one you leave renders).
                Snapshot();
                var (from, to) = SelectionInActiveBlock();
                string before = block[..from], after = block[Math.Max(from, to)..];
                _blocks[_active] = before;
                _blocks.Insert(_active + 1, after);
                PushMarkdown();
                Activate(_active + 1, 0);
                e.Handled = true;
                break;
            }
            case Key.Tab:
            {
                var (from, to) = SelectionInActiveBlock();
                EditActive(from, to, "\t");
                e.Handled = true;
                break;
            }
            case Key.Space:
            {
                var (from, to) = SelectionInActiveBlock();
                EditActive(from, to, " ");
                e.Handled = true;
                break;
            }
            case Key.Back:
            {
                var (from, to) = SelectionInActiveBlock();
                if (from != to) EditActive(from, to, string.Empty);
                else if (from > 0) EditActive(from - 1, from, string.Empty);
                else if (_active > 0) MergeWithPrevious();
                e.Handled = true;
                break;
            }
            case Key.Delete when !ctrl:
            {
                var (from, to) = SelectionInActiveBlock();
                if (from != to) EditActive(from, to, string.Empty);
                else if (from < block.Length) EditActive(from, from + 1, string.Empty);
                else if (_active < _blocks.Count - 1) MergeWithNext();
                e.Handled = true;
                break;
            }
            case Key.Up:
            {
                int caret   = CaretInActiveBlock();
                int firstNl = block.IndexOf('\n');
                if ((firstNl < 0 || caret <= firstNl) && _active > 0)
                {
                    Activate(_active - 1, _blocks[_active - 1].Length);
                    e.Handled = true;
                }
                break;
            }
            case Key.Down:
            {
                int caret  = CaretInActiveBlock();
                int lastNl = block.LastIndexOf('\n');
                if (caret > lastNl && _active < _blocks.Count - 1)
                {
                    Activate(_active + 1, 0);
                    e.Handled = true;
                }
                break;
            }
            case Key.Escape:
                Keyboard.ClearFocus();
                e.Handled = true;
                break;
        }
    }

    private void MergeWithPrevious()
    {
        Snapshot();
        int join = _blocks[_active - 1].Length;
        _blocks[_active - 1] += _blocks[_active];
        _blocks.RemoveAt(_active);
        PushMarkdown();
        Activate(_active - 1, join);
    }

    private void MergeWithNext()
    {
        Snapshot();
        int join = _blocks[_active].Length;
        _blocks[_active] += _blocks[_active + 1];
        _blocks.RemoveAt(_active + 1);
        PushMarkdown();
        Activate(_active, join);
    }

    private void OnPreviewExecuted(object? sender, ExecutedRoutedEventArgs e)
    {
        if (_active < 0) return;
        if (e.Command == ApplicationCommands.Cut)
        {
            var (from, to) = SelectionInActiveBlock();
            if (from != to) { _rtb.Copy(); EditActive(from, to, string.Empty); }
            e.Handled = true;
        }
        else if (e.Command == EditingCommands.ToggleBold)      { Wrap("**"); e.Handled = true; }
        else if (e.Command == EditingCommands.ToggleItalic)    { Wrap("*");  e.Handled = true; }
        else if (e.Command == EditingCommands.ToggleUnderline) { e.Handled = true; }
    }

    private void Wrap(string marker)
    {
        var (from, to) = SelectionInActiveBlock();
        var selected = _blocks[_active].Substring(from, to - from);
        EditActive(from, to, marker + selected + marker);
    }

    private void OnPasting(object? sender, DataObjectPastingEventArgs e)
    {
        var data = e.DataObject;
        e.CancelCommand();
        if (_active < 0) return;

        // DataObject.Pasting fires inside the RichTextBox's change block, where rebuilding the document
        // throws — apply the paste after the operation unwinds.
        var (from, to) = SelectionInActiveBlock();
        Dispatcher.BeginInvoke(() =>
        {
            // Let the host claim rich content (image / file / URL) → inserted as a block.
            if (ContentPasted?.Invoke(data) == true) return;

            // Plain text / markdown → inline paste at the caret.
            var pasted = MarkdownClipboard.ReadBestMarkdown(data);
            if (!string.IsNullOrEmpty(pasted)) InsertPaste(pasted, from, to);
        }, DispatcherPriority.Background);
    }

    private void InsertPaste(string pasted, int from, int to)
    {
        if (_active < 0) return;

        // A multi-block paste becomes multiple blocks; a single-block paste edits in place.
        var parts = MarkdownBlocks.Split(pasted);
        if (parts.Count == 1) { EditActive(from, to, parts[0]); return; }

        Snapshot();
        var block = _blocks[_active];
        from = Math.Clamp(from, 0, block.Length);
        to   = Math.Clamp(to,   from, block.Length);
        string before = block[..from], after = block[to..];
        _blocks[_active] = before + parts[0];
        _blocks.InsertRange(_active + 1, parts.Skip(1));
        int newActive = _active + parts.Count - 1;
        int caret = _blocks[newActive].Length;
        _blocks[newActive] += after;
        PushMarkdown();
        Activate(newActive, caret);
    }

    // ── Caret / selection within the active block ─────────────────────────

    private void OnSelectionChanged(object? sender, RoutedEventArgs e)
    {
        if (_suppress || !_rtb.IsKeyboardFocusWithin) return;
        if (!_rtb.Selection.IsEmpty) return;     // leave multi-char selections alone
        var (block, _) = CaretLocation();
        if (block != _active) ScheduleNavigate();
    }

    private void OnLostFocus(object? sender, KeyboardFocusChangedEventArgs e)
    {
        if (_suppress || _menuOpen) return;          // a context menu took focus — stay in edit mode
        CommitEdit();
    }

    /// <summary>
    /// Defers activating the block under the caret until after the current input/navigation completes —
    /// crucially, after the mouse is released, so it never disturbs the RichTextBox's mouse capture
    /// (rebuilding the document mid-click leaves capture stuck, which manifested as "needs two clicks").
    /// Skips activation when a drag left a selection (so drag-select stays in render mode).
    /// </summary>
    private void ScheduleNavigate()
    {
        if (_navQueued || _menuOpen) return;
        _navQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _navQueued = false;
            var clickPoint = _pendingClickPoint;
            _pendingClickPoint = null;

            if (_suppress || _menuOpen || !_rtb.IsKeyboardFocusWithin || !_rtb.Selection.IsEmpty) return;

            // A click in the empty space below the text starts a fresh trailing block.
            if (clickPoint is { } pt && IsBelowContent(pt))
            {
                if (_blocks[^1].Trim().Length != 0) { _blocks.Add(string.Empty); PushMarkdown(); }
                Activate(_blocks.Count - 1, 0);
                return;
            }

            var (block, off) = CaretLocation();
            if (block != _active) Activate(block, off);
        }, DispatcherPriority.Background);
    }

    private int CaretInActiveBlock()
    {
        if (_activePara is null) return 0;
        return Math.Clamp(CharCount(_activePara.ContentStart, _rtb.CaretPosition), 0, _blocks[_active].Length);
    }

    private (int from, int to) SelectionInActiveBlock()
    {
        var sel = _rtb.Selection;
        if (sel.IsEmpty || _activePara is null) { int c = CaretInActiveBlock(); return (c, c); }

        // Only treat the selection as a range when it lies within the active block.
        if (sel.Start.CompareTo(_activePara.ContentStart) >= 0 && sel.End.CompareTo(_activePara.ContentEnd) <= 0)
        {
            int a = Math.Clamp(CharCount(_activePara.ContentStart, sel.Start), 0, _blocks[_active].Length);
            int b = Math.Clamp(CharCount(_activePara.ContentStart, sel.End),   0, _blocks[_active].Length);
            return a <= b ? (a, b) : (b, a);
        }
        int caret = CaretInActiveBlock();
        return (caret, caret);
    }

    /// <summary>The (block index, in-block offset) the caret currently sits in.</summary>
    private (int block, int offset) CaretLocation()
    {
        var caret = _rtb.CaretPosition;
        if (_activePara is not null &&
            caret.CompareTo(_activePara.ContentStart) >= 0 && caret.CompareTo(_activePara.ContentEnd) <= 0)
            return (_active, CaretInActiveBlock());

        int b = BlockIndexAtPointer(caret);
        if (b >= 0)
        {
            // Rendered-block runs carry a SourceSpan local to that block's content.
            if (caret.Parent is Run { Tag: Markdig.Syntax.SourceSpan span } run && !span.IsEmpty)
                return (b, Math.Clamp(span.Start + CharCount(run.ContentStart, caret), 0, _blocks[b].Length));
            // No source-mapped run under the caret (e.g. a link, or empty space) → caret at end of block,
            // not the start, so clicking after a link doesn't jump the caret to the block's beginning.
            return (b, _blocks[b].Length);
        }
        return (_active >= 0 ? _active : 0, 0);
    }

    private int BlockIndexAtPointer(TextPointer p)
    {
        foreach (var block in _rtb.Document.Blocks)
            if (block.Tag is int idx &&
                p.CompareTo(block.ContentStart) >= 0 && p.CompareTo(block.ContentEnd) <= 0)
                return idx;
        return -1;
    }

    /// <summary>Counts characters between two pointers — Run text (incl. trailing spaces)
    /// plus one '\n' per <see cref="LineBreak"/>. Avoids <c>TextRange.Text</c>, which trims
    /// trailing whitespace and would mis-count.</summary>
    private static int CharCount(TextPointer from, TextPointer to)
    {
        if (from.CompareTo(to) >= 0) return 0;
        int count = 0;
        var p = from;
        while (p is not null && p.CompareTo(to) < 0)
        {
            switch (p.GetPointerContext(LogicalDirection.Forward))
            {
                case TextPointerContext.Text:
                    int run = p.GetTextRunLength(LogicalDirection.Forward);
                    var end = p.GetPositionAtOffset(run, LogicalDirection.Forward);
                    if (end is null || end.CompareTo(to) > 0)
                        return count + p.GetOffsetToPosition(to);
                    count += run;
                    p = end;
                    break;
                case TextPointerContext.ElementStart
                    when p.GetAdjacentElement(LogicalDirection.Forward) is LineBreak:
                    count += 1;
                    p = p.GetNextContextPosition(LogicalDirection.Forward)!;
                    break;
                default:
                    p = p.GetNextContextPosition(LogicalDirection.Forward)!;
                    break;
            }
        }
        return count;
    }

    // ── Rendering ─────────────────────────────────────────────────────────

    private void RenderAll()
    {
        _suppress = true;
        try
        {
            _active     = -1;
            _activePara = null;
            var doc = NewDocument();
            for (int i = 0; i < _blocks.Count; i++)
                foreach (var b in RenderBlock(i)) doc.Blocks.Add(b);
            _rtb.Document = doc;
        }
        finally { _suppress = false; }
        UpdatePlaceholder();
    }

    private void Activate(int index, int caretOffset)
    {
        if (_blocks.Count == 0) _blocks.Add(string.Empty);
        index = Math.Clamp(index, 0, _blocks.Count - 1);

        _suppress = true;
        try
        {
            _active = index;
            var doc = NewDocument();
            _activePara = null;
            for (int i = 0; i < _blocks.Count; i++)
            {
                if (i == index)
                {
                    _activePara = BuildSourceParagraph(_blocks[i]);
                    doc.Blocks.Add(_activePara);
                }
                else
                {
                    foreach (var b in RenderBlock(i)) doc.Blocks.Add(b);
                }
            }
            _rtb.Document = doc;
            _rtb.CaretPosition = OffsetToPointer(caretOffset);
        }
        finally { _suppress = false; }
        UpdatePlaceholder();
    }

    private FlowDocument NewDocument() => new()
    {
        FontFamily  = BlockRenderer.BodyFont,
        FontSize    = BlockRenderer.BaseFontSize,
        Foreground  = Pal.Text,
        Background  = Brushes.Transparent,
        PagePadding = new Thickness(0),
    };

    private IEnumerable<Block> RenderBlock(int index)
    {
        var text = _blocks[index];
        if (text.Trim().Length == 0)
        {
            // Empty block while it is not being edited: a thin clickable line.
            var ph = new Paragraph(new Run(" ")) { Foreground = Pal.TextMuted, Tag = index };
            return [ph];
        }

        var fd = MarkdownFlowDocument.Build(text, Context);
        var blocks = fd.Blocks.ToList();
        fd.Blocks.Clear();
        foreach (var b in blocks) b.Tag = index;   // map a caret in this block back to its index
        return blocks;
    }

    private Paragraph BuildSourceParagraph(string text)
    {
        var p = new Paragraph
        {
            Margin     = new Thickness(0, 2, 0, 2),
            Background = Pal.CodeBg,                // subtle tint marks the block being edited
            FontFamily = BlockRenderer.BodyFont,
            Foreground = Pal.Text,
        };
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) p.Inlines.Add(new LineBreak());
            if (lines[i].Length > 0)               // never add an empty Run — it breaks the caret
                p.Inlines.Add(new Run(PreserveSpaces(lines[i])));
        }
        return p;
    }

    /// <summary>
    /// WPF trims/collapses leading, trailing and repeated spaces in a Run (even from the
    /// model), which would drop characters from the source view. Render those spaces as
    /// non-breaking spaces — they survive layout and stay 1:1 with the real spaces in the
    /// block content, so caret offsets line up. Single spaces between words stay real so
    /// the line can still wrap.
    /// </summary>
    private static string PreserveSpaces(string line)
    {
        if (line.Length == 0) return line;
        var sb = new System.Text.StringBuilder(line.Length);
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == ' ' &&
                (i == 0 || i == line.Length - 1 || line[i - 1] == ' ' || line[i + 1] == ' '))
                sb.Append(' ');   // collapsible space → non-breaking space (U+00A0)
            else
                sb.Append(line[i]);
        }
        return sb.ToString();
    }

    private TextPointer OffsetToPointer(int offset)
    {
        if (_activePara is null) return _rtb.CaretPosition;
        int remaining = Math.Clamp(offset, 0, _blocks[_active].Length);

        var p = _activePara.ContentStart;
        while (remaining > 0 && p is not null)
        {
            switch (p.GetPointerContext(LogicalDirection.Forward))
            {
                case TextPointerContext.Text:
                    int run = p.GetTextRunLength(LogicalDirection.Forward);
                    if (run >= remaining)
                        return p.GetPositionAtOffset(remaining, LogicalDirection.Forward) ?? p;
                    remaining -= run;
                    p = p.GetPositionAtOffset(run, LogicalDirection.Forward);
                    break;
                case TextPointerContext.ElementStart
                    when p.GetAdjacentElement(LogicalDirection.Forward) is LineBreak:
                    remaining -= 1;
                    p = p.GetNextContextPosition(LogicalDirection.Forward);
                    if (remaining == 0 && p is not null) return p;
                    break;
                default:
                    p = p.GetNextContextPosition(LogicalDirection.Forward);
                    break;
            }
        }
        return p ?? _activePara.ContentEnd;
    }

    private void UpdatePlaceholder()
        => _placeholder.Visibility =
            !_rtb.IsKeyboardFocusWithin && _blocks.All(b => b.Trim().Length == 0) && _placeholder.Text.Length > 0
                ? Visibility.Visible : Visibility.Collapsed;
}
