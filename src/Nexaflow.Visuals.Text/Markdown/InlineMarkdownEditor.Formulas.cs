using System;
using System.Windows.Documents;
using System.Windows.Input;
using Nexaflow.Visuals.Text.Markdown.Latex;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// What LaTeX specifically asks of the editor, over and above the block seam every editable block
/// shares: the palette's insertions, and a paste normalised into a formula rather than a paragraph.
/// <para>
/// Focus, keys, caret crossing and the write-back into the block model are not here — they are the same
/// for every embedded block and live in <c>InlineMarkdownEditor.Blocks.cs</c>. What remains is the part
/// that only makes sense for maths: adopting the formula under the caret so a palette key works without
/// clicking into it first, and taking off whatever wrapper a copied formula arrived in.
/// </para>
/// </summary>
public partial class InlineMarkdownEditor
{
    /// <summary>The formula the caret is inside, if any — the target for keys and palette insertions.</summary>
    internal FormulaElement? FocusedFormula => _caretBlock as FormulaElement;

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

        FocusedFormula!.Insert(latex, caretBack);
        return true;
    }

    /// <summary>
    /// Wraps the focused formula's selection in a pair, or inserts the pair at its caret — a function
    /// taking what you picked as its argument. False when there is no formula to act on.
    /// </summary>
    public bool WrapLatexAtCaret(string before, string after)
    {
        if (!AdoptFormulaAtCaret()) return false;

        FocusedFormula!.Wrap(before, after);
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
        if (string.IsNullOrEmpty(text) || FocusedFormula is not { } formula) return false;

        formula.Insert(AsFormula(text));
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
        if (FocusedFormula is not null) return true;

        var index = _rtb.CaretPosition is { } caret ? BlockIndexAtPointer(caret) : -1;
        var found = (index >= 0 ? FormulaInBlock(index) : null) ?? FirstFormula();
        if (found is null) return false;

        FocusBlock(found);
        found.TakeCaret(found.Latex.Length);
        return true;
    }

    /// <summary>The formula rendered for one block of the model, if it holds one.</summary>
    private FormulaElement? FormulaInBlock(int index) => EditableInBlock(index) as FormulaElement;

    /// <summary>The first formula anywhere in the document — the fallback when the caret names none.</summary>
    private FormulaElement? FirstFormula()
    {
        foreach (var block in _rtb.Document.Blocks)
            if (FormulaIn(block) is { } found) return found;
        return null;
    }

    private static FormulaElement? FormulaIn(Block block) => EditableIn(block) as FormulaElement;
}
