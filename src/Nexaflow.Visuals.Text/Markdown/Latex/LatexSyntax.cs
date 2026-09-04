using System.Collections.Generic;
using System.Linq;
using Nexaflow.Visuals.Text.Editing;
using WpfMath.Parsers;
using Nexaflow.Maths.Latex;

namespace Nexaflow.Visuals.Text.Markdown.Latex;

/// <summary>
/// Reading a formula without drawing it — whether it is written correctly, and where it is not.
/// <para>
/// The same parse that decides what a reader sees, asked as a question instead of a picture. Anything
/// wanting to know whether a formula is finished should ask here rather than deciding for itself: a
/// second opinion about what counts as well-formed is a second grammar, and the two would disagree
/// the first time either learned something. The typeset formula on screen and the answer given here
/// come from one parser, so what the reader is looking at and what a caller was told cannot differ.
/// </para>
/// <para>
/// Costs a parse and no typesetting, which is what makes it usable on every keystroke and off the UI
/// thread — no fonts are touched, no glyphs are measured, and nothing is laid out.
/// </para>
/// </summary>
public static class LatexSyntax
{
    /// <summary>
    /// What could not be read in <paramref name="latex"/>, in its own offsets. Empty when all of it
    /// was understood.
    /// </summary>
    public static IReadOnlyList<Diagnostic> Check(string? latex)
    {
        if (string.IsNullOrWhiteSpace(latex)) return [];

        try
        {
            // Holes asked for, because this is the question "is what has been written finished?" and an
            // argument left empty is exactly the case where the answer is no. Without them `\frac{}{2}`
            // reads as perfectly well-formed and a solver is handed a fraction with nothing on top.
            //
            // The same reading the renderer works from, and now literally so: one call, one tree, and
            // the complaints are what the pieces of it say about themselves rather than a second list
            // kept alongside.
            var reading = TexReading.Of(
                TexPipeline.Read(latex, WpfTeXFormulaParser.Instance.Draws, holes: true));

            return reading.Root.SelfAndDescendants()
                .Where(part => part.Node.Trouble is not null)
                .Select(part => Diagnostic.Of(new TexSourcePart(part), DiagnosticSeverity.Error, part.Node.Trouble!))
                .ToList();
        }
        catch
        {
            // Nothing at all could be made of it, which is the strongest kind of "not yet".
            return [new Diagnostic(0, latex.Length, DiagnosticSeverity.Error, "This is not a formula.")];
        }
    }

    /// <summary>
    /// Whether the whole of <paramref name="latex"/> could be read. False for a formula still being
    /// written, which is most of them most of the time.
    /// </summary>
    public static bool IsWellFormed(string? latex) => Check(latex).Count == 0;
}
