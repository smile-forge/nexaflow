using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Visuals.Text.Markdown.Code;

namespace Nexaflow.Visuals.Text.Markdown.Stages;

/// <summary>
/// Cuts a stretch of code at its line ends: every piece of it that runs across one — a comment over three lines, or the whole
/// of it while no grammar has read it — is cut there, and each line end is a piece of its own (<see cref="CodeKinds.LineEnd"/>).
///
/// <para>
/// A stage, because where one line stops and the next starts is a fact about the characters that the builder would otherwise
/// have to find again by looking at them — and a line found by what it says is found in the wrong place wherever the same line
/// is written twice.
/// </para>
/// </summary>
public sealed class CodeLines : IAstStage
{
    public string Name => "code:lines";

    public ContentNode Run(ContentNode tree)
    {
        if (tree.Part(Roles.Body) is not { } body) return tree;

        var pieces = new List<ContentNode>();
        var cut = false;

        foreach (var token in body.IsLeaf ? [body] : body.Children)
            cut |= Cut(token, pieces);

        if (!cut) return tree;

        var lined = ContentNode.Branch(Kinds.Sequence, pieces, Roles.Body);
        return tree.With([.. tree.Children.Select(child => ReferenceEquals(child, body) ? lined : child)]);
    }

    /// <summary>Adds a token to <paramref name="into"/>, cut at every line end in it; whether it had one.</summary>
    private static bool Cut(ContentNode token, List<ContentNode> into)
    {
        var text = token.Text;
        var role = token.Role == Roles.Body ? Roles.Element : token.Role;
        var (from, cut) = (0, false);

        for (var at = 0; at < text.Length; at++)
        {
            if (text[at] != '\n') continue;

            // A return before the line feed is the line end's, not the line's.
            var end = at > from && text[at - 1] == '\r' ? at - 1 : at;

            if (end > from) into.Add(ContentNode.Leaf(token.Kind, text[from..end], role));
            into.Add(ContentNode.Leaf(CodeKinds.LineEnd, text[end..(at + 1)], Roles.Separator));

            (from, cut) = (at + 1, true);
        }

        if (from == 0) into.Add(token);
        else if (from < text.Length) into.Add(ContentNode.Leaf(token.Kind, text[from..], role));

        return cut;
    }
}

