using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Lines;

namespace Nexaflow.Markdown.PlainText;

/// <summary>
/// Plain text, read: one run of its characters, held as written — and, once it is to be laid, cut into lines, each one run of what is
/// written on it and its line end (<see cref="InLines"/>). A line is set as one run, so it is held as one: a piece for every word of a
/// document would cost a node, a part and a string each, for nothing that asks for them.
///
/// <para>
/// Nothing in it is anything but characters: no grammar, nothing that opens and closes. So it is read a stretch at a time as everything
/// read line by line is (<see cref="LineStretches"/>), and a stretch holds one run of characters that stands for them in the document,
/// cut into lines only for the stretches being laid.
/// </para>
/// </summary>
public static class TextParser
{
    /// <summary>The name a tree of plain text is known by (<see cref="BlockNode"/>), and the word a fence of it is written under.</summary>
    public const string Language = "text";

    /// <summary>Text, read as written: one run of its characters, which a stage cuts into lines (<see cref="TextIntoLines"/>).</summary>
    public static ContentNode Parse(string? source) =>
        new BlockNode(Language, [ContentNode.Leaf(Kinds.Verbatim, source ?? string.Empty, Roles.Body, offset: 0)], TextKinds.Text, offset: 0);

    /// <summary>A parse reading text a stretch at a time, as everything read line by line is (<see cref="LineStretches"/>).</summary>
    public static Func<ContentStretch, ContentParse> Windowed() => stretch => LineStretches.Read(stretch, Parse);

    /// <summary>
    /// <paramref name="tree"/>, a stretch of text, with every body of it cut at its line ends into lines (<see cref="TextKinds.Line"/>),
    /// each what is written on it (<see cref="Kinds.Words"/>) and the line end closing it (<see cref="LineKinds.LineEnd"/>), a return
    /// before a line feed the line end's.
    /// </summary>
    public static ContentNode InLines(ContentNode tree)
    {
        List<ContentNode>? parts = null;

        for (var index = 0; index < tree.Children.Count; index++)
        {
            var body = tree.Children[index];

            if (body.Role != Roles.Body || body.Offset is not { } from)
            {
                parts?.Add(body);
                continue;
            }

            parts ??= [.. tree.Children.Take(index)];
            Lines(body.TextAsRead(), from, parts);
        }

        return parts is null ? tree : tree.With(parts);
    }

    /// <summary>The lines of <paramref name="text"/>, read at <paramref name="at"/>, added to <paramref name="into"/>.</summary>
    private static void Lines(string text, int at, List<ContentNode> into)
    {
        var start = 0;

        do
        {
            var feed = text.IndexOf('\n', start);
            var end = feed < 0 ? text.Length : feed + 1;

            into.Add(Line(text, start, end, at));
            start = end;
        }
        while (start < text.Length);
    }

    /// <summary>
    /// The line of <paramref name="text"/> from <paramref name="start"/> to <paramref name="end"/>, read at <paramref name="at"/>: what is written
    /// on it as one run, and its line end a piece of its own.
    /// </summary>
    private static ContentNode Line(string text, int start, int end, int at)
    {
        var written = end > start && text[end - 1] == '\n' ? end - 1 > start && text[end - 2] == '\r' ? end - 2 : end - 1 : end;
        var parts = new List<ContentNode>(2);

        if (written > start) parts.Add(ContentNode.Leaf(Kinds.Words, text[start..written], offset: at + start));
        if (end > written) parts.Add(ContentNode.Leaf(LineKinds.LineEnd, text[written..end], Roles.Separator, offset: at + written));

        return ContentNode.Branch(TextKinds.Line, parts, Roles.Element, offset: at + start);
    }
}
