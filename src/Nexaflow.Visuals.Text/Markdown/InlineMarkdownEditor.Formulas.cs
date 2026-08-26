using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Nexaflow.Visuals.Text.Markdown.Latex;

using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// The seam between the document and a formula inside it: which formula holds the caret, where its keys
/// come from, and how an edit to it gets back into the block model.
/// <para>
/// A formula is the one part of the rendered document that is not text — so the caret has to be able to
/// cross into it and back out, and typing has to reach it. It cannot take focus (a focusable element
/// inside a <c>RichTextBox</c> faults its caret reconciliation), so the editor keeps focus and forwards
/// the keys itself. Mouse input already arrives through <see cref="IInteractiveBlock"/>, which the
/// formula implements alongside the music score.
/// </para>
/// <para>
/// Kept apart from the main editor file because it is a self-contained conversation with one kind of
/// embedded element, not another chapter of how the editor edits text.
/// </para>
/// </summary>
public partial class InlineMarkdownEditor
{
    private FormulaElement? _caretFormula;

    /// <summary>The formula the caret is inside, if any — the target for keys and palette insertions.</summary>
    internal FormulaElement? FocusedFormula => _caretFormula;

    /// <summary>
    /// Hands the caret to <paramref name="formula"/>, taking it off whichever one had it. Called when a
    /// click lands inside a formula, and when the caret arrows in from the text beside it.
    /// </summary>
    private void FocusFormula(FormulaElement formula)
    {
        if (ReferenceEquals(_caretFormula, formula)) return;

        BlurFormula();
        _caretFormula = formula;
        formula.LatexChanged += OnFormulaLatexChanged;
        formula.Exited += OnFormulaExited;

        // There is one caret, and the formula is now drawing it. The RichTextBox keeps a caret of its
        // own at whatever text position the formula's block occupies, and left visible it blinks beside
        // the real one — two carets, neither of which the keys are going to.
        _rtb.CaretBrush = Brushes.Transparent;

        // Same for the selection. The document selects an embedded element whole — it has no way to
        // say "part of that formula" — so the block wash sits under the formula's own, and the reader
        // sees the piece they picked highlighted inside a highlighted line.
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

    /// <summary>Takes the caret back out of whichever formula holds it.</summary>
    private void BlurFormula()
    {
        if (_caretFormula is not { } formula) return;
        _caretFormula = null;

        formula.LatexChanged -= OnFormulaLatexChanged;
        formula.Exited -= OnFormulaExited;
        formula.ReleaseCaret();

        // The document draws the caret again — through the same call that decided how it looks, so it
        // comes back theme-aware or palette-frozen exactly as it was.
        ApplyEditorBrushes();
    }

    /// <summary>
    /// Types LaTeX into the formula holding the caret — how a symbol palette inserts. When no formula
    /// holds it, the one the caret is sitting in is adopted first, so pressing a palette key without
    /// clicking into the formula still does the obvious thing.
    /// <para>
    /// Returns false when there is no formula to type into at all, leaving the caller to insert the
    /// text however it otherwise would.
    /// </para>
    /// </summary>
    /// <param name="latex">The LaTeX to type.</param>
    /// <param name="caretBack">
    /// How far to walk the caret back afterwards, so a template such as <c>\frac{}{}</c> leaves it in
    /// the numerator instead of past the whole thing.
    /// </param>
    public bool InsertLatexAtCaret(string latex, int caretBack = 0)
    {
        if (string.IsNullOrEmpty(latex)) return false;
        if (!AdoptFormulaAtCaret()) return false;

        _caretFormula!.Insert(latex, caretBack);
        return true;
    }

    /// <summary>
    /// Wraps the focused formula's selection in a pair, or inserts the pair at its caret — a function
    /// taking what you picked as its argument. False when there is no formula to act on.
    /// </summary>
    public bool WrapLatexAtCaret(string before, string after)
    {
        if (!AdoptFormulaAtCaret()) return false;

        _caretFormula!.Wrap(before, after);
        return true;
    }

    /// <summary>
    /// Pastes into the formula holding the caret, settling whatever was half-written first — so pasted
    /// text arrives as text rather than being read as a continuation of the command being typed, and
    /// what lands typesets straight away. False when no formula holds the caret, leaving the paste to
    /// the document.
    /// </summary>
    public bool PasteIntoFormula(string? text)
    {
        if (string.IsNullOrEmpty(text) || _caretFormula is null) return false;

        _caretFormula.Insert(AsFormula(text));
        return true;
    }

    /// <summary>
    /// Pasted text as the formula it is meant to be: whatever said "this is maths" taken off, however
    /// many lines it was written over folded into one expression, and the ends trimmed.
    /// <para>
    /// Its own method because it has to happen on every route a paste can take. It used to be done only
    /// where a formula already held the caret, so a paste arriving a moment earlier — before anything
    /// had adopted one — went in through the other door with its <c>$</c> still attached.
    /// </para>
    /// <para>
    /// The newline is trimmed for a reason worth remembering: a copy almost always carries one, and
    /// inside one expression a newline means a space. Left on, it is a character the reader cannot see
    /// at the end of their formula, and their first backspace appears to do nothing at all.
    /// </para>
    /// </summary>
    public static string AsFormula(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : Undelimited(text).ReplaceLineEndings(" ").Trim();

    /// <summary>
    /// Environments that only say "what follows is maths" — the wrapper, never the formula.
    /// <para>
    /// Deliberately a list rather than any <c>\begin{…}</c>: <c>matrix</c>, <c>cases</c> and
    /// <c>array</c> are also environments and they <em>are</em> the formula. Stripping those would
    /// take a matrix apart.
    /// </para>
    /// </summary>
    private static readonly string[] MathEnvironments =
        ["equation", "displaymath", "math", "align", "alignat", "gather", "multline", "eqnarray"];

    /// <summary>
    /// Takes the typesetting instructions off a pasted formula, leaving the formula.
    /// <para>
    /// LaTeX copied from anywhere — a paper, a chat, another editor — arrives wrapped in whatever that
    /// place used to say "this is maths": <c>$$…$$</c>, <c>\[…\]</c>, <c>$…$</c>, <c>\(…\)</c>,
    /// <c>\begin{equation}…\end{equation}</c>. Pasting into a formula, that has already been said by
    /// the surface being pasted into, so keeping it hands the parser commands it has never heard of
    /// and the reader a red wave under their own formula.
    /// </para>
    /// <para>
    /// Repeatedly, because they nest — <c>\[\begin{aligned}…\end{aligned}\]</c> is one wrapper inside
    /// another. Only a pair around the <em>whole</em> of it is taken; a delimiter in the middle is
    /// part of what was copied. A starred form (<c>equation*</c>) is the same environment saying it
    /// wants no number, which is a typesetting instruction too.
    /// </para>
    /// </summary>
    private static string Undelimited(string text)
    {
        var trimmed = text.Trim();

        // Bounded rather than while(true): each pass must remove something, but a grammar this loose
        // is not worth trusting with an unbounded loop over user input.
        for (var pass = 0; pass < 8; pass++)
        {
            var stripped = StripOnce(trimmed);
            if (stripped == trimmed) return trimmed.Length == 0 ? text : trimmed;
            trimmed = stripped.Trim();
        }

        return trimmed;
    }

    /// <summary>
    /// A markdown code fence around the whole of it, taken off with any info string.
    /// <para>
    /// The same kind of wrapper as <c>$$</c> — it says what the text is, and is not part of it. This is
    /// how LaTeX arrives from a browser: copy a formula shown as code and the clipboard's HTML flavour
    /// carries a <c>&lt;pre&gt;</c>, which converts to a fenced block. Left on, the backticks are pasted
    /// into the formula, and a backtick is an opening quote in TeX — three of them at each end, which
    /// is precisely what the reader saw.
    /// </para>
    /// <para>
    /// Only a fence that opens the first line and closes the last: backticks anywhere else were typed.
    /// </para>
    /// </summary>
    private static string? Unfenced(string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        if (lines.Length < 2) return null;

        var open = lines[0].TrimEnd();
        var fence = new string('`', open.Length - open.TrimStart('`').Length);
        if (fence.Length < 3) return null;

        // The info string is a language name, never code: ```latex is still a fence.
        if (open[fence.Length..].Trim().Contains('`')) return null;

        var last = lines.Length - 1;
        while (last > 0 && lines[last].Trim().Length == 0) last--;
        if (last == 0 || lines[last].Trim() != fence) return null;

        return string.Join("\n", lines[1..last]);
    }

    private static string StripOnce(string text)
    {
        if (Unfenced(text) is { } unfenced) return unfenced;

        (string Open, string Close)[] pairs = [("$$", "$$"), (@"\[", @"\]"), (@"\(", @"\)"), ("$", "$")];

        foreach (var (open, close) in pairs)
        {
            if (text.Length < open.Length + close.Length) continue;
            if (!text.StartsWith(open, StringComparison.Ordinal)) continue;
            if (!text.EndsWith(close, StringComparison.Ordinal)) continue;

            return text[open.Length..^close.Length];
        }

        foreach (var environment in MathEnvironments)
        foreach (var name in new[] { environment, environment + "*" })
        {
            var open = @"\begin{" + name + "}";
            var close = @"\end{" + name + "}";

            if (!text.StartsWith(open, StringComparison.OrdinalIgnoreCase)) continue;
            if (!text.EndsWith(close, StringComparison.OrdinalIgnoreCase)) continue;

            return text[open.Length..^close.Length];
        }

        return text;
    }

    /// <summary>
    /// Hands the caret to the formula under it, so keys reach the formula when focus arrived without a
    /// click on it — tabbing in, or a host that opens straight onto a formula. False when the document
    /// holds no formula.
    /// </summary>
    public bool FocusFormulaAtCaret()
    {
        // Without the keyboard the formula would draw a caret no keystroke ever reached, which is a
        // worse lie than no caret at all.
        if (!_rtb.IsKeyboardFocusWithin) { _rtb.Focus(); Keyboard.Focus(_rtb); }
        return AdoptFormulaAtCaret();
    }

    /// <summary>
    /// Makes sure some formula holds the caret, adopting the one under it if none does. False when the
    /// document has no formula to adopt.
    /// </summary>
    private bool AdoptFormulaAtCaret()
    {
        if (_caretFormula is not null) return true;

        var index = _rtb.CaretPosition is { } caret ? BlockIndexAtPointer(caret) : -1;
        var found = (index >= 0 ? FormulaInBlock(index) : null) ?? FirstFormula();
        if (found is null) return false;

        FocusFormula(found);
        found.TakeCaret(found.Latex.Length);
        return true;
    }

    /// <summary>The formula rendered for one block of the model, if it holds one.</summary>
    private FormulaElement? FormulaInBlock(int index) => EditableInBlock(index) as FormulaElement;

    /// <summary>
    /// The caret-taking element rendered for one block of the model, whatever kind it is — the seam an
    /// arrow key crosses into. A formula today; a score or a diagram the moment either implements
    /// <see cref="IEditableBlock"/>, with nothing here to change.
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
    /// along a line hops the whole formula as if it were one character. Crossing into it deliberately —
    /// and at the place the reader was coming from — is what makes the boundary invisible: right into
    /// its start, left into its end, and down into whatever sits under the column the caret was already
    /// in.
    /// </para>
    /// Returns true when the caret was handed over and the document's own handling should stand down.
    /// </summary>
    private bool ArrowCrossesIntoBlock(KeyEventArgs e)
    {
        if (_caretFormula is not null) return false;   // already inside something; its keys, not ours
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

        // Only a formula's keys are routed for now — it is the only block that takes any. The crossing
        // above is already whole: a score or a diagram implementing IEditableBlock gets the caret here
        // without a line changing, and grows key handling when it has keys of its own to want.
        if (target is FormulaElement formula) FocusFormula(formula);
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

    /// <summary>The first formula anywhere in the document — the fallback when the caret names none.</summary>
    private FormulaElement? FirstFormula()
    {
        foreach (var block in _rtb.Document.Blocks)
            if (FormulaIn(block) is { } found) return found;
        return null;
    }

    private static FormulaElement? FormulaIn(Block block) => EditableIn(block) as FormulaElement;

    private static IEditableBlock? EditableIn(Block block) =>
        block switch
        {
            BlockUIContainer { Child: IEditableBlock child } => child,
            Paragraph paragraph => FirstIn(paragraph),
            _ => null,
        };

    private static IEditableBlock? FirstIn(Paragraph paragraph)
    {
        foreach (var inline in paragraph.Inlines)
            if (inline is InlineUIContainer { Child: IEditableBlock child }) return child;
        return null;
    }

    /// <summary>
    /// Routes a keystroke to the focused formula. Returns true when the formula dealt with it and the
    /// editor's own text handling should stand down.
    /// </summary>
    private bool FormulaHandlesKey(KeyEventArgs e)
    {
        if (_caretFormula is not { } formula) return false;

        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        // Paste keeps the caret where it is. The text itself arrives later, through DataObject.Pasting,
        // and goes into the formula — so giving the caret back here would hand it to the document a beat
        // before the paste landed, and a pasted formula would appear beside the one being edited rather
        // than in it. Every other shortcut is still the editor's.
        if (ctrl && e.Key is Key.V) return false;
        if (shift && e.Key is Key.Insert) return false;
        if (ctrl) { BlurFormula(); return false; }

        switch (e.Key)
        {
            case Key.Left:
            case Key.Right:
                // Running off an end raises Exited, which puts the caret in the text beside the formula.
                formula.MoveCaret(forward: e.Key == Key.Right, extend: shift);
                return true;

            case Key.Up:
            case Key.Down:
                // Inside a fraction or a script there is somewhere to go; otherwise let the editor move
                // the caret to another line, which means leaving the formula.
                if (formula.MoveCaretVertically(up: e.Key == Key.Up, extend: shift)) return true;
                BlurFormula();
                return false;

            case Key.Back:
                if (formula.Backspace()) return true;
                BlurFormula();      // nothing left in it — the next backspace is the document's
                return false;

            case Key.Delete:
                if (formula.Delete()) return true;
                BlurFormula();
                return false;

            case Key.Space:
            case Key.Enter:
                // Settles a half-written command. Enter inside a formula is never a block split — you
                // are inside one expression, not between two paragraphs.
                formula.Commit(e.Key == Key.Space ? " " : " ");
                return true;

            case Key.Tab:
                // Through the holes of whatever was just inserted, so a construct is filled by typing
                // and tabbing. Only while there are holes left; otherwise Tab is the document's.
                return formula.SelectNextPlaceholder(forward: !shift);

            case Key.Escape:
                BlurFormula();
                return true;

            default:
                return false;   // printable keys arrive through OnPreviewTextInput
        }
    }

    /// <summary>Routes typed text to the focused formula. Returns true when it took it.</summary>
    private bool FormulaHandlesText(string text)
    {
        if (_caretFormula is not { } formula || string.IsNullOrEmpty(text)) return false;

        foreach (var character in text)
        {
            if (character is '\r' or '\n') { formula.Commit(); continue; }
            formula.Type(character);
        }
        return true;
    }

    /// <summary>
    /// Puts a formula's edit back into the block it came from, without re-rendering: rebuilding the
    /// document on every keystroke would destroy the very element being typed into.
    /// </summary>
    private void OnFormulaLatexChanged(object? sender, EventArgs e)
    {
        if (sender is not FormulaElement formula) return;

        var index = BlockIndexOf(formula);
        if (index < 0 || index >= _blocks.Count) return;

        if (formula.IsWholeBlock)
        {
            // Bare when the editor owns the fence — it puts one back on to typeset, and a fence stored
            // here as well would be re-fenced on every keystroke until the block was nothing but $$.
            _blocks[index] = SingleFormula ? formula.Latex : $"$$\n{formula.Latex}\n$$";
        }
        else
        {
            var source = _blocks[index];
            var start = Math.Clamp(formula.SourceStart, 0, source.Length);
            var length = Math.Clamp(formula.SourceLength, 0, source.Length - start);
            _blocks[index] = string.Concat(source.AsSpan(0, start), formula.Latex, source.AsSpan(start + length));
            formula.SourceLength = formula.Latex.Length;   // the run just changed size
        }

        PushMarkdown();
    }

    /// <summary>The caret walked off one end of a formula — put it in the text on that side.</summary>
    private void OnFormulaExited(object? sender, BlockExit side)
    {
        if (sender is not FormulaElement formula) return;

        // When the formula is the whole editor there is nowhere to step out to. Handing the caret to
        // the document anyway let the RichTextBox take it somewhere of its own choosing — the start of
        // the line, then off the right-hand edge, then down — for a key that should have done nothing.
        if (SingleFormula) { formula.TakeCaretArriving(new CaretArrival(side, CaretStep.Character, null)); return; }

        var container = ContainerOf(formula);
        BlurFormula();
        if (container is null) return;

        var landing = side == BlockExit.Before
            ? container.ContentStart.GetNextInsertionPosition(LogicalDirection.Backward)
            : container.ContentEnd.GetNextInsertionPosition(LogicalDirection.Forward);

        if (landing is null) return;
        _suppress = true;
        try { _rtb.CaretPosition = landing; }
        finally { _suppress = false; }
    }

    /// <summary>Which block of the model a formula belongs to, via the Tag every rendered block carries.</summary>
    private int BlockIndexOf(FormulaElement formula)
    {
        for (DependencyObject? d = formula; d is not null; d = LogicalTreeHelper.GetParent(d))
            if (d is TextElement { Tag: int index }) return index;

        // An inline formula sits in an InlineUIContainer whose logical parent chain reaches the
        // paragraph; a block one sits in a BlockUIContainer. Either way the pointer route is a backstop.
        var container = ContainerOf(formula);
        return container is null ? -1 : BlockIndexAtPointer(container.ContentStart);
    }

    /// <summary>The text element hosting a formula — its <c>BlockUIContainer</c> or <c>InlineUIContainer</c>.</summary>
    private static TextElement? ContainerOf(FormulaElement formula)
    {
        for (DependencyObject? d = formula; d is not null; d = LogicalTreeHelper.GetParent(d))
            if (d is BlockUIContainer or InlineUIContainer) return (TextElement)d;
        return null;
    }
}
