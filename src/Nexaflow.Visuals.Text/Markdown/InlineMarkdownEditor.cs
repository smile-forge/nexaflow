using System.Windows;
using System.Windows.Controls;
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

    public InlineMarkdownEditor()
    {
        _rtb = new RichTextBox
        {
            AcceptsTab                    = false,   // Tab is intercepted to insert a tab
            AllowDrop                     = false,   // drops are handled at the note/canvas level
            IsUndoEnabled                 = false,   // we rebuild the document; WPF undo would fight it
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
        _rtb.SelectionChanged  += OnSelectionChanged;
        _rtb.GotKeyboardFocus  += (_, _) => ScheduleNavigate();
        _rtb.LostKeyboardFocus += OnLostFocus;
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

    private MarkdownRenderContext Context => new() { Palette = Pal, OnNavigate = LinkNavigate };

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

    // ── Edits (applied to the block model) ────────────────────────────────

    /// <summary>Replaces text in the active block between two in-block offsets, then rebuilds.</summary>
    private void EditActive(int from, int to, string insert)
    {
        insert = insert.Replace("\r\n", "\n").Replace("\r", "\n");
        var block = _blocks[_active];
        from = Math.Clamp(from, 0, block.Length);
        to   = Math.Clamp(to,   from, block.Length);
        _blocks[_active] = block[..from] + insert + block[to..];
        Activate(_active, from + insert.Length);
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
            case Key.Enter:
            {
                // Split the block at the caret → a new block (the one you leave renders).
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
        int join = _blocks[_active - 1].Length;
        _blocks[_active - 1] += _blocks[_active];
        _blocks.RemoveAt(_active);
        PushMarkdown();
        Activate(_active - 1, join);
    }

    private void MergeWithNext()
    {
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
        var pasted = MarkdownClipboard.ReadBestMarkdown(e.DataObject);
        e.CancelCommand();
        if (string.IsNullOrEmpty(pasted) || _active < 0) return;

        // DataObject.Pasting fires inside the RichTextBox's change block, where setting
        // Document throws — apply the edit after the paste operation unwinds.
        var (from, to) = SelectionInActiveBlock();
        Dispatcher.BeginInvoke(() => InsertPaste(pasted, from, to), DispatcherPriority.Background);
    }

    private void InsertPaste(string pasted, int from, int to)
    {
        if (_active < 0) return;

        // A multi-block paste becomes multiple blocks; a single-block paste edits in place.
        var parts = MarkdownBlocks.Split(pasted);
        if (parts.Count == 1) { EditActive(from, to, parts[0]); return; }

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
        if (_suppress) return;
        _blocks = MarkdownBlocks.Compact(_blocks);   // drop the transient empty block(s)
        _active = -1;
        RenderAll();
        PushMarkdown();
    }

    /// <summary>Defers activating the block under the caret until after the current
    /// input/navigation completes (never rebuild inside a WPF change block).</summary>
    private void ScheduleNavigate()
    {
        if (_navQueued) return;
        _navQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _navQueued = false;
            if (_suppress || !_rtb.IsKeyboardFocusWithin || !_rtb.Selection.IsEmpty) return;
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
            return (b, 0);
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
