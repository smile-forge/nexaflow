using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Code;
using Nexaflow.Markdown.Lines;
using Nexaflow.Syntax;
using System;
using System.Collections.Generic;

namespace Nexaflow.Visuals.Text.Markdown.Code;

/// <summary>
/// Code, read: one run of characters, held as written — and then cut into lines, each cut into what its reading's grammar calls each
/// stretch of it (<see cref="InLines"/>), each a piece whose kind is what the grammar called it, so a keyword is a keyword in the tree
/// and not a colour, and the builder colours by kind knowing nothing about tree-sitter.
///
/// <para>
/// The grammar's spans may overlap and the last one wins, which is what a grammar means by a pattern later in its query being
/// more specific. So they are flattened a character at a time and the runs that agree are joined back up — a token is one piece,
/// as a run of words is.
/// </para>
/// </summary>
public static class CodeParser
{
    /// <summary>Code, read as written: one run of its characters, which a stage colours from its reading's syntax (<see cref="Stages.CodeHighlights"/>).</summary>
    /// <param name="language">The grammar the code is read against, or nothing for code in a language no grammar reads.</param>
    public static ContentNode Parse(string? source, string language) =>
        new BlockNode(language, [ContentNode.Leaf(Kinds.Verbatim, source ?? string.Empty, Roles.Body, offset: 0)], CodeKinds.Code, offset: 0);

    /// <summary>A parse reading code a stretch at a time, as everything read line by line is (<see cref="LineStretches"/>).</summary>
    /// <param name="language">The grammar the code is read against, or nothing for code in a language no grammar reads.</param>
    public static Func<ContentStretch, ContentParse> Windowed(string language) => stretch => LineStretches.Read(stretch, source => Parse(source, language));

    /// <summary>
    /// <paramref name="tree"/>, a stretch of code, with every body of it cut at its line ends into lines (<see cref="CodeKinds.Line"/>), each
    /// holding the pieces <paramref name="spans"/> — in the tree's own offsets — call its characters, and the line end closing it
    /// (<see cref="CodeKinds.LineEnd"/>), a return before a line feed the line end's. A line <paramref name="kept"/> has from before, written
    /// and coloured as this one is, is that line again, moved along to where this one stands.
    /// </summary>
    internal static ContentNode InLines(ContentNode tree, IReadOnlyList<HighlightSpan> spans, CodeLinePool? kept = null)
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
            Lines(body.TextAsRead(), from, spans, kept, parts);
        }

        return parts is null ? tree : tree.With(parts);
    }

    /// <summary>
    /// The lines of <paramref name="text"/>, read at <paramref name="at"/>, added to <paramref name="into"/>: each coloured by the parts of
    /// <paramref name="spans"/> — in the reading's offsets — reaching into it, in their order.
    /// </summary>
    private static void Lines(string text, int at, IReadOnlyList<HighlightSpan> spans, CodeLinePool? kept, List<ContentNode> into)
    {
        var starts = new List<int> { 0 };
        for (var place = text.IndexOf('\n'); place >= 0 && place + 1 < text.Length; place = text.IndexOf('\n', place + 1)) starts.Add(place + 1);

        // What each line is coloured as, counted from where it starts: each span reaching it, cut to it, in the order the spans came.
        var coloured = new List<HighlightSpan>?[starts.Count];

        foreach (var span in spans)
        {
            var (low, high) = (Math.Max(span.Start - at, 0), Math.Min(span.Start + span.Length - at, text.Length));
            if (high <= low) continue;

            for (var line = Line(starts, low); line < starts.Count && starts[line] < high; line++)
            {
                var end = line + 1 < starts.Count ? starts[line + 1] : text.Length;
                var (from, to) = (Math.Max(low, starts[line]), Math.Min(high, end));

                if (to > from) (coloured[line] ??= []).Add(new HighlightSpan(from - starts[line], to - from, span.Capture));
            }
        }

        for (var line = 0; line < starts.Count; line++)
        {
            var (start, end) = (starts[line], line + 1 < starts.Count ? starts[line + 1] : text.Length);
            HighlightSpan[] colours = coloured[line] is { } said ? [.. said] : [];

            if (kept?.Line(text.AsSpan(start, end - start), colours, at + start) is { } again)
            {
                into.Add(again);
                continue;
            }

            var made = Line(text, start, end, colours, at);
            kept?.Keep(text[start..end], colours, made, at + start);
            into.Add(made);
        }
    }

    /// <summary>Which of the lines starting at <paramref name="starts"/> holds <paramref name="offset"/>.</summary>
    private static int Line(List<int> starts, int offset)
    {
        var found = starts.BinarySearch(offset);
        return found >= 0 ? found : ~found - 1;
    }

    /// <summary>
    /// The line of <paramref name="text"/> from <paramref name="start"/> to <paramref name="end"/>, read at <paramref name="at"/>: its characters
    /// cut into what <paramref name="colours"/> — counted from the line's start — call them, the last over a character winning and the runs
    /// that agree joined back up, and its line end a piece of its own.
    /// </summary>
    private static ContentNode Line(string text, int start, int end, HighlightSpan[] colours, int at)
    {
        var written = end > start && text[end - 1] == '\n' ? end - 1 > start && text[end - 2] == '\r' ? end - 2 : end - 1 : end;
        var called = new string?[written - start];

        foreach (var span in colours)
            for (var place = span.Start; place < Math.Min(span.Start + span.Length, called.Length); place++) called[place] = span.Capture;

        var parts = new List<ContentNode>();
        var from = 0;

        for (var place = 1; place <= called.Length; place++)
            if (place == called.Length || called[place] != called[from])
            {
                // What the grammar had no name for is held as written, which is what it looks like: text.
                parts.Add(ContentNode.Leaf(called[from] ?? Kinds.Verbatim, text[(start + from)..(start + place)], offset: at + start + from));
                from = place;
            }

        if (end > written) parts.Add(ContentNode.Leaf(CodeKinds.LineEnd, text[written..end], Roles.Separator, offset: at + written));

        return ContentNode.Branch(CodeKinds.Line, parts, Roles.Element, offset: at + start);
    }
}
