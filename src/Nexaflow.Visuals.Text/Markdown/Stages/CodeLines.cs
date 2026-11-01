using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Code;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Visuals.Text.Markdown.Code;

namespace Nexaflow.Visuals.Text.Markdown.Stages;

/// <summary>
/// Cuts a stretch of code at its line ends: every piece of it that runs across one — a comment over three lines, or the whole
/// of it while no grammar has read it — is cut there, each line end is a piece of its own (<see cref="CodeKinds.LineEnd"/>), and
/// each line is a part of the stretch's own (<see cref="CodeKinds.Line"/>) — the block a keystroke lays again, every other line
/// that reads as it did set down as it was laid.
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

    /// <summary>
    /// Every body of the stretch — one, or one for each stretch of a body read a stretch at a time — cut at its line ends into lines, each a
    /// part of the stretch's own holding its pieces and the line end closing it.
    /// </summary>
    public ContentNode Run(ContentNode tree)
    {
        List<ContentNode>? parts = null;

        for (var at = 0; at < tree.Children.Count; at++)
        {
            var body = tree.Children[at];

            if (body.Role != Roles.Body)
            {
                parts?.Add(body);
                continue;
            }

            parts ??= [.. tree.Children.Take(at)];

            var pieces = new List<ContentNode>();
            foreach (var token in body.IsLeaf ? [body] : body.Children) Cut(token, pieces);

            Lines(pieces, parts);
        }

        return parts is null ? tree : tree.With(parts);
    }

    /// <summary>Adds <paramref name="pieces"/> to <paramref name="into"/> a line at a time, each line closed by the line end after it.</summary>
    private static void Lines(List<ContentNode> pieces, List<ContentNode> into)
    {
        var line = new List<ContentNode>();

        foreach (var piece in pieces)
        {
            line.Add(piece);
            if (piece.Kind != CodeKinds.LineEnd) continue;

            into.Add(ContentNode.Branch(CodeKinds.Line, line, Roles.Element, line[0].Offset));
            line = [];
        }

        if (line.Count > 0) into.Add(ContentNode.Branch(CodeKinds.Line, line, Roles.Element, line[0].Offset));
    }

    /// <summary>
    /// Adds a token to <paramref name="into"/>, cut at every line end in it; whether it had one.
    ///
    /// <para>
    /// Each piece was read from where the token was read from, so many characters along — which is a fact this knows
    /// exactly, being the one cutting the token up, and says so rather than leaving it to be worked out from the order.
    /// </para>
    /// </summary>
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

            if (end > from) into.Add(ContentNode.Leaf(token.Kind, text[from..end], role, offset: token.Offset + from));
            into.Add(ContentNode.Leaf(CodeKinds.LineEnd, text[end..(at + 1)], Roles.Separator, offset: token.Offset + end));

            (from, cut) = (at + 1, true);
        }

        if (from == 0) into.Add(token.As(role));
        else if (from < text.Length) into.Add(ContentNode.Leaf(token.Kind, text[from..], role, offset: token.Offset + from));

        return cut;
    }
}

