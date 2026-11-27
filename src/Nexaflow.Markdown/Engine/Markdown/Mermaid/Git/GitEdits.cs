using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Git;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Git;

/// <summary>What an edit means in a git graph: a branch is put in quotes to hold what a bare name cannot. Every other key does what it does anywhere.</summary>
internal sealed class GitEdits : IOnEdit
{
    /// <inheritdoc/>
    public ContentChange? Edit(ContentEdit edit)
    {
        var change = DiagramWriting.Typed(edit, Escaping) ?? OrdinaryEdits.Keyed(edit);

        return change is null ? null : DiagramRenames.AtEveryMention(edit, change, GitRoles.Name);
    }

    /// <summary>
    /// In a string — a name or a value in quotes — a backslash keeps the character after it, as Mermaid's strings are written, so one
    /// typed there is kept by another, and so is a single quote in a string written in single quotes; a double quote goes in as the
    /// entity code standing for it. A branch name is put in quotes to hold anything a branch is not named with, a backslash in it kept
    /// by another.
    /// </summary>
    internal static MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        var quote = Quote(part);
        var doubled = text.Replace("\\", "\\\\");

        if (quote == '\'')
        {
            var kept = doubled.Replace("'", "\\'");
            return kept == text ? null : new MermaidWriting(caret, caret, kept, caret + kept.Length);
        }

        if (quote == '"')
        {
            var kept = MermaidText.Quoted(doubled);
            return kept == text ? null : new MermaidWriting(caret, caret, kept, caret + kept.Length);
        }

        return MermaidWriting.Escape(part, caret, doubled, (_, said) => GitParser.Bare(said))
            ?? (part.Kind == MermaidKinds.Setting ? MermaidWriting.InQuotes(caret, text) : null);
    }

    /// <summary>The quote the string <paramref name="part"/> stands in is written in — a value's own, or a name's — or null where it stands in none.</summary>
    private static char? Quote(ContentPart part)
    {
        if (part.Kind == MermaidKinds.Setting) return part.Text is [var first, ..] && first is '"' or '\'' ? first : null;

        for (var holder = part.Parent; holder is not null; holder = holder.Parent)
            if (holder.Children.FirstOrDefault(child => child.Role == Roles.Open) is { } open)
                return open.Text is [var opened] && opened is '"' or '\'' ? opened : null;

        return null;
    }
}
