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
    /// <summary>
    /// What this content is: how it is built, and what writing into it means. Both are the formula's own
    /// and neither is the element's — see <see cref="LatexContent"/>.
    /// </summary>
    /// <summary>Raised when the reader's own editing changed the LaTeX.</summary>
    public event EventHandler? LatexChanged;

    public FormulaElement(string latex, MarkdownPalette palette, double scale, bool inline = false)
        : base(latex ?? string.Empty, palette, new LatexContent(scale, inline))
    {
        SourceChanged += (_, _) => LatexChanged?.Invoke(this, EventArgs.Empty);

        WashPad = scale * 0.14;
        Rebuild();
    }

    /// <summary>The LaTeX this is showing.</summary>
    public string Latex => Source;



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

    /// <summary>Settles a half-written command — the host's Enter and space arrive here.</summary>
    public void Commit(string separator = " ") { if (!IsReadOnly) Settle(separator); }
}
