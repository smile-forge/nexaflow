using System;
using System.Collections.Generic;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Syntax;

namespace Nexaflow.Visuals.Text.Markdown.Stages;

/// <summary>
/// Reads a stretch of code into the tokens a grammar found in it — each one a piece whose kind is what the
/// grammar called it, so a keyword is a keyword in the tree and not a colour.
///
/// <para>
/// A stage, because what a run of characters <em>is</em> in some other language is a fact about what the
/// characters amount to, not about the characters. The builder then colours by kind and knows nothing about
/// tree-sitter, exactly as it knows nothing about Markdig.
/// </para>
/// <para>
/// The spans may overlap and the last one wins, which is what a grammar means by a pattern later in its
/// query being more specific. So they are flattened a character at a time and the runs that agree are joined
/// back up — a token is one piece, as a run of words is.
/// </para>
/// </summary>
public sealed class WithTokens(IReadOnlyList<HighlightSpan> spans) : IAstStage
{
    public string Name => "code:tokens";

    public ContentNode Run(ContentNode tree) =>
        tree.Part(Roles.Body) is { IsLeaf: true, Text.Length: > 0 } body
            ? tree.With([.. Replaced(tree.Children, body, Tokens(body.Text))])
            : tree;

    private static IEnumerable<ContentNode> Replaced(IReadOnlyList<ContentNode> children, ContentNode body, ContentNode read)
    {
        foreach (var child in children) yield return ReferenceEquals(child, body) ? read : child;
    }

    private ContentNode Tokens(string text)
    {
        var called = new string?[text.Length];

        foreach (var span in spans)
        {
            var to = Math.Min(span.Start + span.Length, text.Length);

            for (var at = Math.Max(span.Start, 0); at < to; at++) called[at] = span.Capture;
        }

        var parts = new List<ContentNode>();
        var from = 0;

        for (var at = 1; at <= text.Length; at++)
            if (at == text.Length || called[at] != called[from])
            {
                // What the grammar had no name for is held as written, which is what it looks like: text.
                parts.Add(ContentNode.Leaf(called[from] ?? Kinds.Verbatim, text[from..at]));
                from = at;
            }

        return ContentNode.Branch(Kinds.Sequence, parts, Roles.Body);
    }
}
