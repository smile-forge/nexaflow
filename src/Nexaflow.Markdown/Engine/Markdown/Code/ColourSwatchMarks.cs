using System.Collections.Generic;

using Nexaflow.Markdown.Editing;
using Nexaflow.Visuals.Text.Editor.Highlighting;

namespace Nexaflow.Visuals.Text.Markdown.Code;

/// <summary>
/// The colours a code file writes — <c>#FF3B30</c>, <c>rgb(255 59 48)</c>, <c>Tomato</c>, a brush a XAML resource names — each shown by a bar
/// under it in that colour. Asked only for the stretch being drawn, and read from what its grammar has already coloured there
/// (<see cref="ContentMarkRequest.Syntax"/>), so nothing is parsed for it and a long file costs a screen's worth.
/// </summary>
public sealed class ColourSwatchMarks(string grammar) : ContentMarkLayer
{
    /// <summary>Whether an eight-digit hex literal puts its alpha first, as XAML reads it, or last, as CSS does.</summary>
    private readonly bool _alphaFirst = ColorLiterals.AlphaFirst(grammar);

    public override void Mark(ContentMarkRequest request, ICollection<ContentMark> into)
    {
        if (request.Syntax is not CodeSyntax syntax) return;

        var document = request.Document;

        foreach (var span in syntax.Known(request.Start, request.End))
        {
            if (span.Length == 0 || span.Start < 0 || span.Start + span.Length > document.Length || !CouldNameAColour(span.Capture)) continue;

            var written = document.Slice(span.Start, span.Length);

            if (ColorLiterals.Parse(written, _alphaFirst) is { } colour)
            {
                // Fully clear shows nothing; the colour carries its own alpha, so it is not dimmed twice.
                if (colour.A != 0)
                    into.Add(new ContentMark(ContentMarkKind.Underbar, span.Start, span.Length, string.Empty)
                    {
                        Colour = ((uint)colour.A << 24) | ((uint)colour.R << 16) | ((uint)colour.G << 8) | colour.B,
                    });
            }
            else if (span.Capture == "constant")
            {
                // A resource key, drawn in what the running theme names it — which is what makes {StaticResource AccentBrush} previewable.
                // Only a constant is tried, and XAML marks a key that way only inside a *Resource extension, so a binding path is never
                // looked up; a key the theme does not hold is not drawn.
                into.Add(new ContentMark(ContentMarkKind.Underbar, span.Start, span.Length, written));
            }
        }
    }

    /// <summary>
    /// Only value-shaped roles: an element or an attribute name is never a colour, so a <c>&lt;Red&gt;</c> element or a <c>Beige</c>
    /// property grows no bar.
    /// </summary>
    private static bool CouldNameAColour(string capture) => capture is "string" or "constant" or "variable" or "number";
}
