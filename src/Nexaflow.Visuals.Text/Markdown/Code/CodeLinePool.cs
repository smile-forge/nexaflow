using System;
using System.Collections.Generic;
using System.Threading;

using Nexaflow.Markdown.Ast;
using Nexaflow.Syntax;

namespace Nexaflow.Visuals.Text.Markdown.Code;

/// <summary>
/// The lines a code reading was last cut into (<see cref="CodeParser.InLines"/>), each by what it says and what it is coloured as — so
/// a line written and coloured as one before it is that line again, moved along to where it now stands, rather than cut and coloured
/// afresh. A keystroke makes the line it was typed on, and the colours landing for it make only the lines they changed.
/// </summary>
internal sealed class CodeLinePool
{
    /// <summary>How many lines are kept before what is kept is let go and kept again from what comes next: a few long units' worth.</summary>
    private const int Most = 1 << 16;

    private readonly Lock _gate = new();

    /// <summary>Each line kept, by a number for what it says.</summary>
    private readonly Dictionary<int, List<Kept>> _lines = [];

    private int _count;

    /// <summary>
    /// The line kept that says <paramref name="text"/> coloured as <paramref name="spans"/> — counted from its start — standing at
    /// <paramref name="at"/>; or null where none is.
    /// </summary>
    public ContentNode? Line(ReadOnlySpan<char> text, ReadOnlySpan<HighlightSpan> spans, int at)
    {
        lock (_gate)
        {
            if (!_lines.TryGetValue(string.GetHashCode(text), out var alike)) return null;

            var found = -1;

            for (var index = 0; index < alike.Count; index++)
            {
                if (!alike[index].Text.AsSpan().SequenceEqual(text) || !alike[index].Spans.AsSpan().SequenceEqual(spans)) continue;

                found = index;
                break;
            }

            if (found < 0) return null;

            // Kept where it now stands, so the next asking finds it there.
            var (said, colours, line, was) = alike[found];
            if (was == at) return line;

            var moved = line.FurtherOn(at - was);
            alike[found] = new Kept(said, colours, moved, at);

            return moved;
        }
    }

    /// <summary>
    /// Keeps <paramref name="line"/>, which says <paramref name="text"/> coloured as <paramref name="spans"/>, standing at <paramref name="at"/> —
    /// in place of any kept saying the same, so what is kept is a line for each thing said, where it last stood.
    /// </summary>
    public void Keep(string text, HighlightSpan[] spans, ContentNode line, int at)
    {
        lock (_gate)
        {
            if (_count >= Most)
            {
                _lines.Clear();
                _count = 0;
            }

            var hash = string.GetHashCode(text.AsSpan());
            if (!_lines.TryGetValue(hash, out var alike)) _lines[hash] = alike = [];

            for (var index = 0; index < alike.Count; index++)
                if (alike[index].Text == text && alike[index].Spans.AsSpan().SequenceEqual(spans))
                {
                    alike[index] = new Kept(text, spans, line, at);
                    return;
                }

            alike.Add(new Kept(text, spans, line, at));
            _count++;
        }
    }

    /// <summary>A line kept: what it says, what it is coloured as, the line, and where it stood.</summary>
    private readonly record struct Kept(string Text, HighlightSpan[] Spans, ContentNode Line, int At);
}
