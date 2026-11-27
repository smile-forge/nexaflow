using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Architecture;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Architecture;

/// <summary>
/// What an edit means in an architecture diagram: a title is put in quotes to hold what would end it, and what an id cannot hold is
/// dropped. Every other key does what it does anywhere.
/// </summary>
internal sealed class ArchitectureEdits : IOnEdit
{
    /// <inheritdoc/>
    public ContentChange? Edit(ContentEdit edit) => DiagramWriting.Typed(edit, Escaping);

    /// <summary>
    /// A title and an icon are in brackets. In a title in quotes, what would end it is kept (<see cref="ArchitectureParser.Escaped"/>); a
    /// title without them is put in double quotes to hold a quote, a bracket closing it or a comment, and an icon is put in quotes the
    /// same way. An id, the group something is in and a side are written bare and cannot be quoted at all, so what they cannot hold
    /// is dropped — and a hole standing where one of those goes holds what it will hold.
    /// </summary>
    internal static MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (ArchitectureParser.QuotedOf(part) is { } quoted && ArchitectureParser.Opened(quoted) is { } quote)
        {
            var kept = ArchitectureParser.Escaped(text, quote);
            return kept == text ? null : new MermaidWriting(caret, caret, kept, caret + kept.Length);
        }

        if (Titled(part, caret, text) is { } titled) return titled;
        if (MermaidWriting.Escape(part, caret, text) is { } escaped) return escaped;

        var role = part.Kind == Kinds.Hole ? part.Parent?.Role : part.Role;
        return role is ArchitectureRoles.Id or ArchitectureRoles.In or ArchitectureRoles.Side
            ? MermaidWriting.Only(caret, text, MermaidLine.Letter)
            : null;
    }

    /// <summary>
    /// Text written into a title in square brackets without quotes: as it is, where it holds nothing that would end the title — and
    /// otherwise the title put in double quotes, which are a string, where a backslash is kept by another as a quote is by its entity
    /// code. Null for anything but such a title.
    /// </summary>
    private static MermaidWriting? Titled(ContentPart part, int caret, string text)
    {
        if (part.Kind is not (Kinds.Words or Kinds.Hole) || part.Parent is not { Kind: MermaidKinds.Label } title) return null;
        if (title.Children.FirstOrDefault(child => child.Role == Roles.Open) is not { Text: "[" } open) return null;

        var said = part.Kind == Kinds.Hole ? string.Empty : part.Text;
        var at = Math.Clamp(caret - part.Start, 0, said.Length);
        var (before, after) = (said[..at] + text, said[at..]);

        if (!(before + after).Any(character => character is '"' or '%' or ']')) return null;

        var close = title.Children.LastOrDefault(child => child.Role == Roles.Close);
        var head = "\"" + ArchitectureParser.Escaped(before, '"');
        return new MermaidWriting(open.End, close?.Start ?? title.End, head + ArchitectureParser.Escaped(after, '"') + "\"", open.End + head.Length);
    }
}
