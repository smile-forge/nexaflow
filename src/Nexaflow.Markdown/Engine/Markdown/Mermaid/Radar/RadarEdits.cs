using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Radar;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Radar;

/// <summary>
/// What an edit means in a radar chart: in a label in quotes, what would end it is kept by a backslash, and Backspace or Delete over
/// a backslash and what it keeps takes the two together; a name is put in quotes to hold what a bare one cannot. Every other key
/// does what it does anywhere.
/// </summary>
internal sealed class RadarEdits : IOnEdit
{
    /// <inheritdoc/>
    public ContentChange? Edit(ContentEdit edit) =>
        edit.Kind is EditKind.Erasing or EditKind.Deleting ? Unkept(edit) : DiagramWriting.Typed(edit, Escaping);

    /// <summary>
    /// In a label in quotes, what would end it kept (<see cref="RadarParser.Escaped"/>). A bare name holds a word starting with a
    /// letter or an underscore, and is put in quotes to hold anything else; see <see cref="MermaidWriting.Escape"/> for names in quotes
    /// and labels in brackets. A name in quotes ends where a label in quotes does, a backslash keeping the character after it, so a
    /// backslash typed into a name — or into one the typing puts in quotes — is kept by another.
    /// </summary>
    internal static MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (RadarParser.QuotedOf(part) is { } quoted && RadarParser.Opened(quoted) is { } quote)
        {
            var escaped = RadarParser.Escaped(text, quote);
            return escaped == text ? null : new MermaidWriting(caret, caret, escaped, caret + escaped.Length);
        }

        if (part.Parent?.Kind != MermaidKinds.Name) return MermaidWriting.Escape(part, caret, text, Bare);

        var kept = text.Replace(RadarParser.Backslash.ToString(), @"\\", StringComparison.Ordinal);
        return MermaidWriting.Escape(part, caret, kept, Bare) ?? (kept == text ? null : new MermaidWriting(caret, caret, kept, caret + kept.Length));

        static bool Bare(ContentPart _, string said) => RadarParser.Bare(said);
    }

    /// <summary>Backspace or Delete over a backslash and what it keeps in a label: the two together, since neither says anything without the other.</summary>
    private static ContentChange? Unkept(ContentEdit edit)
    {
        if (edit.State.HasSelection || edit.Part is not { } part || RadarParser.QuotedOf(part) is not { } quoted) return null;

        var caret = edit.State.Caret;
        var erasing = edit.Kind == EditKind.Erasing;

        foreach (var escape in quoted.SelfAndDescendants())
            if (escape.Kind == RadarKinds.Escape && (erasing ? escape.Start < caret && caret <= escape.End : escape.Start <= caret && caret < escape.End))
                return ContentChange.Write(escape.Start, escape.Length, string.Empty, escape.Start);

        return null;
    }
}
