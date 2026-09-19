using System;
using System.Windows.Documents;
using System.Windows.Input;
using Nexaflow.Visuals.Text.Markdown.Latex;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// What LaTeX specifically asks of the editor, over the block seam every editable block shares (focus, keys,
/// caret crossing, write-back — all in <c>InlineMarkdownEditor.Blocks.cs</c>): the palette's insertions, and a
/// paste normalised into a formula rather than a paragraph.
/// </summary>
public partial class InlineMarkdownEditor
{
    /// <summary>The formula the caret is inside, if any — the target for keys and palette insertions.</summary>
    internal Editing.ContentElement? FocusedContent => _caretBlock as Editing.ContentElement;

    /// <summary>Types LaTeX into the formula holding the caret — how a symbol palette inserts. When no
    /// formula holds it, the one under the caret is adopted first, so a palette key works without clicking
    /// into the formula. Returns false when there's no formula to type into, leaving the caller to insert
    /// the text however it otherwise would.</summary>
    /// <param name="latex">The LaTeX to type.</param>
    /// <param name="caretBack">How far to walk the caret back afterwards, so a template such as
    /// <c>\frac{}{}</c> leaves it in the numerator instead of past the whole thing.</param>
    public bool InsertLatexAtCaret(string latex, int caretBack = 0)
    {
        if (string.IsNullOrEmpty(latex)) return false;
        if (!AdoptFormulaAtCaret()) return false;

        FocusedContent!.Insert(latex, caretBack);
        return true;
    }

    /// <summary>Wraps the focused formula's selection in a pair, or inserts it at its caret — a function
    /// taking what you picked as its argument. False when there is no formula to act on.</summary>
    public bool WrapLatexAtCaret(string before, string after)
    {
        if (!AdoptFormulaAtCaret()) return false;

        FocusedContent!.Wrap(before, after);
        return true;
    }

    /// <summary>Pastes into the formula holding the caret, settling whatever was half-written first, so the
    /// pasted text isn't read as a continuation of the command being typed. False when no formula holds the
    /// caret, leaving the paste to the document.</summary>
    public bool PasteIntoFormula(string? text)
    {
        if (string.IsNullOrEmpty(text) || FocusedContent is not { } formula) return false;

        formula.Insert(AsFormula(text));
        return true;
    }

    /// <summary>Pasted text as the formula it is meant to be: whatever said "this is maths" taken off,
    /// however many lines folded into one expression, and the ends trimmed. Its own method because every
    /// paste route needs it. The newline is trimmed because a copy almost always carries one and inside an
    /// expression a newline means a space — left on, it's invisible and the first backspace seems to do nothing.
    /// </summary>
    public static string AsFormula(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : Undelimited(text).ReplaceLineEndings(" ").Trim();

    /// <summary>Environments that only say "what follows is maths" — the wrapper, never the formula.
    /// Deliberately a fixed list, not any <c>\begin{…}</c>: <c>matrix</c>, <c>cases</c> and <c>array</c> are
    /// also environments but ARE the formula, and stripping those would take a matrix apart.</summary>
    private static readonly string[] MathEnvironments =
        ["equation", "displaymath", "math", "align", "alignat", "gather", "multline", "eqnarray"];

    /// <summary>Takes the typesetting instructions off a pasted formula, leaving the formula. LaTeX copied
    /// from anywhere arrives wrapped in whatever said "this is maths" (<c>$$…$$</c>, <c>\[…\]</c>,
    /// <c>\begin{equation}…\end{equation}</c>); pasting into a formula, that has already been said, so
    /// keeping it hands the parser unknown commands and a red wave under the reader's own formula. Stripped
    /// repeatedly since wrappers nest; only a pair around the whole text counts, not one in the middle.
    /// </summary>
    private static string Undelimited(string text)
    {
        var trimmed = text.Trim();

        // Bounded rather than while(true) — a grammar this loose isn't worth trusting with an unbounded loop over user input.
        for (var pass = 0; pass < 8; pass++)
        {
            var stripped = StripOnce(trimmed);
            if (stripped == trimmed) return trimmed.Length == 0 ? text : trimmed;
            trimmed = stripped.Trim();
        }

        return trimmed;
    }

    /// <summary>A markdown code fence around the whole of it, taken off with any info string — the same
    /// kind of wrapper as <c>$$</c>. This is how LaTeX arrives from a browser: copying a formula shown as
    /// code carries an HTML <c>&lt;pre&gt;</c>, converted to a fenced block; left on, the backticks paste in
    /// as opening quotes in TeX. Only a fence opening the first line and closing the last counts — backticks
    /// elsewhere were typed.</summary>
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

    /// <summary>Hands the caret to the formula under it, so keys reach it when focus arrived without a
    /// click — tabbing in, or a host opening straight onto a formula. False when the document holds no
    /// formula.</summary>
    public bool FocusFormulaAtCaret()
    {
        // Without the keyboard the formula draws a caret no keystroke reaches, worse than no caret at all.
        if (!_rtb.IsKeyboardFocusWithin) { _rtb.Focus(); Keyboard.Focus(_rtb); }
        return AdoptFormulaAtCaret();
    }

    /// <summary>Makes sure some formula holds the caret, adopting the one under it if none does. False when
    /// the document has no formula to adopt.</summary>
    private bool AdoptFormulaAtCaret()
    {
        if (FocusedContent is not null) return true;

        var index = _rtb.CaretPosition is { } caret ? BlockIndexAtPointer(caret) : -1;
        var found = (index >= 0 ? ContentInBlock(index) : null) ?? FirstContent();
        if (found is null) return false;

        FocusBlock(found);
        found.TakeCaret(found.Source.Length);
        return true;
    }

    /// <summary>The formula rendered for one block of the model, if it holds one.</summary>
    private Editing.ContentElement? ContentInBlock(int index) => EditableInBlock(index) as Editing.ContentElement;

    /// <summary>The first formula anywhere in the document — the fallback when the caret names none.</summary>
    private Editing.ContentElement? FirstContent()
    {
        foreach (var block in _rtb.Document.Blocks)
            if (ContentIn(block) is { } found) return found;
        return null;
    }

    private static Editing.ContentElement? ContentIn(Block block) => EditableIn(block) as Editing.ContentElement;


}
