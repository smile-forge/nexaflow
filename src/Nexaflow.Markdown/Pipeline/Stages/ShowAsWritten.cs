using System.Text;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Pipeline.Stages;

/// <summary>
/// The same tree with a stretch of it shown as the characters it was written with rather than read.
///
/// <para>
/// What a surface being written on runs last, and only while somebody is mid-keystroke. A half-written
/// construct is invalid almost by definition — <c>\fra</c>, <c>[CE</c>, <c>K:Bbmi</c> — and saying so on
/// every keystroke would be the wrong thing to draw. Shown instead, the reader sees exactly what they
/// have typed while everything around it stays set.
/// </para>
/// <para>
/// The stretch is widened to whole pieces — a caret three characters into a command is not editing three
/// characters, it is editing a command — and taken as deep as it will go, so that typing in one cell of a
/// table does not stop the table being a table, or in one bar of a tune stop the tune being barred.
/// </para>
/// <para>
/// Language-agnostic: it works in offsets and kinds this assembly already owns, and every language wants
/// exactly this behaviour, so it is written once.
/// </para>
/// </summary>
public sealed class ShowAsWritten(int start, int length) : IAstStage
{
    public string Name => "show-as-written";

    /// <summary>The stage, or nothing at all when there is no stretch to show.</summary>
    public static ShowAsWritten? Of((int Start, int Length)? zone) =>
        zone is { Length: > 0 } at ? new ShowAsWritten(at.Start, at.Length) : null;

    public ContentNode Run(ContentNode tree) =>
        length <= 0 ? tree : Show(tree, 0, start, start + length) ?? tree;

    /// <summary>
    /// This piece rewritten so that everything between <paramref name="from"/> and <paramref name="to"/>
    /// is shown rather than read, or null where the stretch does not reach it.
    /// </summary>
    private static ContentNode? Show(ContentNode node, int at, int from, int to)
    {
        var end = at + node.Width;
        if (to <= at || from >= end) return null;

        // All of this piece is inside the stretch, so this piece is what gets shown.
        if (from <= at && to >= end) return ContentNode.Shown(node.Print(), role: node.Role);

        // Part of it, and nothing underneath to be more precise about: a caret inside a word is still
        // editing the word.
        if (node.IsLeaf) return ContentNode.Shown(node.Text, role: node.Role);

        var starts = new int[node.Children.Count];
        var cursor = at;
        for (var i = 0; i < node.Children.Count; i++)
        {
            starts[i] = cursor;
            cursor += node.Children[i].Width;
        }

        int first = -1, last = -1;
        for (var i = 0; i < node.Children.Count; i++)
        {
            // A piece standing for no source cannot be reached by a caret: it is what something means,
            // and somebody typing is typing the thing itself.
            if (node.Children[i].Width == 0) continue;
            if (to <= starts[i] || from >= starts[i] + node.Children[i].Width) continue;

            if (first < 0) first = i;
            last = i;
        }

        if (first < 0) return null;

        var rebuilt = new List<ContentNode>(node.Children.Count);
        for (var i = 0; i < first; i++) rebuilt.Add(node.Children[i]);

        if (first == last)
        {
            rebuilt.Add(Show(node.Children[first], starts[first], from, to) ?? node.Children[first]);
        }
        else
        {
            // Several pieces at once, so what replaces them is one run of characters playing none of
            // their parts — a numerator and the brace after it are not a numerator.
            var text = new StringBuilder();
            for (var i = first; i <= last; i++) node.Children[i].PrintTo(text);
            rebuilt.Add(ContentNode.Shown(text.ToString(), role: Roles.Element));
        }

        for (var i = last + 1; i < node.Children.Count; i++) rebuilt.Add(node.Children[i]);

        return node.With(rebuilt);
    }
}
