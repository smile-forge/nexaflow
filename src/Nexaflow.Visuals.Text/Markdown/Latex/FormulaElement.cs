using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
// Ours, not WPF own: System.Windows has a ContentElement too, and it is a different idea entirely.
using ContentElement = Nexaflow.Visuals.Text.Editing.ContentElement;

namespace Nexaflow.Visuals.Text.Markdown.Latex;

/// <summary>
/// A formula, drawn where it was written and edited in place.
///
/// <para>
/// <strong>Almost nothing is here.</strong> Laying out, painting, the wash, the wave, the blinking caret,
/// the pointer, the selection grown out to whole constructs, arrow keys, backspace, the palette — all of
/// it is <see cref="ContentElement"/>, and is the same code a tune and a barcode run. What is left is the
/// handful of questions only a parse tree can answer, and each is one override.
/// </para>
/// <para>
/// The scale is the type size the formula is set at, which is a fact about the formula rather than about
/// the element: how large it is <em>drawn</em> is <see cref="ContentElement.Zoom"/>, and the two are
/// different things — one changes what the typesetter chooses, the other how big the result appears.
/// </para>
/// </summary>
public sealed class FormulaElement : ContentElement
{
    private readonly double _scale;
    private readonly bool _inline;

    /// <summary>
    /// The formula as TeX sees it, remade beside the layout every time the source changes. Its reading is
    /// worked out only if something asks a question about the parse, so a keystroke that merely redraws
    /// pays nothing for it.
    /// </summary>
    private LatexTree _tree;

    /// <summary>Raised when the reader's own editing changed the LaTeX.</summary>
    public event EventHandler? LatexChanged;

    public FormulaElement(string latex, MarkdownPalette palette, double scale, bool inline = false)
        : base(latex ?? string.Empty, palette)
    {
        _scale = scale;
        _inline = inline;

        // Set before the first lay-out, which the constructor does below; every later one replaces it.
        _tree = new LatexTree(Source, Laid.Nothing, LatexBuilder.Draws);

        SourceChanged += (_, _) => LatexChanged?.Invoke(this, EventArgs.Empty);

        Rebuild();
    }

    /// <summary>The LaTeX this is showing.</summary>
    public string Latex => Source;

    /// <summary>The map behind what is drawn — always there, because a builder always makes one.</summary>
    public LatexTree Layout => _tree;

    /// <summary>
    /// How far a selection wash reaches past the ink it marks, as a fraction of the type size. A glyph's
    /// box here is its advance and its own height, so washing it exactly leaves an <c>a</c> showing the
    /// wash through its counter and nowhere else, and an <c>i</c> as a stripe too narrow to notice.
    /// </summary>
    protected override double WashPad => _scale * 0.14;

    /// <summary>
    /// Typesets the whole formula, with the stretch being written set as the characters that were typed.
    ///
    /// <para>
    /// One layout over the real source, rather than a layout of the settled part with the raw characters
    /// painted over it afterwards. Painting over could only ever work while the stretch was the last thing
    /// in the formula: anywhere else it covered whatever followed, which is what un-rendering a fraction
    /// in the middle of an expression looked like. Set through the typesetter it takes up room like
    /// anything else, so the formula flows around it — and every offset the tree reports is an offset into
    /// the source the reader is editing, with no mapping in between.
    /// </para>
    /// </summary>
    protected override Laid Lay(EditState state, double room, double pixelsPerDip)
    {
        var laid = LatexBuilder.Build(
            state.Source, _scale, _inline, shownAsWritten: state.Raw, placeholders: !IsReadOnly,
            pixelsPerDip: pixelsPerDip);

        _tree = new LatexTree(state.Source, laid, LatexBuilder.Draws, state.Raw, !IsReadOnly);
        return laid;
    }

    /// <summary>
    /// Backspace behind a rendered command un-renders it rather than deleting a character of it — there
    /// is source to go back to, which the reader cannot see. A symbol has nothing hidden behind it: an α
    /// is one thing on the page however many letters spelled it, so it is simply taken.
    /// </summary>
    protected override EditState? Backspacing(EditState state)
    {
        if (state.HasSelection || state.Raw is not null) return null;

        if (_tree.SymbolBefore(state.Caret) is not { Exists: true } symbol) return null;
        if (symbol.Sits() is not { Length: > 1 } place) return null;

        var span = (Start: place.Start, Length: place.Length);

        return _tree.IsComposite(symbol) ? state.Backspace(span) : state.Remove(span.Start, span.Length);
    }

    /// <summary>
    /// A formula is one expression, read from its start — so only a step <em>along</em> the text can land
    /// anywhere but the beginning of it, and then only at the end, which is the character you stepped back
    /// onto. Up and down both land at the start, because a line step goes to where the line begins and the
    /// whole formula is that line. The column is ignored for the same reason: landing part-way along
    /// because that is where it fell would drop the reader into the middle of a subscript.
    /// </summary>
    public override void TakeCaretArriving(CaretArrival arrival)
    {
        if (arrival is not { Step: CaretStep.Character, Edge: BlockExit.After }) { TakeCaret(0); return; }

        base.TakeCaretArriving(arrival);
    }

    /// <summary>
    /// Ends a stretch being shown as written, as space or Enter does.
    ///
    /// <para>
    /// <strong>This override should not exist.</strong> Settling is typing the character, and the base
    /// does exactly that — except that LaTeX has to <em>add</em> a space after a control word, to say
    /// where the name stopped, since dropping it silently turns <c>\alpha x</c> into the unknown command
    /// <c>\alphax</c>. That is only true because the builder captures a pass made by an engine handed a
    /// finished parse, so there is nowhere to say "the command stops here" except in the characters. Fix
    /// the builder and this goes.
    /// </para>
    /// </summary>
    protected override void Settle(string separator) => Apply(State.Settle(separator), notify: true);

    /// <summary>Settles a half-written command — the host's Enter and space arrive here.</summary>
    public void Commit(string separator = " ") { if (!IsReadOnly) Settle(separator); }

    /// <summary>
    /// LaTeX's two rules about how it is written: what the structure makes of the text, and what the
    /// characters make of themselves.
    ///
    /// <para>
    /// <strong>Both of these are meant to go.</strong> Typing should be a splice at the caret and a
    /// re-render, with the spelling falling out of a backslash the builder cannot read and shows as source,
    /// and the structure falling out of a properly nested tree whose stops only offer positions text may be
    /// written into. Removing them costs seven tests today: `\alpha` followed by a letter becomes the unknown
    /// command `\alphax`, and a 3 after `x^2` follows the script instead of joining it. Both are the same
    /// missing piece — nothing normalises the source so that every stop is a valid splice.
    /// </para>
    /// </summary>
    protected override EditState? Typing(EditState state, string text) =>
        WriteThroughTree(state, text) ?? (text.Length == 1 ? state.Typing(text[0]) : null);

    /// <summary>
    /// Lets the tree make the edit, when the caret is somewhere a construct has an opinion about — the 3
    /// after <c>x^2</c> belongs in the exponent. Null when the position belongs to no construct in
    /// particular and the caller should write the text itself.
    /// </summary>
    /// <remarks>
    /// Deliberately declined mid-command and mid-selection. A half-written command is being shown as the
    /// characters it is spelled with, so the layout is a step behind the source and the tree would be
    /// answering about a formula the reader is not looking at; a selection is a replacement, which is a
    /// different edit. Whitespace is declined too — a space is how you say "out of this script", so it must
    /// never be the thing that grows one.
    /// </remarks>
    private EditState? WriteThroughTree(EditState state, string text)
    {
        if (state.HasSelection || state.Raw is not null) return null;
        if (string.IsNullOrWhiteSpace(text)) return null;

        // Only from inside. A caret that has stepped out of a construct is past it — that is what the place
        // means and the whole reason it exists — so a 3 typed there follows `x^2` rather than joining its
        // exponent, and the same keystroke one bar to the left still makes it twenty-three.
        if (Level > 0) return null;

        return _tree.Write(state.Caret, text) is { } written
            ? new EditState(written.Latex, written.Caret)
            : null;
    }
}
