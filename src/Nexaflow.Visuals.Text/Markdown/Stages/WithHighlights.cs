using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Visuals.Text.Markdown.Code;

namespace Nexaflow.Visuals.Text.Markdown.Stages;

/// <summary>
/// What a grammar makes of a block of code, where it has already been read against it: every stretch it names is a token of
/// that kind (<see cref="WithTokens"/>). Where the grammar has not read it yet the code stays as written, and is coloured
/// when the reading lands and the content is laid again (<see cref="CodeSpans"/>) — nothing waits for it.
/// </summary>
/// <param name="grammar">The grammar the block's fence named, or null for one naming none.</param>
public sealed class WithHighlights(string? grammar) : IAstStage
{
    public string Name => "code:highlights";

    public ContentNode Run(ContentNode tree) =>
        grammar is { Length: > 0 } reads && CodeSpans.For(reads, tree.Print()) is { } spans ? new WithTokens(spans).Run(tree) : tree;
}
