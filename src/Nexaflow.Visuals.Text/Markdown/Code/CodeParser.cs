using Nexaflow.Markdown.Ast;
using Nexaflow.Syntax;
using System;
using System.Collections.Generic;

namespace Nexaflow.Visuals.Text.Markdown.Code;

/// <summary>
/// Code, read: one run of characters, held as written — or, read against a grammar (<see cref="CodeSpans"/>), cut into what the
/// grammar calls each stretch of it, each a piece whose kind is what the grammar called it, so a keyword is a keyword in the tree
/// and not a colour, and the builder colours by kind knowing nothing about tree-sitter. Where its lines break is a stage's
/// (<see cref="Stages.CodeLines"/>).
///
/// <para>
/// The grammar's spans may overlap and the last one wins, which is what a grammar means by a pattern later in its query being
/// more specific. So they are flattened a character at a time and the runs that agree are joined back up — a token is one piece,
/// as a run of words is.
/// </para>
/// </summary>
public static class CodeParser
{
    /// <param name="language">The grammar the code is read against, or nothing for code in a language no grammar reads.</param>
    /// <param name="spans">What a grammar made of it, or null where nothing has read it against one.</param>
    public static ContentNode Parse(string? source, string language, IReadOnlyList<HighlightSpan>? spans = null)
    {
        source ??= string.Empty;

        return new BlockNode(language,
            [spans is null || source.Length == 0 ? ContentNode.Leaf(Kinds.Verbatim, source, Roles.Body) : Tokens(source, spans)],
            CodeKinds.Code);
    }

    private static ContentNode Tokens(string text, IReadOnlyList<HighlightSpan> spans)
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
