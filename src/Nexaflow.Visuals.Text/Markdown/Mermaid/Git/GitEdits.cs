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
    public ContentChange? Edit(ContentEdit edit) => DiagramWriting.Typed(edit, Escaping);

    /// <summary>
    /// A quote typed into a value written in quotes goes in as the entity code standing for it, and a branch name is put in quotes
    /// to hold anything a branch is not named with.
    /// </summary>
    internal static MermaidWriting? Escaping(ContentPart part, int caret, string text) =>
        MermaidWriting.Escape(part, caret, text, (_, said) => GitGrammar.Bare(said))
        ?? (part.Kind == MermaidKinds.Setting ? MermaidWriting.InQuotes(caret, text) : null);
}
