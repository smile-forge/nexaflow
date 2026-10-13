using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// What a page reads as where that is what it draws rather than what was written (<see cref="IContentLanguage.ReadsAsDrawn"/>):
/// its words in the order a reader comes to them, a line between two paragraphs or two cells, and every character with the
/// stretch of source it was drawn for — so a search finds what a reader sees and nothing of the machinery around it, a copy
/// carries the words, and whatever is found is still a place in the source the page can show.
///
/// <para>
/// <strong>A run drawn over several lines is one run.</strong> Its lines are pieces standing for the same part, met in
/// the order they are read, so each starts where the one before it in that part stopped — which is what puts every letter
/// of a wrapped word back at its own offset. A piece drawn as something other than what was written (a decoded character,
/// a word set in capitals) stands for its whole part, letter by letter.
/// </para>
/// </summary>
public sealed class DrawnText
{
    private static readonly ConditionalWeakTable<Laid, DrawnText> Read = new();

    private readonly SourcePlace[] _from;

    private DrawnText(string text, SourcePlace[] from)
    {
        Text = text;
        _from = from;
    }

    /// <summary>The page's words, as a reader reads them.</summary>
    public string Text { get; }

    /// <summary>What <paramref name="laid"/> reads as — worked out once for each laying out.</summary>
    public static DrawnText Of(Laid laid) => Read.GetValue(laid, Gathered);

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

    private static DrawnText Gathered(Laid laid)
    {
        var text = new StringBuilder();
        var from = new List<SourcePlace>();

        // How far into each part its pieces have been read, so each line of a run starts where the last one stopped.
        var read = new Dictionary<ISourcePart, int>(ReferenceEqualityComparer.Instance);
        object? block = null;

        foreach (var piece in laid.Root.SelfAndDescendants())
        {
            if (piece.Words is not { } words || piece.Part is not { } part) continue;

            var drawn = words.Glyphs.Text;
            if (drawn.Length == 0) continue;

            var holder = Block(part);
            if (block is not null && !ReferenceEquals(holder, block) && text.Length > 0)
            {
                text.Append('\n');
                from.Add(Between);
            }

            block = holder;

            if (words.Maps)
            {
                read.TryGetValue(part, out var already);

                for (var at = 0; at < drawn.Length; at++)
                    from.Add(new SourcePlace(Math.Min(part.Start + already + at, part.Start + part.Length), 1));

                read[part] = already + drawn.Length;
            }
            else
            {
                for (var at = 0; at < drawn.Length; at++) from.Add(new SourcePlace(part.Start, part.Length));
            }

            text.Append(drawn);
        }

        return new DrawnText(text.ToString(), [.. from]);
    }

    /// <summary>The paragraph or cell a part was drawn in — what a line between two runs of words is decided by.</summary>
    private static object Block(ISourcePart part)
    {
        if (part is not ContentPart content) return part;

        for (var at = content; at is not null; at = at.Parent)
            if (at.Kind is MarkdownKinds.Paragraph or MarkdownKinds.Cell or MarkdownKinds.Heading or MarkdownKinds.Item)
                return at;

        return content.Ancestors().LastOrDefault() ?? content;
    }
}
