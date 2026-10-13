using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Prose;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// What content reads as where that is what it draws rather than what was written (<see cref="IContentLanguage.ReadsAsDrawn"/>):
/// the words of its runs of words in the order a reader comes to them, a line between two runs, and every character with the
/// stretch of source it stands for — so a search finds what a reader reads and nothing of the machinery around it, a copy
/// carries the words, and whatever is found is still a place in the source the page can show.
///
/// <para>
/// Read from what the content read to, not from what was laid, so it reaches all of a long document however little of it
/// is on the page. The engine keeps it (<see cref="ContentEngine"/>), and the element washes what is found at the places in
/// the source it stands for, as it washes anything.
/// </para>
/// </summary>
public sealed class DrawnText
{
    private readonly SourcePlace[] _from;

    private DrawnText(string text, SourcePlace[] from)
    {
        Text = text;
        _from = from;
    }

    /// <summary>The page's words, as a reader reads them.</summary>
    public string Text { get; }

    /// <summary>
    /// What <paramref name="tree"/> reads as: the words of every run of words in it (<see cref="Kinds.Words"/>), in order, a line
    /// between one run and the next, and every character with the stretch of source it stands for. A character written as an
    /// escape — an RTF <c>\'e9</c>, a markdown <c>&amp;eacute;</c> — reads as what it decodes to, standing for the whole escape;
    /// nothing else in a run that is not a word is read.
    /// </summary>
    public static DrawnText Of(ContentNode tree)
    {
        var text = new StringBuilder();
        var from = new List<SourcePlace>();

        Runs(tree, text, from);

        return new DrawnText(text.ToString(), [.. from]);
    }

    /// <summary>Every run of words <paramref name="node"/> holds, each on a line of its own.</summary>
    private static void Runs(ContentNode node, StringBuilder text, List<SourcePlace> from)
    {
        if (node.IsDerived) return;

        if (node.Kind == Kinds.Words)
        {
            Lined(text, from);
            Worded(node, text, from);

            return;
        }

        foreach (var child in node.Children) Runs(child, text, from);
    }

    /// <summary>The words of a run: what its words were written as, and what its escapes decode to.</summary>
    private static void Worded(ContentNode node, StringBuilder text, List<SourcePlace> from)
    {
        if (node.IsDerived) return;

        var at = node.Offset ?? 0;

        switch (node.Kind)
        {
            case MarkdownKinds.Word when node.IsLeaf:
                text.Append(node.Text);
                for (var letter = 0; letter < node.Text.Length; letter++) from.Add(new SourcePlace(at + letter, 1));
                return;

            case MarkdownKinds.Encoded:
                if (node.Fact(MarkdownRoles.Decoded)?.Held is not string decoded) return;

                text.Append(decoded);
                for (var letter = 0; letter < decoded.Length; letter++) from.Add(new SourcePlace(at, node.Width));
                return;
        }

        foreach (var child in node.Children) Worded(child, text, from);
    }

    /// <summary>A line between what was gathered and what comes next — one, however many things begin there.</summary>
    private static void Lined(StringBuilder text, List<SourcePlace> from)
    {
        if (text.Length == 0 || text[^1] == '\n') return;

        text.Append('\n');
        from.Add(Between);
    }

    /// <summary>
    /// Everywhere <paramref name="occurrences"/> finds something in what is drawn, as the stretch of source it was drawn
    /// for — in the order a reader comes to them, each once.
    /// </summary>
    public IReadOnlyList<(int Start, int Length)> Every(Func<string, IReadOnlyList<(int Index, int Length)>>? occurrences)
    {
        if (occurrences is null || Text.Length == 0) return [];

        var found = new List<(int Start, int Length)>();

        foreach (var (index, length) in occurrences(Text))
        {
            if (length <= 0 || index < 0 || index + length > Text.Length) continue;

            var start = _from[index].Start;
            var end = _from[index + length - 1].End;

            if (found.Count == 0 || found[^1].Start != start) found.Add((start, Math.Max(end - start, 0)));
        }

        return found;
    }

    /// <summary>The words drawn for any of the source from <paramref name="start"/> for <paramref name="length"/>.</summary>
    public string Within(int start, int length)
    {
        var end = start + length;
        var words = new StringBuilder();
        var between = false;

        for (var at = 0; at < Text.Length; at++)
        {
            var from = _from[at];

            // A line between two blocks belongs to neither; it is kept only between two words that are.
            if (from.Start < 0)
            {
                between |= words.Length > 0;

                continue;
            }

            if (from.Start >= end || from.End <= start) continue;

            if (between) words.Append('\n');
            between = false;
            words.Append(Text[at]);
        }

        return words.ToString();
    }

    /// <summary>The line of drawn words holding what was drawn for <paramref name="offset"/> of the source, tidied.</summary>
    public string LineAt(int offset)
    {
        for (var at = 0; at < Text.Length; at++)
        {
            var from = _from[at];
            if (from.Start < 0 || offset < from.Start || offset >= from.End) continue;

            var begins = Text.LastIndexOf('\n', at) + 1;
            var ends = Text.IndexOf('\n', at);

            return Text[begins..(ends < 0 ? Text.Length : ends)].Trim();
        }

        return string.Empty;
    }

    // ── Gathering it ────────────────────────────────────────────────────────

    /// <summary>A line between blocks, which stands for no source at all.</summary>
    private static readonly SourcePlace Between = new(-1, 0);
}
