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
/// There are two editing modes, both keeping the block model authoritative:
/// <list type="bullet">
/// <item><b>Word-style (single click + type):</b> plain-text edits applied to the rendered
/// paragraph in place — no source view, no rebuild, no scroll jump. Typed text inherits the
/// formatting around the caret but never creates formatting (typed markdown syntax is escaped
/// to stay literal). After every edit the paragraph is serialized back to markdown
/// (<see cref="MarkdownInlineSerializer"/>); a session only starts once the pristine paragraph
/// provably round-trips to the exact block source, so this mode can never corrupt a document.</item>
/// <item><b>Source mode (double click, or any block Word-style can't serve):</b> the block swaps
/// to its raw markdown source; input is intercepted and applied to the model, and the document
/// is rebuilt (which avoids the re-entrancy crash from mutating a RichTextBox inside its own
/// change block). Caret/selection navigation stays native.</item>
/// </list>
///
/// Editing semantics: Enter = new block (the block you leave renders); Ctrl+Enter = a
/// markdown hard break inside the current block (source mode); Tab = a tab. Block separators
/// are NOT part of a block's content, so editing a block never shows or doubles the blank-line
/// separator.
///
/// Offsets within the active block are measured at the run level
/// (<see cref="TextPointer.GetTextRunLength"/>), NOT via <c>TextRange.Text</c>, because
/// the latter trims trailing whitespace and would drop spaces (e.g. "## " → "##").
/// </summary>
public partial class InlineMarkdownEditor : UserControl
{
    private readonly RichTextBox _rtb;
    private readonly TextBlock   _placeholder;
    private readonly MarkdownEditToolbar _editBar;
    private readonly Popup       _editBarPopup;

    private List<string> _blocks = [""];
    private int        _active = -1;          // block shown as source, or -1 when fully rendered
    private Paragraph? _activePara;

    // Word-style (native) edit session: typing into a RENDERED block edits the document in place —
    // no source view, no rebuild — and the model is kept in sync by serializing the edited paragraph
    // back to markdown after every edit (see MarkdownInlineSerializer + EnsureNativeSession).
    private int        _nativeBlock = -1;     // block being edited Word-style, or -1
    private Paragraph? _nativePara;
    private string     _nativePrefix = "";    // block-level prefix outside the inlines (e.g. "## ")
    private bool       _suppress;             // guard around programmatic document/caret changes
    private bool       _navQueued;
    private bool       _menuOpen;             // a context menu is open → don't treat the focus loss as leaving edit mode
    private Point?     _pendingClickPoint;    // where the last left-click landed, consumed by the deferred activation
    private IInteractiveBlock? _pointerBlock; // interactive block owning the in-progress click/drag gesture
    private Point?     _dragArm;              // press point when it landed on the selection — a potential copy drag-out
    private bool       _renderPending;        // a RenderAll was requested while hidden → run it once the editor is shown

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
        // Spell-check is enabled only while a block is being edited (see Activate/RenderAll) so the
        // rendered text doesn't show squiggles.
        SpellCheck.SetIsEnabled(_rtb, false);

        _rtb.PreviewTextInput  += OnPreviewTextInput;
        _rtb.PreviewKeyDown    += OnPreviewKeyDown;
        _rtb.PreviewMouseLeftButtonDown  += OnPreviewMouseLeftButtonDown;
        _rtb.PreviewMouseLeftButtonUp    += OnPreviewMouseLeftButtonUp;  // ends an interactive-block gesture
        _rtb.PreviewMouseMove            += OnPreviewMouseMove;   // pre-empt the native (move) drag-out with a copy
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

        // Caret + text brushes. With no explicit Palette the editor is theme-aware, so track the live
        // theme brushes (the caret was otherwise the default black — invisible on a dark theme).
        ApplyEditorBrushes();

        // While hidden (e.g. the markdown viewer's source-only mode swaps this editor out), a bound
        // Markdown change still fires RenderAll per keystroke — wasted work for a doc with diagrams.
        // Defer the render and run it once when the editor is shown again with the latest content.
        IsVisibleChanged += (_, _) => { if (IsVisible && _renderPending) RenderAll(); };

        // Right-click formatting toolbar (shown while editing). It takes focus while open, so guard the
        // focus loss like the context menu does (_menuOpen) and hand focus back to the editor on close.
        _editBar = new MarkdownEditToolbar();
        _editBar.ActionInvoked += OnEditAction;
        _editBarPopup = new Popup
        {
            Child              = _editBar,
            StaysOpen          = false,
            AllowsTransparency = true,
            Placement          = PlacementMode.MousePoint,
            PlacementTarget    = _rtb,
            PopupAnimation     = PopupAnimation.Fade,
        };
        _editBarPopup.Opened += (_, _) => _menuOpen = true;
        _editBarPopup.Closed += (_, _) =>
        {
            _menuOpen = false;
            if (_active >= 0) { _rtb.Focus(); Keyboard.Focus(_rtb); }
        };
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

    /// <summary>When true, a single click only selects/places the caret (everywhere — text and diagrams
    /// alike) and a <em>double</em>-click enters edit mode for the block under the cursor. When false
    /// (the default, used by the scratchpad), a single click enters edit mode. The markdown viewer sets
    /// this so you can select a diagram without it dropping into source.</summary>
    public static readonly DependencyProperty EditOnDoubleClickProperty =
        DependencyProperty.Register(nameof(EditOnDoubleClick), typeof(bool), typeof(InlineMarkdownEditor),
            new PropertyMetadata(false));

    public bool EditOnDoubleClick
    {
        get => (bool)GetValue(EditOnDoubleClickProperty);
        set => SetValue(EditOnDoubleClickProperty, value);
    }

    private MarkdownRenderContext Context => new()
    {
        Palette           = Pal,
        OnNavigate        = LinkNavigate,
        BaseDirectory     = BaseDirectory,
        FitContentToWidth = true,   // diagrams scale to the column rather than getting un-grabbable scrollbars
    };

    private void OnPaletteChanged()
    {
        ApplyEditorBrushes();
        if (!_rtb.IsKeyboardFocusWithin) RenderAll();
    }

    /// <summary>Sets the caret/text/placeholder brushes. With an explicit <see cref="Palette"/> they are
    /// the palette's (frozen) brushes; with none, the editor is theme-aware and tracks the live theme
    /// brushes via resource references — so the caret stays visible on dark themes and follows theme
    /// switches.</summary>
    private void ApplyEditorBrushes()
    {
        if (Palette is not null)
        {
            _rtb.Foreground = Pal.Text;
            _rtb.CaretBrush = Pal.Text;
            _placeholder.Foreground = Pal.TextMuted;
        }
        else
        {
            _rtb.SetResourceReference(ForegroundProperty, "TextBrush");
            _rtb.SetResourceReference(TextBoxBase.CaretBrushProperty, "TextBrush");
            _placeholder.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        }
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
        self.ClearNativeSession();                   // external truth replaces any stale native edit
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

    /// <summary>Scrolls the rendered view so the heading whose <em>ancestor path</em> equals
    /// <paramref name="titlePath"/> sits at the top — matched on the full heading hierarchy (not a
    /// document-wide text match), so duplicate heading names under different parents stay distinct. No-op if
    /// no such heading exists. Deferred to a layout pass so the block rects are measured first.</summary>
    public void ScrollToHeading(IReadOnlyList<string>? titlePath)
    {
        int blockIndex = MarkdownBlocks.FindHeadingBlock(_blocks, titlePath);
        if (blockIndex < 0) return;

        Dispatcher.BeginInvoke(() =>
        {
            try { if (BlockTopY(blockIndex) is double y) _rtb.ScrollToVerticalOffset(_rtb.VerticalOffset + y - 8); }
            catch { /* not laid out yet — leave the scroll where it is */ }
        }, DispatcherPriority.Loaded);
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

        SyncNativeModel();                                                               // commit a Word-style edit
        ClearNativeSession();                                                            // (indices shift below)
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
        SyncNativeModel();          // a Word-style edit may be in flight — settle the model first
        ClearNativeSession();
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
    /// A click strictly inside a rendered link follows it. Otherwise behaviour depends on
    /// <see cref="EditOnDoubleClick"/>:
    /// <list type="bullet">
    /// <item>Default (single-click-to-edit): the click is left to WPF (focus + caret) and the block under
    /// the caret is activated by the deferred <see cref="ScheduleNavigate"/> — after the mouse is released
    /// so it never disturbs the RichTextBox's mouse capture, skipping activation when a drag selected text.</item>
    /// <item><see cref="EditOnDoubleClick"/>: a single click only selects/places the caret (and commits any
    /// in-progress edit when it lands outside the active block); a double click enters edit on the block
    /// under the cursor.</item>
    /// </list>
    /// </summary>
    private void OnPreviewMouseLeftButtonDown(object? sender, MouseButtonEventArgs e)
    {
        _pendingClickPoint = null;

        // Arm a copy-only drag-out when the press lands inside an existing selection (OnPreviewMouseMove
        // turns it into a drag once it crosses the threshold). A press anywhere else is a normal click /
        // drag-select and leaves _dragArm null.
        _dragArm = null;
        if (!_rtb.Selection.IsEmpty
            && _rtb.GetPositionFromPoint(e.GetPosition(_rtb), snapToText: true) is { } hit
            && hit.CompareTo(_rtb.Selection.Start) >= 0 && hit.CompareTo(_rtb.Selection.End) <= 0)
            _dragArm = e.GetPosition(_rtb);

        // A single click on a self-handling block (e.g. a music score) belongs to that element — it owns
        // measure/note selection. The event's source can't be trusted here: the RichTextBox's text container
        // attributes clicks over an embedded UIElement island to the container — or even to a NEIGHBOURING
        // Paragraph/Run for parts of the island — so we locate the element with a geometric visual hit-test
        // instead and drive it directly (it captures the mouse for the drag). Disarm the copy-drag and
        // suppress the caret/activation so the whole block isn't selected. Double-click still falls through
        // to enter source-edit mode.
        if (e.ClickCount == 1 && InteractiveBlockAtPoint(e.GetPosition(_rtb)) is { } ib && ib is UIElement uie)
        {
            _pendingClickPoint = null;
            _dragArm = null;                    // a score drag must not become the RTB's copy-drag
            _pointerBlock = ib;
            ib.BeginPointerSelect(e.GetPosition(uie));
            // Capture to the RTB so the drag keeps flowing to our move/up handlers even when the pointer
            // leaves the element or the control (the embedded element itself can't hold capture reliably).
            Mouse.Capture(_rtb);
            e.Handled = true;
            return;
        }

        // A plain click elsewhere on the page drops any active interactive-block (e.g. music) selection.
        InteractiveSelection.ClearActive();

        if (EditOnDoubleClick)
        {
            if (e.ClickCount == 2)
            {
                var p = _rtb.GetPositionFromPoint(e.GetPosition(_rtb), snapToText: true);
                // Already editing this block → let WPF select the word; otherwise enter edit at the click.
                bool inActive = _activePara is not null && p is not null
                    && p.CompareTo(_activePara.ContentStart) >= 0 && p.CompareTo(_activePara.ContentEnd) <= 0;
                if (inActive) return;
                e.Handled = true;          // suppress word-select; we enter edit instead
                ActivateAtPoint(e.GetPosition(_rtb));
                return;
            }

            if (TryNavigateLink(e)) return;

            // A single click outside the block being edited leaves edit mode (so typing can't target a
            // block the caret has left); a click within it just repositions the caret natively.
            if (_active >= 0)
            {
                var p = _rtb.GetPositionFromPoint(e.GetPosition(_rtb), snapToText: true);
                bool inActive = _activePara is not null && p is not null
                    && p.CompareTo(_activePara.ContentStart) >= 0 && p.CompareTo(_activePara.ContentEnd) <= 0;
                if (!inActive) Dispatcher.BeginInvoke(CommitEdit, DispatcherPriority.Background);
            }
            return;
        }

        if (e.ClickCount != 1) return;   // let double-click select a word for editing
        if (TryNavigateLink(e)) return;

        // Remember where the click landed so the deferred activation can start a fresh trailing block
        // when the click is below the text (rather than editing the last line).
        _pendingClickPoint = e.GetPosition(_rtb);
    }

    /// <summary>Follows a link when the click landed strictly inside a rendered hyperlink. Returns true
    /// (and marks the event handled) when it navigated.</summary>
    private bool TryNavigateLink(MouseButtonEventArgs e)
    {
        var glyph = _rtb.GetPositionFromPoint(e.GetPosition(_rtb), snapToText: false);
        if (glyph is null || FindHyperlink(glyph) is not { NavigateUri: { } uri } link
            || glyph.CompareTo(link.ContentStart) <= 0 || glyph.CompareTo(link.ContentEnd) >= 0)
            return false;

        e.Handled = true;
        var url = uri.ToString();
        if (LinkNavigate?.Invoke(url) == true) return true;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
        return true;
    }

    /// <summary>Enters edit mode for the block under <paramref name="pointInEditor"/>, placing the caret
    /// at the click. Deferred to Background priority so it runs after the mouse is released and never
    /// disturbs the RichTextBox's capture (the same reason as <see cref="ScheduleNavigate"/>).</summary>
    private void ActivateAtPoint(Point pointInEditor)
    {
        var pos = _rtb.GetPositionFromPoint(pointInEditor, snapToText: true);
        Dispatcher.BeginInvoke(() =>
        {
            _rtb.Focus();
            Keyboard.Focus(_rtb);

            int block = -1, off = 0;
            if (pos is not null) (block, off) = PointerToModel(pos, preferEnd: false);
            if (block < 0)
            {
                if (IsBelowContent(pointInEditor))
                {
                    if (_blocks[^1].Trim().Length != 0) { _blocks.Add(string.Empty); PushMarkdown(); }
                    block = _blocks.Count - 1; off = _blocks[^1].Length;
                }
                else { block = _active >= 0 ? _active : 0; off = 0; }
            }
            // Place the top of the edit area at the click position (the mouse Y), not the top of the page.
            Activate(Math.Clamp(block, 0, _blocks.Count - 1), off, pointInEditor.Y);
        }, DispatcherPriority.Background);
    }

    /// <summary>True when <paramref name="pt"/> (in RichTextBox coordinates) is below the last line of text.</summary>
    private bool IsBelowContent(Point pt)
    {
        var last = _rtb.Document.Blocks.LastBlock;
        if (last is null) return true;
        try { return pt.Y > last.ContentEnd.GetCharacterRect(LogicalDirection.Backward).Bottom; }
        catch { return false; }
    }

    // ── Drag-out (copy-only) ──────────────────────────────────────────────
    // The block model is authoritative and WPF must never edit the document itself. The RichTextBox's
    // built-in text drag does a MOVE — after the drop it deletes the dragged selection straight from the
    // document, desyncing the model and crashing on the post-drop cleanup. Pre-empt it: once a press that
    // landed on the selection (armed in OnPreviewMouseLeftButtonDown) crosses the drag threshold, run our
    // own COPY drag. The external app still receives the text; the source document is never mutated.
    private void OnPreviewMouseLeftButtonUp(object? sender, MouseButtonEventArgs e)
    {
        if (_pointerBlock is null) return;
        EndPointerGesture();
        e.Handled = true;
    }

    /// <summary>Finishes an interactive-block gesture: releases the RTB's capture and notifies the block.</summary>
    private void EndPointerGesture()
    {
        var pb = _pointerBlock;
        _pointerBlock = null;
        if (Mouse.Captured == _rtb) Mouse.Capture(null);
        pb?.EndPointerSelect();
    }

    private void OnPreviewMouseMove(object? sender, MouseEventArgs e)
    {
        // An in-progress interactive-block gesture (music note/measure selection) owns the moves.
        if (_pointerBlock is { } pb)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                pb.ExtendPointerSelect(e.GetPosition((UIElement)pb));
                e.Handled = true;
                return;
            }
            EndPointerGesture();   // button no longer down (up happened off-element) — finish cleanly
        }

        if (_dragArm is not { } start || e.LeftButton != MouseButtonState.Pressed) { _dragArm = null; return; }

        var now = e.GetPosition(_rtb);
        if (Math.Abs(now.X - start.X) < SystemParameters.MinimumHorizontalDragDistance
         && Math.Abs(now.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _dragArm = null;
        var md = SelectedMarkdown();
        if (string.IsNullOrEmpty(md)) return;

        e.Handled = true;   // pre-empt the RichTextBox's native (move) drag, which we start before it can
        try { DragDrop.DoDragDrop(_rtb, new DataObject(DataFormats.UnicodeText, md), DragDropEffects.Copy); }
        catch { /* a failed/aborted drag must never take the app down */ }
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
        else if (_nativeBlock >= 0)
        {
            // A Word-style session ends with a re-render: the model is already synced per edit, but the
            // rebuild refreshes the runs' SourceSpan tags (stale after native edits) and re-parses typed
            // markdown syntax as the escaped literal text it was stored as.
            SyncNativeModel();
            ClearNativeSession();
            RenderAll();
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

    /// <summary>True when the mouse hit lands on (or within) an <see cref="IInteractiveBlock"/> embedded in the
    /// document — a block that owns its own click/drag input (e.g. a music score).</summary>
    /// <summary>The <see cref="IInteractiveBlock"/> under <paramref name="pointInRtb"/>, or null. Found by a
    /// geometric visual hit-test rather than the routed event's source: the text container attributes clicks
    /// over an embedded UIElement island to the <see cref="BlockUIContainer"/> — or even to a neighbouring
    /// Paragraph/Run for parts of the island — so source-based detection misses regions of the element. The
    /// hit-test also sees through wrappers (e.g. the score's warnings StackPanel), since it lands on the
    /// element itself and walks up.</summary>
    private IInteractiveBlock? InteractiveBlockAtPoint(Point pointInRtb)
    {
        var hit = VisualTreeHelper.HitTest(_rtb, pointInRtb)?.VisualHit;
        for (DependencyObject? d = hit; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is IInteractiveBlock ib) return ib;
            if (ReferenceEquals(d, _rtb)) break;   // stop at the host
        }
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
        e.Handled = true;

        // While editing a block, offer the formatting mini-toolbar; otherwise the plain Cut/Copy menu.
        if (_active >= 0) { OpenEditBar(); return; }

        UpdateMenuState();
        _menuOpen = true;
        var menu = _rtb.ContextMenu!;
        menu.PlacementTarget = _rtb;
        menu.Placement       = PlacementMode.MousePoint;
        menu.IsOpen          = true;
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
        SnapshotAt(_active, CaretInActiveBlock());
    }

    /// <summary>Block-level undo shared by source-mode and Word-style sessions: edits within the same
    /// block coalesce into one undo step.</summary>
    private void SnapshotAt(int block, int caret)
    {
        if (block == _undoGroupBlock) return;     // already snapshotted this block's session
        _undo.Add(([.. _blocks], block, caret));
        if (_undo.Count > UndoLimit) _undo.RemoveAt(0);
        _undoGroupBlock = block;
    }

    /// <summary>
    /// Restores the block model to the start of the last editing session (Ctrl+Z). Edits within one block
    /// coalesce into a single step, so undo is block-level rather than per-keystroke. No-op with nothing to
    /// undo. Public so a host can wire its own undo affordance — and so the step granularity is assertable.
    /// </summary>
    public void Undo()
    {
        if (_undo.Count == 0) return;
        ClearNativeSession();                     // undo discards the in-flight Word-style edit
        var (blocks, active, caret) = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _blocks = blocks;
        _undoGroupBlock = -2;                       // next edit starts a fresh undo group
        Activate(Math.Clamp(active, 0, _blocks.Count - 1), caret);
        PushMarkdown();
    }

    private void OnPreviewTextInput(object? sender, TextCompositionEventArgs e)
    {
        var text = e.Text;
        if (string.IsNullOrEmpty(text) || text is "\r" or "\n") return;  // Enter handled in keydown
        if (_active < 0)
        {
            // Word-style: type into the rendered line in place — plain text that inherits the
            // surrounding formatting; the model is re-serialized from the paragraph after the edit.
            if (TryNativeInsert(text)) { e.Handled = true; return; }
            // Fallback (tables, lists, task lists, any block we can't reconstruct losslessly):
            // enter source-edit mode at the caret and apply the edit through the block model.
            if (!ActivateAtCaretForInput()) return;
        }
        var (from, to) = SelectionInActiveBlock();
        EditActive(from, to, text);
        e.Handled = true;
    }

    /// <summary>Enters source-edit mode for the block the caret sits in. The caret is mapped to a (block,
    /// source offset) against the rendered document's run tags and the block is activated there. The
    /// fallback for blocks Word-style editing can't serve. Returns false if the caret maps to no block.</summary>
    private bool ActivateAtCaretForInput()
    {
        var (block, off) = CaretLocation();
        if (block < 0 || block >= _blocks.Count) return false;
        Activate(block, Math.Clamp(off, 0, _blocks[block].Length));
        return true;
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (_active < 0)
        {
            // Word-style editing of the rendered document. Each editing key first tries the native
            // path (edit in place + re-serialize); a block that can't be handled that way falls back
            // to source-edit mode below. Navigation and other shortcuts stay native.
            switch (e.Key)
            {
                case Key.Z when ctrl:
                    if (_undo.Count > 0) { Undo(); e.Handled = true; }
                    return;
                case Key.Enter when !ctrl:
                    if (TryNativeSplit()) { e.Handled = true; return; }
                    break;
                case Key.Back when !ctrl:
                    if (TryNativeDelete(forward: false)) { e.Handled = true; return; }
                    break;
                case Key.Delete when !ctrl:
                    if (TryNativeDelete(forward: true)) { e.Handled = true; return; }
                    break;
                case Key.Space:
                    if (TryNativeInsert(" ")) { e.Handled = true; return; }
                    break;
                case Key.Tab:
                    if (TryNativeInsert("\t")) { e.Handled = true; return; }
                    break;
                case Key.Enter:   // Ctrl+Enter (hard break) → source path
                case Key.Back:    // Ctrl+Back → source path
                case Key.Delete:  // Ctrl+Delete → source path
                    break;
                case Key.Escape:
                    CommitEdit();                    // ends a Word-style session with a re-render
                    Keyboard.ClearFocus();
                    e.Handled = true;
                    return;
                default:
                    return;                          // caret movement, selection, copy… — native
            }
            if (!ActivateAtCaretForInput()) return;
        }
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
            case Key.Enter when MarkdownBlocks.IsFenced(block):
            {
                // A fenced code/diagram block is one block — Enter is a literal newline inside it,
                // not a block split (splitting would break the fence, e.g. a mermaid diagram).
                var (from, to) = SelectionInActiveBlock();
                EditActive(from, to, "\n");
                e.Handled = true;
                break;
            }
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

    // ── Word-style (native) editing of a rendered block ───────────────────
    // Single click + type edits the rendered line IN PLACE: no source view, no document rebuild, no
    // scroll jump. Typed characters are plain text (they inherit the formatting around the caret but
    // markdown syntax the user types stays literal — it is escaped on serialization). The block model
    // stays authoritative: after every native edit the paragraph is serialized back to markdown
    // (MarkdownInlineSerializer) and written into the model. A session only starts after proving the
    // pristine paragraph serializes back to EXACTLY the block's source — any block that fails that
    // round-trip (tables, lists, task lists, sub/superscript, `__x__`-style emphasis…) falls back to
    // source-edit mode, so Word-style editing can never corrupt a document.

    /// <summary>Starts (or continues) a Word-style session for the block under the caret. Returns false
    /// when the block can't be edited natively — the caller falls back to source-edit mode.</summary>
    private bool EnsureNativeSession()
    {
        var caret = _rtb.CaretPosition;
        var para  = TopLevelParagraphAt(caret, out int b);
        if (para is null || b < 0 || b >= _blocks.Count) return false;

        // A selection reaching outside the paragraph would let a native edit touch other blocks.
        var sel = _rtb.Selection;
        if (!sel.IsEmpty && (sel.Start.CompareTo(para.ContentStart) < 0 || sel.End.CompareTo(para.ContentEnd) > 0))
            return false;

        if (_nativeBlock == b && ReferenceEquals(_nativePara, para)) return true;

        var src = _blocks[b];
        if (src.Contains('\n') || MarkdownBlocks.IsFenced(src)) return false;

        // An empty block renders as a placeholder glyph — drop it so the paragraph serializes to "".
        if (src.Length == 0 && para.Inlines.Count > 0)
        {
            _suppress = true;
            try { para.Inlines.Clear(); } finally { _suppress = false; }
            caret = _rtb.CaretPosition;
        }

        var prefix  = HeadingPrefixOf(src);
        bool escape = prefix.Length == 0;
        if (!MarkdownInlineSerializer.TrySerialize(para, null, escape, out var rebuilt)
            || prefix + rebuilt != src)
            return false;                       // not losslessly reconstructable → source-edit fallback

        SyncNativeModel();                      // settle a session on another block first
        MarkdownInlineSerializer.TrySerialize(para, caret, escape, out var upTo);
        SnapshotAt(b, prefix.Length + upTo.Length);
        _nativeBlock  = b;
        _nativePara   = para;
        _nativePrefix = prefix;
        return true;
    }

    /// <summary>Serializes the natively-edited paragraph back into the block model and pushes the
    /// markdown. No-op without a session. Never renders — the document already shows the edit.</summary>
    private void SyncNativeModel()
    {
        if (_nativeBlock < 0 || _nativePara is null) return;
        if (!MarkdownInlineSerializer.TrySerialize(_nativePara, null, _nativePrefix.Length == 0, out var md))
            return;                             // can't happen after the session-start proof; keep the model
        var full = _nativePrefix + md;
        if (_blocks[_nativeBlock] == full) return;
        _blocks[_nativeBlock] = full;
        PushMarkdown();
    }

    private void ClearNativeSession()
    {
        _nativeBlock  = -1;
        _nativePara   = null;
        _nativePrefix = "";
    }

    private bool TryNativeInsert(string text)
    {
        if (!EnsureNativeSession()) return false;
        NativeInsertText(text);
        return true;
    }

    /// <summary>Replaces the selection (or inserts at the caret) with plain text in the rendered
    /// paragraph — the text takes the formatting at the insertion point — then re-syncs the model.</summary>
    private void NativeInsertText(string text)
    {
        _suppress = true;
        try
        {
            _rtb.Selection.Text = text;
            var end = _rtb.Selection.End;
            _rtb.Selection.Select(end, end);
        }
        finally { _suppress = false; }
        SyncNativeModel();
    }

    private bool TryNativeDelete(bool forward)
    {
        if (!EnsureNativeSession()) return false;
        var sel = _rtb.Selection;
        if (sel.IsEmpty)
        {
            var caret  = _rtb.CaretPosition;
            var target = caret.GetNextInsertionPosition(forward ? LogicalDirection.Forward : LogicalDirection.Backward);
            if (target is null
                || target.CompareTo(_nativePara!.ContentStart) < 0
                || target.CompareTo(_nativePara.ContentEnd) > 0)
                return false;                   // block boundary — merging blocks is a source-mode edit
            _suppress = true;
            try { new TextRange(caret, target).Text = string.Empty; }
            finally { _suppress = false; }
        }
        else
        {
            _suppress = true;
            try { sel.Text = string.Empty; }
            finally { _suppress = false; }
        }
        SyncNativeModel();
        return true;
    }

    /// <summary>Enter during a Word-style session: split the block at the caret (replacing any selection)
    /// and re-render, caret at the start of the new block. The split offsets come from the serializer's
    /// prefix property — serialize-up-to-pointer is always a string prefix of the full serialization.</summary>
    private bool TryNativeSplit()
    {
        if (!EnsureNativeSession()) return false;
        var para   = _nativePara!;
        int b      = _nativeBlock;
        var prefix = _nativePrefix;
        bool esc   = prefix.Length == 0;
        if (!MarkdownInlineSerializer.TrySerialize(para, null, esc, out var full)) return false;
        MarkdownInlineSerializer.TrySerialize(para, _rtb.Selection.Start, esc, out var upToStart);
        MarkdownInlineSerializer.TrySerialize(para, _rtb.Selection.End,   esc, out var upToEnd);
        ClearNativeSession();

        _blocks[b] = prefix + upToStart;
        _blocks.Insert(b + 1, full[Math.Min(upToEnd.Length, full.Length)..]);
        PushMarkdown();
        RenderAll();
        MoveCaretToRenderedBlockStart(b + 1);
        return true;
    }

    /// <summary>The top-level document block containing <paramref name="p"/> as a Paragraph (with its
    /// model index), or null when it isn't a plain paragraph (section, table, list…).</summary>
    private Paragraph? TopLevelParagraphAt(TextPointer p, out int index)
    {
        index = -1;
        foreach (var blk in _rtb.Document.Blocks)
            if (blk.Tag is int i && p.CompareTo(blk.ContentStart) >= 0 && p.CompareTo(blk.ContentEnd) <= 0)
            {
                index = i;
                return blk as Paragraph;
            }
        return null;
    }

    private void MoveCaretToRenderedBlockStart(int index)
    {
        foreach (var blk in _rtb.Document.Blocks)
            if (blk.Tag is int i && i == index)
            {
                _suppress = true;
                try { _rtb.CaretPosition = blk.ContentStart; }
                finally { _suppress = false; }
                return;
            }
    }

    private static readonly System.Text.RegularExpressions.Regex HeadingPrefix =
        new(@"^#{1,6}[ \t]+", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string HeadingPrefixOf(string src) => HeadingPrefix.Match(src).Value;

    private void OnPreviewExecuted(object? sender, ExecutedRoutedEventArgs e)
    {
        if (_active < 0)
        {
            if (e.Command == ApplicationCommands.Cut)
            {
                // Within a Word-style session the native cut is fine (copy + delete inside the
                // paragraph) — re-sync afterwards. Anywhere else it would desync the model: block it.
                if (EnsureNativeSession()) Dispatcher.BeginInvoke(SyncNativeModel, DispatcherPriority.Background);
                else e.Handled = true;
            }
            else if (e.Command == EditingCommands.ToggleBold
                  || e.Command == EditingCommands.ToggleItalic
                  || e.Command == EditingCommands.ToggleUnderline)
                e.Handled = true;               // Word-style typing adds no formatting
            return;
        }
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
        EditActive(from, to, MarkdownBlockFormat.WrapSelection(selected, marker));
    }

    private void OnPasting(object? sender, DataObjectPastingEventArgs e)
    {
        var data = e.DataObject;
        e.CancelCommand();

        if (_active < 0)
        {
            // Word-style paste: single-line text goes in at the caret as plain text (like typing it);
            // rich/multi-line content becomes block(s) after the caret's block.
            Dispatcher.BeginInvoke(() =>
            {
                if (ContentPasted?.Invoke(data) == true) return;
                var pasted = MarkdownClipboard.ReadBestMarkdown(data);
                if (string.IsNullOrEmpty(pasted)) return;
                if (!pasted.Contains('\n') && TryNativeInsert(pasted)) return;
                InsertMarkdownAtCaret(pasted);
            }, DispatcherPriority.Background);
            return;
        }

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
        if (EditOnDoubleClick) return;   // activation is driven only by a double-click in this mode
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
        // Hidden → don't build the document now (it would re-run diagram layout per keystroke);
        // remember to render when the editor is next shown.
        if (!IsVisible) { _renderPending = true; return; }
        _renderPending = false;

        SyncNativeModel();                                  // never drop an in-flight Word-style edit
        ClearNativeSession();                               // the paragraph is about to be replaced

        int focal     = _active;                            // the block being left — keep it pinned across the rebuild
        double offset = _rtb.VerticalOffset;
        double? focalY = focal >= 0 ? BlockTopY(focal) : null;
        // Tear the speller down BEFORE swapping the document. WPF's Speller scans on a deferred OnIdle
        // holding pointers into the current document; replacing the document while it is still attached
        // lets a queued scan deref a stale text container and NRE (crash.log: Speller.OnIdle). No
        // squiggles in the fully-rendered view either way.
        SpellCheck.SetIsEnabled(_rtb, false);
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
        AnchorScroll(offset, focal, focalY, keepCaretVisible: false);
        UpdatePlaceholder();
    }

    /// <param name="anchorTopY">When set (a double-click entering edit), the screen-Y the activated block's
    /// top should land at — i.e. the mouse position. Otherwise the block is pinned to where it already was.</param>
    private void Activate(int index, int caretOffset, double? anchorTopY = null)
    {
        SyncNativeModel();                                  // entering source mode commits a Word-style edit
        ClearNativeSession();
        if (_blocks.Count == 0) _blocks.Add(string.Empty);
        index = Math.Clamp(index, 0, _blocks.Count - 1);

        double offset  = _rtb.VerticalOffset;
        double? targetY = anchorTopY ?? BlockTopY(index);   // pin the focal block to the mouse, else to where it is now
        // Detach the speller before swapping the document (block→block navigation rebuilds while editing),
        // then re-attach to the new document below — see the note in RenderAll.
        SpellCheck.SetIsEnabled(_rtb, false);
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
        SpellCheck.SetIsEnabled(_rtb, true);    // squiggles only while editing
        AnchorScroll(offset, index, targetY, keepCaretVisible: true);
        UpdatePlaceholder();
    }

    /// <summary>The viewport-relative Y of block <paramref name="index"/>'s top in the current document —
    /// the active source paragraph if it is that block, else the rendered block tagged with the index.</summary>
    private double? BlockTopY(int index)
    {
        try
        {
            if (index == _active && _activePara is not null)
                return _activePara.ContentStart.GetCharacterRect(LogicalDirection.Forward).Top;
            foreach (var blk in _rtb.Document.Blocks)
                if (blk.Tag is int i && i == index)
                    return blk.ContentStart.GetCharacterRect(LogicalDirection.Forward).Top;
        }
        catch { }
        return null;
    }

    /// <summary>Keeps the focal block visually fixed across a document rebuild (which otherwise jumps to the
    /// top): restores the scroll offset, then scrolls block <paramref name="focalIndex"/> back to
    /// <paramref name="targetY"/> — so content added/removed elsewhere doesn't bounce the reader, and an
    /// entered block lands at the mouse. Optionally keeps the caret on screen while typing.</summary>
    private void AnchorScroll(double offset, int focalIndex, double? targetY, bool keepCaretVisible)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _rtb.ScrollToVerticalOffset(offset);
            try
            {
                if (targetY is double y && BlockTopY(focalIndex) is double now)
                    _rtb.ScrollToVerticalOffset(_rtb.VerticalOffset + (now - y));

                if (keepCaretVisible && _active >= 0)
                {
                    var rect = _rtb.CaretPosition.GetCharacterRect(LogicalDirection.Forward);
                    if (rect.Bottom > _rtb.ViewportHeight)
                        _rtb.ScrollToVerticalOffset(_rtb.VerticalOffset + (rect.Bottom - _rtb.ViewportHeight) + 6);
                    else if (rect.Top < 0)
                        _rtb.ScrollToVerticalOffset(_rtb.VerticalOffset + rect.Top - 6);
                }
            }
            catch { /* rect not available yet — leave the restored offset */ }
        }, DispatcherPriority.Loaded);
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
