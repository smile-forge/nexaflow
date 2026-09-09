using System.Linq;
using Nexaflow.Maths.Latex;
using Nexaflow.Visuals.Text.Editing;
using WpfMath.Parsers;
using XamlMath;
using WpfMath.Rendering;
using XamlMath.Rendering;

namespace Nexaflow.Visuals.Text.Markdown.Latex;

/// <summary>
/// The one thing that knows a formula is a formula.
///
/// <para>
/// It reads LaTeX, typesets it, and lays the result out as pieces. Everything after that — painting,
/// hit-testing, where a caret goes, what a drag took, what a selection washes — is the same code a tune
/// and a barcode run, over the <see cref="Laid"/> this hands back. That is the whole of the arrangement:
/// a builder per kind of content, and nothing per kind of content anywhere else.
/// </para>
/// <para>
/// There is deliberately no <c>LatexLayout</c> any more. It held a tree, forwarded the size and the
/// source to it, and offered a <c>Paint</c> that was the shared painter with a default argument — three
/// objects wrapping one, and the outer two are why a formula could not be hosted by the same element as
/// a score.
/// </para>
/// </summary>
public static class LatexBuilder
{
    /// <summary>
    /// Typesets <paramref name="latex"/> and records where every piece landed, or returns null when it
    /// will not parse — which the caller shows as source rather than as a formula.
    /// </summary>
    /// <param name="shownAsWritten">
    /// A stretch to set as the characters written rather than read as maths — the piece being edited,
    /// which has to be seen exactly as typed while the formula around it stays typeset.
    /// <para>
    /// It goes through the typesetter rather than being painted over the top afterwards, which is the
    /// only way the rest of the formula can be laid out knowing it is there. Painted over, a stretch of
    /// any length in the middle of a formula simply covered whatever followed it.
    /// </para>
    /// </param>
    /// <param name="placeholders">
    /// Whether an argument or table cell left empty is given a hole to stand in it. Asked for by a
    /// surface being written on, where the hole is how the reader sees there is something still to
    /// write and how they aim at it. Off by default, because a box in the middle of a formula that is
    /// only being read would simply be wrong, and reading is the commoner case.
    /// </param>
    public static LatexTree? Build(string latex, double scale, bool inline = false, string systemFont = "Arial",
                                   RawZone? shownAsWritten = null, bool placeholders = false)
    {
        if (string.IsNullOrEmpty(latex)) return null;

        try
        {
            // The tables, not the reader. Everything below builds from our own reading; what this is
            // asked for is what a name means to a typesetter — which symbol, which face, which command
            // it has a drawing for.
            var knowledge = WpfTeXFormulaParser.Instance;

            var editing = shownAsWritten is { } zone && zone.Length > 0
                ? (zone.Start, zone.Length)
                : ((int, int)?)null;

            // One reading, every time, whatever the caret is doing. What cannot be drawn and what is
            // being typed are both settled before this — they come back as pieces that say they are to
            // be shown rather than read, and the builder sets them as characters without having to know
            // which of the two it is looking at.
            // Asked of the builder, because the builder is what draws. It was asked of the tables, which
            // describe what the engine's own parser could read — and being a different question, it came back
            // a different answer: a `\ ` the builder sets directly was shown as its own characters in red.
            var read = TexPipeline.Read(
                latex, name => XamlMath.TexFormulaBuilder.Draws(name, knowledge), editing, placeholders);
            var reading = TexReading.Of(read);
            var formula = XamlMath.TexFormulaBuilder.Build(reading.Root, knowledge);

            if (formula is null)
            {
                // Nothing here could set it as maths, so it is set as it was typed. Which is the same
                // answer this gives a stretch under the caret and a command nobody has heard of, reached
                // by the same road — and a great deal more use to whoever wrote it than a blank space.
                reading = TexReading.Of(TexNode.Branch(TexKind.Sequence, [TexNode.Shown(latex)]));
                formula = XamlMath.TexFormulaBuilder.Build(reading.Root, knowledge);
            }

            if (formula is null) return null;

            var environment = WpfTeXEnvironment.Create(
                style: inline ? TexStyle.Text : TexStyle.Display,
                scale: scale,
                systemTextFontName: systemFont);

            var capture = new LatexLayoutCapture(scale, reading);
            formula.RenderTo(capture, environment, 0, 0);

            // …which also settles the tree onto the origin. A shifted or transformed box can land above or
            // left of where the pen started, and a tree with negative coordinates would put the caret outside
            // the control that draws it — one number now, because everything in it is relative to the root.
            capture.FinishRendering();
            if (capture.Tree is not { } laid) return null;

            // Asked of the tree rather than collected on the way through it. A piece that could not be
            // read carries the reason it could not, so there is one place the answer lives and no second
            // list to fall out of step with it — and a piece being typed carries nothing, which is how
            // it draws without being complained about.
            var trouble = reading.Root.SelfAndDescendants()
                .Where(part => part.Node.Trouble is not null)
                .Select(part => TexSourcePart.Trouble(part, DiagnosticSeverity.Error, part.Node.Trouble!))
                .Concat(formula.Ignored.Select(part => TexSourcePart.Trouble(
                    part,
                    DiagnosticSeverity.Warning,
                    "This was read, and nothing here knows how to draw it.")))
                .ToList();

            return new LatexTree(latex, reading, new Laid(laid, capture.Size, trouble));
        }
        catch
        {
            // Every reading failure is the same answer to the caller: there is no formula to map.
            return null;
        }
    }
}
