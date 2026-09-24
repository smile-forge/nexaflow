using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Writing on somebody's behalf: a block the host puts in, something dropped, and the symbol palette's keys.
///
/// <para>
/// <strong>"The formula under the caret" is a question about the tree.</strong> Whether the caret is in a formula is
/// whether a part written as maths holds it — asked of what was laid, which names every part it drew. So a palette key
/// lands in the formula the reader is writing, and says so where there is none rather than typing <c>\frac{}{}</c> into a
/// sentence.
/// </para>
/// </summary>
public sealed partial class MarkdownSurface
{
    /// <summary>Puts the caret at the end of what is written and takes the keyboard — for a note just made, to be written in.</summary>
    public void BeginEdit()
    {
        Focus();
        Keyboard.Focus(this);

        var (start, length) = Inner;
        _shown.TakeCaret(start + length);
    }

    // ── Blocks from the host ────────────────────────────────────────────────

    /// <summary>
    /// Puts <paramref name="markdown"/> in as a block of its own after the one the caret is in — a picture or a link the
    /// host made of something pasted. In one block of a language it is more of that language instead.
    /// </summary>
    public void InsertMarkdownAtCaret(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown) || IsReadOnly) return;

        if (!string.IsNullOrEmpty(SingleBlock))
        {
            if (InsertLatexAtCaret(markdown)) return;

            var (start, length) = Inner;
            Write(_shown.Current.MoveCaretTo(start + length).Insert(markdown));

            return;
        }

        Write(After(Blocked(_shown.Caret)?.End ?? _shown.Markdown.Length, markdown));
    }

    /// <summary>
    /// Puts <paramref name="markdown"/> in as a block of its own after the block at <paramref name="pointInEditor"/>, or at
    /// the end where the point is past everything written — what dropping something on a note does.
    /// </summary>
    public void InsertMarkdownAt(string markdown, Point pointInEditor)
    {
        if (string.IsNullOrWhiteSpace(markdown) || IsReadOnly) return;

        var block = Offset(pointInEditor) is { } offset ? Blocked(offset) : null;

        Write(After(block?.End ?? _shown.Markdown.Length, markdown));
    }

    /// <summary>
    /// Swaps the first block written exactly as <paramref name="from"/> for <paramref name="to"/> — a link pasted in, for
    /// the preview fetched for it. Nothing, where that block has gone.
    /// </summary>
    public void ReplaceBlock(string from, string to)
    {
        var wanted = from.Trim();

        foreach (var block in Read.Children)
        {
            if (block.Derived || block.Role == Roles.Trivia) continue;

            var written = block.Print();
            if (!string.Equals(written.Trim(), wanted, StringComparison.Ordinal)) continue;

            var source = _shown.Markdown;
            var start = block.Start + (written.Length - written.TrimStart().Length);
            var length = written.Trim().Length;

            Write(new EditState(source[..start] + to + source[(start + length)..], start + to.Length));

            return;
        }
    }

    /// <summary>The document with <paramref name="markdown"/> set in as blocks of its own at <paramref name="at"/>, a blank line either side.</summary>
    private EditState After(int at, string markdown)
    {
        var source = _shown.Markdown;
        var before = source[..at].TrimEnd('\n', '\r');
        var after = source[at..].TrimStart('\n', '\r');
        var block = markdown.ReplaceLineEndings("\n").Trim('\n');

        var head = before.Length > 0 ? before + "\n\n" : string.Empty;
        var tail = after.Length > 0 ? "\n\n" + after : source.EndsWith('\n') ? "\n" : string.Empty;

        return new EditState(head + block + tail, head.Length + block.Length);
    }

    // ── Something dropped ───────────────────────────────────────────────────

    /// <inheritdoc/>
    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        if (e.Handled) return;

        e.Effects = IsReadOnly ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        if (e.Handled || IsReadOnly) return;

        DropContent(e.Data, e.GetPosition(this));
        e.Handled = true;
    }

    /// <summary>
    /// Writes in what was dropped at <paramref name="pointInEditor"/>: the host first, for a picture or a file, then as
    /// markdown — or, onto a formula, as the formula it is meant to be.
    /// </summary>
    public void DropContent(IDataObject data, Point pointInEditor)
    {
        if (IsReadOnly || ContentDropped?.Invoke(data, pointInEditor) == true) return;

        var maths = SingleBlock?.Trim().ToLowerInvariant() is "latex" or "math" or "tex";
        var text = maths ? MarkdownClipboard.AsFormula(MarkdownClipboard.ReadPlainText(data)) : MarkdownClipboard.ReadBestMarkdown(data);
        if (string.IsNullOrEmpty(text)) return;

        // A formula field has one place for anything to go, and a drop on it goes there as a paste would.
        if (maths)
        {
            if (Adopted()) _shown.Insert(text);

            return;
        }

        var at = Offset(pointInEditor) ?? _shown.Markdown.Length;
        var (start, length) = Inner;

        Write(_shown.Current.MoveCaretTo(Math.Clamp(at, start, start + length)).Insert(text.ReplaceLineEndings("\n")));
    }

    /// <summary>The offset of the source drawn at a point on this control, or null where nothing is drawn there.</summary>
    private int? Offset(Point pointInEditor)
    {
        var at = TranslatePoint(pointInEditor, _shown);
        var zoom = _shown.Zoom;
        var point = new Point(at.X / zoom, at.Y / zoom);

        if (point.Y > _shown.Laid.Size.Height) return null;

        return _shown.Laid.Root.OffsetAt(point);
    }

    // ── The formula the caret is in ─────────────────────────────────────────

    /// <summary>Whether the caret — or where what is picked out starts — is in a formula, which a palette key types into and a paste is cleaned up for.</summary>
    public bool InFormula()
    {
        var state = _shown.Current;
        return Formula(state.HasSelection ? state.SelectionStart : state.Caret) is not null;
    }

    /// <summary>
    /// Types LaTeX into the formula the caret is in — how a symbol palette inserts. Where the caret is in none, the one
    /// it is nearest is taken, so a key works without clicking into the formula first. False where there is no formula to
    /// type into, leaving the caller to put the text in however it otherwise would.
    /// </summary>
    /// <param name="caretBack">How far to walk the caret back afterwards, so a template such as <c>\frac{}{}</c> leaves it in the numerator.</param>
    public bool InsertLatexAtCaret(string latex, int caretBack = 0)
    {
        if (string.IsNullOrEmpty(latex) || IsReadOnly || !Adopted()) return false;

        _shown.Insert(latex, caretBack);

        return true;
    }

    /// <summary>Wraps what is chosen in the formula in a pair — a function taking what was picked as its argument — or puts the pair at its caret.</summary>
    public bool WrapLatexAtCaret(string before, string after)
    {
        if (IsReadOnly || !Adopted()) return false;

        _shown.Wrap(before, after);

        return true;
    }

    /// <summary>
    /// Pastes into the formula the caret is in, as the formula the text is meant to be — whatever said "this is maths"
    /// taken off — and settles whatever was half-written first. False where the caret is in no formula.
    /// </summary>
    public bool PasteIntoFormula(string? text)
    {
        if (string.IsNullOrEmpty(text) || IsReadOnly || !InFormula()) return false;

        _shown.Insert(MarkdownClipboard.AsFormula(text));

        return true;
    }

    /// <summary>
    /// Takes the keyboard and puts the caret in a formula, where it is in none — for focus arriving without a press, a
    /// host opening straight onto one. False where the document holds no formula.
    /// </summary>
    public bool FocusFormulaAtCaret()
    {
        Focus();
        Keyboard.Focus(this);

        return Adopted();
    }

    /// <summary>Makes sure the caret is in a formula, taking the first one's end where it is in none. False where there is none.</summary>
    private bool Adopted()
    {
        if (InFormula()) return true;

        if (Formulas().FirstOrDefault() is not { } first || first.Part(Roles.Body) is not { } body) return false;

        _shown.Restore(_shown.Current.MoveCaretTo(Ending(body)));
        if (!IsReadOnly) _shown.ShowCaret();

        return true;
    }

    /// <summary>The end of what is written in a block: its last character, not the line break that closes its last line.</summary>
    private static int Ending(ContentPart body)
    {
        var (start, length) = ContentNesting.Own(body);
        return start + length;
    }

    /// <summary>The formula whose own text holds <paramref name="offset"/>, or null.</summary>
    private ContentPart? Formula(int offset) =>
        Formulas().FirstOrDefault(part => part.Part(Roles.Body) is { } body
                                          && ContentNesting.Own(body) is var (start, length)
                                          && start <= offset && offset <= start + length);

    /// <summary>Every formula drawn, in the order written — asked of the layout, which names every part it drew.</summary>
    private System.Collections.Generic.IEnumerable<ContentPart> Formulas() =>
        _shown.Laid.Root.SelfAndDescendants()
            .Select(piece => piece.Part as ContentPart)
            .OfType<ContentPart>()
            .Where(part => part.Kind is MarkdownKinds.Math or MarkdownKinds.Formula)
            .Distinct()
            .OrderBy(part => part.Start);
}
