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
/// Rendered content is the part of the document that is not text — so the caret has to be able to cross
/// into it and back out, and typing has to reach it. None of it can take focus (a focusable element
/// inside a <c>RichTextBox</c> faults its caret reconciliation), so the editor keeps focus and forwards
/// the keys itself. Mouse input already arrives through <see cref="IInteractiveBlock"/>.
/// </para>
/// <para>
/// Everything here is written against <see cref="IEditableBlock"/> and knows nothing about what it is
/// driving. Where a key means something only one kind of content can offer — moving between the halves
/// of a fraction, tabbing through the holes of a half-written construct — the test is explicit and
/// narrow, and anything that fails it hands the key back to the document rather than swallowing it.
/// </para>
/// </summary>
public partial class InlineMarkdownEditor
{
    private IEditableBlock? _caretBlock;

    /// <summary>
    /// How long the focused block's run in <c>_blocks[index]</c> is right now.
    /// <para>
    /// Tracked here rather than asked of the block, because the block cannot know it: by the time it
    /// says its source changed, <see cref="IEditableBlock.Source"/> is already the new text and the
    /// length needed to splice it in is the one from before the keystroke.
    /// </para>
    /// </summary>
    private int _caretRun;

    /// <summary>
    /// The block the caret is inside, if any — what keys, pastes and palette insertions are going to.
    /// </summary>
    internal IEditableBlock? FocusedBlock => _caretBlock;

    /// <summary>
    /// Hands the caret to <paramref name="block"/>, taking it off whichever one had it. Called when a
    /// click lands inside a block that takes a caret, and when the caret arrows in from the text beside
    /// it.
    /// </summary>
    private void FocusBlock(IEditableBlock block)
    {
        if (ReferenceEquals(_caretBlock, block)) return;

        BlurBlock();
        _caretBlock = block;
        _caretRun   = block.Source.Length;
        block.SourceChanged += OnBlockSourceChanged;
        block.Exited        += OnBlockExited;

        // There is one caret, and the block is now drawing it. The RichTextBox keeps a caret of its own
        // at whatever text position the block occupies, and left visible it blinks beside the real one —
        // two carets, neither of which the keys are going to.
        _rtb.CaretBrush = Brushes.Transparent;

        // Same for the selection. The document selects an embedded element whole — it has no way to say
        // "part of that" — so the block wash sits under the block's own, and the reader sees the piece
        // they picked highlighted inside a highlighted line.
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

        // The document draws the caret again — through the same call that decided how it looks, so it
        // comes back theme-aware or palette-frozen exactly as it was.
        ApplyEditorBrushes();
    }

    /// <summary>
    /// The caret-taking element rendered for one block of the model, whatever kind it is — the seam an
    /// arrow key crosses into.
    /// </summary>
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

    /// <summary>
    /// Steps the caret out of the text and into the block on the other side of it, when an arrow key
    /// would otherwise skip straight over that block.
    /// <para>
    /// An embedded element is a single indivisible position to a flow document, so left-arrowing back
    /// along a line hops the whole thing as if it were one character. Crossing into it deliberately —
    /// and at the place the reader was coming from — is what makes the boundary invisible: right into
    /// its start, left into its end, and down into whatever sits under the column the caret was already
    /// in.
    /// </para>
    /// Returns true when the caret was handed over and the document's own handling should stand down.
    /// </summary>
    private bool ArrowCrossesIntoBlock(KeyEventArgs e)
    {
        if (_caretBlock is not null) return false;   // already inside something; its keys, not ours
        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down)) return false;

        // Shift is extending a selection across the block and Ctrl is a word/document jump; neither is
        // "step into this thing", and a selection sweeping over a block is handled whole (see SweepBlocks).
        if ((Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) != 0) return false;

        var forward = e.Key is Key.Right or Key.Down;
        var vertical = e.Key is Key.Up or Key.Down;

        var (block, offset) = CaretLocation();
        if (block < 0 || !AtBlockEdge(block, offset, forward, vertical)) return false;

        if (EditableInBlock(block + (forward ? 1 : -1)) is not { } target) return false;

        // Coming in forward means entering over the block's leading edge, and vice versa.
        var edge = forward ? BlockExit.Before : BlockExit.After;
        var step = vertical ? CaretStep.Line : CaretStep.Character;
        var column = vertical && target is UIElement element ? CaretColumnIn(element) : null;

        FocusBlock(target);
        _rtb.Focus();
        target.TakeCaretArriving(new CaretArrival(edge, step, column));
        return true;
    }

    /// <summary>
    /// Whether the caret is against the edge the key is pushing at — the far end of the block for a
    /// sideways move, its first or last line for a vertical one, since those are the only positions
    /// from which the next step leaves the block at all.
    /// </summary>
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

    /// <summary>
    /// Where the document's caret sits horizontally, in <paramref name="target"/>'s own coordinates —
    /// the column a vertical step has to keep.
    /// </summary>
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

    /// <summary>
    /// The editable block <paramref name="element"/> is, or the one it holds.
    /// <para>
    /// Not always the container's own child. A block rendered from a fence is wrapped for layout before
    /// it is embedded — fitted to the column in a <c>Viewbox</c>, given a scroller, given a border — and
    /// looking only one level down found a formula (which is embedded bare) while missing everything
    /// else. The walk is the logical tree rather than the visual one, so it answers before the document
    /// has been laid out.
    /// </para>
    /// </summary>
    private static IEditableBlock? EditableWithin(DependencyObject element)
    {
        if (element is IEditableBlock editable) return editable;

        foreach (var child in LogicalTreeHelper.GetChildren(element))
            if (child is DependencyObject node && EditableWithin(node) is { } found) return found;

        return null;
    }

    /// <summary>
    /// Routes a keystroke to the block holding the caret. Returns true when it dealt with the key and
    /// the editor's own text handling should stand down.
    /// <para>
    /// Most of this is the same whatever is being typed into. What differs is what the content has to
    /// offer: a formula has lines to move between, half-written commands to settle and holes to tab
    /// through; content that is one run of characters on one line has none of those, so those keys fall
    /// back to the document rather than being swallowed by a block with no use for them.
    /// </para>
    /// </summary>
    private bool BlockHandlesKey(KeyEventArgs e)
    {
        if (_caretBlock is not { } block) return false;

        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        // Paste keeps the caret where it is. The text itself arrives later, through DataObject.Pasting,
        // and goes into the block — so giving the caret back here would hand it to the document a beat
        // before the paste landed, and what was pasted would appear beside the block being edited rather
        // than in it. Every other shortcut is still the editor's.
        if (ctrl && e.Key is Key.V) return false;
        if (shift && e.Key is Key.Insert) return false;
        if (ctrl) { BlurBlock(); return false; }

        switch (e.Key)
        {
            case Key.Left:
            case Key.Right:
                // Running off an end raises Exited, which puts the caret in the text beside the block.
                block.MoveCaret(forward: e.Key == Key.Right, extend: shift);
                return true;

            case Key.Up:
            case Key.Down:
                // Inside a fraction or a script there is somewhere to go; otherwise let the editor move
                // the caret to another line, which means leaving the block.
                if (block is FormulaElement vertical
                    && vertical.MoveCaretVertically(up: e.Key == Key.Up, extend: shift)) return true;
                BlurBlock();
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
                // Settles a half-written command in a formula. Anywhere else a space is simply a
                // character, and it arrives through text input like any other.
                if (block is FormulaElement spacing) { spacing.Commit(" "); return true; }
                return false;

            case Key.Enter:
                if (block is FormulaElement entering) { entering.Commit(" "); return true; }
                // Never a block split: the caret is inside one piece of content, not between two
                // paragraphs — and content that is one line has no second line to start, so this does
                // nothing at all rather than tearing the document in half behind it.
                return true;

            case Key.Tab:
                // Through the holes of whatever was just inserted, so a construct is filled by typing
                // and tabbing. Only while there are holes left; otherwise Tab is the document's.
                return block is FormulaElement holed && holed.SelectNextPlaceholder(forward: !shift);

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

        foreach (var character in text)
        {
            // A newline settles a formula's half-written command, and means nothing to a one-line value.
            if (character is '\r' or '\n')
            {
                if (block is FormulaElement formula) formula.Commit();
                continue;
            }

            block.Type(character);
        }
        return true;
    }

    /// <summary>
    /// Puts a block's edit back into the markdown it came from, without re-rendering: rebuilding the
    /// document on every keystroke would destroy the very element being typed into.
    /// </summary>
    private void OnBlockSourceChanged(object? sender, EventArgs e)
    {
        if (sender is not IEditableBlock block) return;
        if (sender is not DependencyObject element) return;

        var index = BlockIndexOf(element);
        if (index < 0 || index >= _blocks.Count) return;

        if (block.SourceStart < 0)
        {
            // The whole markdown block is this content, which today only a $$…$$ formula can be — and
            // the delimiters have to go back on. Bare when the editor owns the fence: it puts one back
            // to typeset, and a fence stored here as well would be re-fenced on every keystroke until
            // the block was nothing but $$.
            _blocks[index] = SingleFormula ? block.Source : $"$$\n{block.Source}\n$$";
        }
        else
        {
            // Everything else occupies a run inside its block — a formula among prose, a barcode's value
            // inside its fence — and the edit goes back exactly where that run was.
            var source = _blocks[index];
            var start  = Math.Clamp(block.SourceStart, 0, source.Length);
            var length = Math.Clamp(_caretRun, 0, source.Length - start);

            _blocks[index] = string.Concat(source.AsSpan(0, start), block.Source, source.AsSpan(start + length));
        }

        _caretRun = block.Source.Length;   // the run just changed size
        PushMarkdown();
    }

    /// <summary>The caret walked off one end of a block — put it in the text on that side.</summary>
    private void OnBlockExited(object? sender, BlockExit side)
    {
        if (sender is not IEditableBlock block) return;

        // When one formula is the whole editor there is nowhere to step out to. Handing the caret to
        // the document anyway let the RichTextBox take it somewhere of its own choosing — the start of
        // the line, then off the right-hand edge, then down — for a key that should have done nothing.
        if (SingleFormula && block is FormulaElement)
        {
            block.TakeCaretArriving(new CaretArrival(side, CaretStep.Character, null));
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

        // An inline element sits in an InlineUIContainer whose logical parent chain reaches the
        // paragraph; a block one sits in a BlockUIContainer. Either way the pointer route is a backstop.
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
