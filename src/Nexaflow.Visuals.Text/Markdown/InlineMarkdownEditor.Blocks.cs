using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Nexaflow.Visuals.Text.Markdown.Latex;

using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// The seam between the document and the rendered content embedded in it: which block holds the caret,
/// where its keys come from, and how an edit to it gets back into the block model.
/// <para>
/// Rendered content can't take focus (a focusable element inside a <c>RichTextBox</c> faults its caret
/// reconciliation), so the editor keeps focus and forwards keys itself. Everything here is written against
/// <see cref="IEditableBlock"/> and knows nothing about what it drives; a key only one kind of content can
/// use (moving between the halves of a fraction, tabbing through a construct's holes) is tested explicitly
/// and handed back to the document when it doesn't apply.
/// </para>
/// </summary>
public partial class InlineMarkdownEditor
{
    private IEditableBlock? _caretBlock;

    /// <summary>How long the focused block's run in <c>_blocks[index]</c> is right now. Tracked here, not
    /// asked of the block: by the time it says its source changed, <see cref="IEditableBlock.Source"/> is
    /// already the new text, and the splice needs the length from before the keystroke.</summary>
    private int _caretRun;

    /// <summary>
    /// Where the caret stood in the focused block before the key now being handled — where an undo of what the key does
    /// puts it back.
    /// </summary>
    private int _caretBefore;

    /// <summary>
    /// The block the caret is inside, if any — what keys, pastes and palette insertions are going to.
    /// </summary>
    internal IEditableBlock? FocusedBlock => _caretBlock;

    /// <summary>Hands the caret to <paramref name="block"/>, taking it off whichever one had it — on a click
    /// inside a caret-taking block, and when the caret arrows in from the text beside it.</summary>
    private void FocusBlock(IEditableBlock block)
    {
        if (ReferenceEquals(_caretBlock, block)) return;

        BlurBlock();
        _caretBlock = block;
        _caretRun   = block.Source.Length;
        block.SourceChanged += OnBlockSourceChanged;
        block.Exited        += OnBlockExited;

        // There is one caret; the block draws it now. Left visible, the RTB's own caret (at the block's
        // text position) would blink beside it — two carets, neither reached by the keys.
        _rtb.CaretBrush = Brushes.Transparent;

        // Same for selection: the document can only select an embedded element whole, so its wash would
        // sit under the block's own and double-highlight the piece the reader picked.
        ClearDocumentSelection();
    }

    /// <summary>Collapses the document's selection, leaving whatever the block itself has selected.</summary>
    private void ClearDocumentSelection()
    {
        if (_rtb.Selection.IsEmpty) return;

        _suppress = true;
        try { _rtb.Selection.Select(_rtb.Selection.Start, _rtb.Selection.Start); }
        finally { _suppress = false; }
    }

    /// <summary>Takes the caret back out of whichever block holds it.</summary>
    private void BlurBlock()
    {
        if (_caretBlock is not { } block) return;
        _caretBlock = null;

        block.SourceChanged -= OnBlockSourceChanged;
        block.Exited        -= OnBlockExited;
        block.ReleaseCaret();

        // Reuses the call that decided how the caret looks, so it comes back theme-aware or palette-frozen as before.
        ApplyEditorBrushes();
    }

    /// <summary>What is selected inside the focused block, or null when nothing is. The document's own
    /// selection is empty while a block holds one (a flow document selects an embedded element only whole),
    /// so cut/copy have to ask the block or they'd act on the whole note instead.</summary>
    private string? SelectionInBlock()
    {
        if (_caretBlock is not { } block || block.Selection.Count == 0) return null;

        var source = block.Source;
        var (start, length) = block.Selection[0];

        start  = Math.Clamp(start, 0, source.Length);
        length = Math.Clamp(length, 0, source.Length - start);

        return length == 0 ? null : source.Substring(start, length);
    }

    /// <summary>Deletes what is selected inside the focused block. False when there was no selection.</summary>
    private bool DeleteSelectionInBlock() =>
        SelectionInBlock() is not null && _caretBlock!.Backspace();

    /// <summary>
    /// Makes sure the block the caret is over holds it, whatever kind it is — a formula, a tune, anything
    /// that takes a caret of its own. Adopting through the <see cref="IEditableBlock"/> seam rather than a
    /// formula-only path is what makes a tune (or anything else) editable at all, not just maths.
    /// </summary>
    public bool FocusBlockAtCaret()
    {
        // Without the keyboard the block draws a caret no keystroke reaches, worse than no caret at all.
        if (!_rtb.IsKeyboardFocusWithin) { _rtb.Focus(); Keyboard.Focus(_rtb); }
        if (_caretBlock is not null) return true;

        var index = _rtb.CaretPosition is { } caret ? BlockIndexAtPointer(caret) : -1;
        var found = (index >= 0 ? EditableInBlock(index) : null) ?? FirstEditable();
        if (found is null) return false;

        FocusBlock(found);

        // Told the caret arrived from the end, as the document does arrowing in from adjacent text — every
        // kind of block answers this the same way.
        found.TakeCaretArriving(new CaretArrival(BlockExit.After, CaretStep.Character, null));

        return true;
    }

    /// <summary>The first block anywhere in the document that takes a caret.</summary>
    private IEditableBlock? FirstEditable()
    {
        foreach (var block in _rtb.Document.Blocks)
            if (EditableIn(block) is { } found) return found;
        return null;
    }

    private IEditableBlock? EditableInBlock(int index)
    {
        if (index < 0) return null;

        foreach (var block in _rtb.Document.Blocks)
        {
            if (block.Tag is not int tagged || tagged != index) continue;
            if (EditableIn(block) is { } found) return found;
        }
        return null;
    }

    /// <summary>The editable content in block <paramref name="index"/> starting at <paramref name="start"/>,
    /// or the block's first where none does — a paragraph can hold several formulas, only one being written in.</summary>
    private IEditableBlock? ContentIn(int index, int start)
    {
        var held = new List<IEditableBlock>();

        foreach (var block in _rtb.Document.Blocks)
        {
            if (block.Tag is not int tagged || tagged != index) continue;

            if (block is BlockUIContainer { Child: { } child } && EditableWithin(child) is { } whole) held.Add(whole);

            if (block is Paragraph paragraph)
                foreach (var inline in paragraph.Inlines)
                    if (inline is InlineUIContainer { Child: { } inner } && EditableWithin(inner) is { } found) held.Add(found);
        }

        return held.FirstOrDefault(content => content.SourceStart == start) ?? held.FirstOrDefault();
    }

    /// <summary>
    /// Steps the caret into the block on the other side when an arrow key would otherwise skip straight
    /// over it — an embedded element is a single indivisible position to a flow document, so arrowing past
    /// it would hop the whole thing as if it were one character. Returns true when the caret was handed
    /// over and the document's own handling should stand down.
    /// </summary>
    private bool ArrowCrossesIntoBlock(KeyEventArgs e)
    {
        if (_caretBlock is not null) return false;   // already inside something; its keys, not ours
        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down)) return false;

        // Shift extends a selection and Ctrl is a word/document jump; neither is "step into this thing"
        // (a selection sweeping over a block is handled whole — see SweepBlocks).
        if ((Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) != 0) return false;

        var forward = e.Key is Key.Right or Key.Down;
        var vertical = e.Key is Key.Up or Key.Down;

        var (block, offset) = CaretLocation();
        if (block < 0 || !AtBlockEdge(block, offset, forward, vertical)) return false;

        if (EditableInBlock(block + (forward ? 1 : -1)) is not { } target) return false;

        // Forward enters over the block's leading edge, backward over its trailing edge.
        var edge = forward ? BlockExit.Before : BlockExit.After;
        var step = vertical ? CaretStep.Line : CaretStep.Character;
        var column = vertical && target is UIElement element ? CaretColumnIn(element) : null;

        FocusBlock(target);
        _rtb.Focus();
        target.TakeCaretArriving(new CaretArrival(edge, step, column));
        return true;
    }

    /// <summary>Whether the caret is against the edge the key is pushing at — the far end for a sideways
    /// move, the first/last line for a vertical one — the only positions from which the next step leaves
    /// the block.</summary>
    private bool AtBlockEdge(int block, int offset, bool forward, bool vertical)
    {
        if (block < 0 || block >= _blocks.Count) return false;
        var text = _blocks[block];

        if (!vertical) return forward ? offset >= text.Length : offset <= 0;

        if (forward)
        {
            var lastLine = text.LastIndexOf('\n');
            return offset > lastLine;
        }

        var firstLine = text.IndexOf('\n');
        return firstLine < 0 || offset <= firstLine;
    }

    /// <summary>Where the document's caret sits horizontally, in <paramref name="target"/>'s own coordinates
    /// — the column a vertical step has to keep.</summary>
    private double? CaretColumnIn(UIElement target)
    {
        try
        {
            var caret = _rtb.CaretPosition.GetCharacterRect(LogicalDirection.Forward);
            return _rtb.TranslatePoint(new Point(caret.X, 0), target).X;
        }
        catch { return null; }   // no rect yet — the edge is a good enough answer
    }

    private static IEditableBlock? EditableIn(Block block) =>
        block switch
        {
            BlockUIContainer { Child: { } child } => EditableWithin(child),
            Paragraph paragraph => FirstIn(paragraph),
            _ => null,
        };

    private static IEditableBlock? FirstIn(Paragraph paragraph)
    {
        foreach (var inline in paragraph.Inlines)
            if (inline is InlineUIContainer { Child: { } child } && EditableWithin(child) is { } found)
                return found;
        return null;
    }

    /// <summary>The editable block <paramref name="element"/> is, or the one it holds — not always the
    /// container's own child, since a fenced block is wrapped for layout (Viewbox, scroller, border) before
    /// being embedded. Walks the logical tree, not the visual one, so it answers before layout runs.</summary>
    private static IEditableBlock? EditableWithin(DependencyObject element)
    {
        if (element is IEditableBlock editable) return editable;

        foreach (var child in LogicalTreeHelper.GetChildren(element))
            if (child is DependencyObject node && EditableWithin(node) is { } found) return found;

        return null;
    }

    /// <summary>Routes a keystroke to the block holding the caret. Returns true when it dealt with the key
    /// and the editor's own text handling should stand down. Content with no use for a key (e.g. one-line
    /// content and a formula's line/hole navigation) falls back to the document instead of swallowing it.</summary>
    private bool BlockHandlesKey(KeyEventArgs e)
    {
        if (_caretBlock is not { } block) return false;
        _caretBefore = block.Caret;

        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        // Paste keeps the caret where it is: the text arrives later via DataObject.Pasting, and blurring
        // here would hand the caret to the document a beat before the paste landed.
        if (ctrl && e.Key is Key.V) return false;
        if (shift && e.Key is Key.Insert) return false;
        // Copy/cut act on what the block has selected — blurring first would throw the selection away.
        if (ctrl && e.Key is Key.C or Key.X) return false;
        if (ctrl) { BlurBlock(); return false; }

        // Whatever the block wants for itself, before any shared handling (e.g. a score claims Page Up/Down
        // for an octave) — asked of the seam, not the type, so the host never learns what a note is.
        if (block.HandleKey(e.Key, Keyboard.Modifiers)) return true;

        switch (e.Key)
        {
            case Key.Left:
            case Key.Right:
                block.MoveCaret(forward: e.Key == Key.Right, extend: shift);   // running off an end raises Exited
                return true;

            case Key.Up:
            case Key.Down:
                if (block.MoveCaretVertically(up: e.Key == Key.Up, extend: shift)) return true;
                BlurBlock();   // nowhere to go inside it — leaving the block
                return false;

            case Key.Back:
                if (block.Backspace()) return true;
                BlurBlock();      // nothing left in it — the next backspace is the document's
                return false;

            case Key.Delete:
                if (block.Delete()) return true;
                BlurBlock();
                return false;

            case Key.Space:
                // Settles a half-written formula command; typed here rather than left to the document,
                // which would insert the space into the next paragraph it could find.
                if (block.Commit(" ")) return true;
                block.Type(' ');
                return true;

            case Key.Home:
            case Key.End:
                // The ends of this content, not of the line it sits on.
                block.TakeCaretArriving(new CaretArrival(
                    e.Key == Key.Home ? BlockExit.Before : BlockExit.After, CaretStep.Character, null));
                return true;

            case Key.Enter:
                if (block.Commit("\n")) return true;
                // Never a block split — the caret is inside one piece of content, not between paragraphs.
                return true;

            case Key.Tab:
                return block.SelectNextPlaceholder(forward: !shift);   // through the construct's holes while any remain

            case Key.Escape:
                BlurBlock();
                return true;

            default:
                return false;   // printable keys arrive through OnPreviewTextInput
        }
    }

    /// <summary>Routes typed text to the block holding the caret. Returns true when it took it.</summary>
    private bool BlockHandlesText(string text)
    {
        if (_caretBlock is not { } block || string.IsNullOrEmpty(text)) return false;
        _caretBefore = block.Caret;

        foreach (var character in text)
        {
            if (character is '\r' or '\n')   // settles a formula's half-written command; nothing to a one-line value
            {
                block.Commit(" ");
                continue;
            }

            block.Type(character);
        }
        return true;
    }

    /// <summary>Puts a block's edit back into the markdown it came from, without re-rendering — a rebuild
    /// per keystroke would destroy the very element being typed into.</summary>
    private void OnBlockSourceChanged(object? sender, EventArgs e)
    {
        if (sender is not IEditableBlock block) return;
        if (sender is not DependencyObject element) return;

        var index = BlockIndexOf(element);
        if (index < 0 || index >= _blocks.Count) return;

        // Before the model changes, so Ctrl+Z has somewhere to go back to — the one kind of edit that used
        // to have no snapshot at all (unlike the source-mode and Word-style paths). Coalesced by block, so
        // a value typed in one go is one undo step.
        SnapshotAt(index, _caretBefore, content: block.SourceStart);

        if (block.SourceStart < 0)
        {
            // The whole markdown block is this content — today only a $$…$$ formula — so the delimiters go
            // back on. Bare when the editor owns the fence, which adds its own to typeset.
            _blocks[index] = IsSingleBlock ? block.Source : $"$$\n{block.Source}\n$$";
        }
        else
        {
            // Everything else occupies a run inside its block, and the edit goes back where that run was.
            // A block names its position inside what it was rendered from, which (when the editor owns the
            // fence) is longer at the front than what is stored — rebasing here keeps the two in step;
            // without it every edit to a fenced single block eats the first few characters of the content.
            var source = _blocks[index];
            var start  = Math.Clamp(block.SourceStart - (IsSingleBlock ? Fence.Open.Length : 0), 0, source.Length);
            var length = Math.Clamp(_caretRun, 0, source.Length - start);

            _blocks[index] = string.Concat(source.AsSpan(0, start), block.Source, source.AsSpan(start + length));
        }

        _caretRun = block.Source.Length;   // the run just changed size
        PushMarkdown();
    }

    /// <summary>Whether a single block is being handed its caret back — see <see cref="OnBlockExited"/>.</summary>
    private bool _handingBack;

    /// <summary>The caret walked off one end of a block — put it in the text on that side.</summary>
    private void OnBlockExited(object? sender, BlockExit side)
    {
        if (sender is not IEditableBlock block) return;

        // In single-block mode there's nowhere to step out to; handing the caret to the document let the
        // RichTextBox take it somewhere of its own choosing for a key that should have done nothing.
        if (IsSingleBlock)
        {
            // Handed straight back, but only once — a block with nowhere to stand raises Exited again from
            // TakeCaretArriving, and answering that with another hand-back loops until stack overflow (an
            // empty formula did exactly this the moment it was focused).
            if (_handingBack) return;
            _handingBack = true;
            try { block.TakeCaretArriving(new CaretArrival(side, CaretStep.Character, null)); }
            finally { _handingBack = false; }
            return;
        }

        var container = sender is DependencyObject element ? ContainerOf(element) : null;
        BlurBlock();
        if (container is null) return;

        var landing = side == BlockExit.Before
            ? container.ContentStart.GetNextInsertionPosition(LogicalDirection.Backward)
            : container.ContentEnd.GetNextInsertionPosition(LogicalDirection.Forward);

        if (landing is null) return;
        _suppress = true;
        try { _rtb.CaretPosition = landing; }
        finally { _suppress = false; }
    }

    /// <summary>Which block of the model an embedded element belongs to, via the Tag every rendered block carries.</summary>
    private int BlockIndexOf(DependencyObject element)
    {
        for (DependencyObject? d = element; d is not null; d = LogicalTreeHelper.GetParent(d))
            if (d is TextElement { Tag: int index }) return index;

        // Backstop via the pointer route when the logical-parent walk doesn't find a tagged element.
        var container = ContainerOf(element);
        return container is null ? -1 : BlockIndexAtPointer(container.ContentStart);
    }

    /// <summary>The text element hosting an embedded one — its <c>BlockUIContainer</c> or <c>InlineUIContainer</c>.</summary>
    private static TextElement? ContainerOf(DependencyObject element)
    {
        for (DependencyObject? d = element; d is not null; d = LogicalTreeHelper.GetParent(d))
            if (d is BlockUIContainer or InlineUIContainer) return (TextElement)d;
        return null;
    }
}
