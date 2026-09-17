using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>
/// Words a diagram sets, measured and not yet placed. They are one of three things, and which is the whole of what a reader
/// can do with them:
/// <list type="bullet">
/// <item>what somebody <b>wrote</b> — a label, a name, a value — which is typed into where it is drawn;</item>
/// <item>words <b>worked out</b> — a share, a total, the names a union overlaps — which stand for what they were worked out
/// from, and are pressed as it, but hold no caret;</item>
/// <item>the <b>hole</b> standing where something is still to be written, which is where typing it puts it.</item>
/// </list>
///
/// <para>
/// <strong>Every word a diagram draws is one of these</strong>, made by <see cref="MermaidBuilder.Written"/> or
/// <see cref="MermaidBuilder.Worked"/>. A builder measures them to lay its diagram out and sets them where they go; how they
/// behave was decided when they were made, and is never decided again.
/// </para>
/// </summary>
internal sealed class DiagramWords
{
    private readonly FormattedText _text;
    private readonly FormattedText _letter;
    private readonly bool _maps;
    private readonly bool _writes;

    internal DiagramWords(FormattedText text, ISourcePart? part, ISourcePart? hole, FormattedText letter, Brush ink, bool maps, bool writes)
    {
        _text = text;
        _letter = letter;
        _maps = maps;
        _writes = writes;
        Part = part;
        Hole = hole;
        Ink = ink;
    }

    /// <summary>What they say — empty for a hole.</summary>
    public string Says => Hole is null ? _text.Text : string.Empty;

    /// <summary>What they were written in, or stand for — what pressing them means. Null for words that stand for nothing.</summary>
    public ISourcePart? Part { get; }

    /// <summary>The hole they are, where nothing is written yet.</summary>
    public ISourcePart? Hole { get; }

    public Brush Ink { get; }

    /// <summary>How wide they are set: the words, or the hole.</summary>
    public double Width => Hole is null ? _text.Width : LayoutText.HoleWidth(_letter);

    /// <summary>How tall a line of them is set.</summary>
    public double Height => Math.Max(_text.Height, _letter.Height);

    /// <summary>How far below their top their baseline is — what words set side by side line up on.</summary>
    public double Baseline => Hole is null ? _text.Baseline : _letter.Baseline;

    /// <summary>Sets them with their top left at <paramref name="at"/>, as a piece of <paramref name="kind"/>.</summary>
    public void Set(LayoutBuilder build, Point at, string kind)
    {
        if (Hole is not null)
        {
            LayoutText.Hole(build, Hole, at, _letter, Ink);
            return;
        }

        LayoutText.Words(build, _text, at, _text.Width, TextAlignment.Left, Part, kind, maps: _maps, writes: _writes, ink: Ink);
    }

    /// <summary>
    /// Where each of <paramref name="lines"/> goes, set one under the other in <paramref name="room"/> — what
    /// <see cref="MermaidBuilder.Wrapped"/> hands back, set as the lines of one label: against the side
    /// <paramref name="align"/> says, and middling down the room.
    /// </summary>
    public static IEnumerable<(DiagramWords Words, Point At)> Stack(IReadOnlyList<DiagramWords> lines, Rect room,
                                                                    TextAlignment align = TextAlignment.Center)
    {
        var top = room.Top + ((room.Height - lines.Sum(line => line.Height)) / 2);

        foreach (var line in lines)
        {
            var left = align switch
            {
                TextAlignment.Right => room.Right - line.Width,
                TextAlignment.Center => room.Left + ((room.Width - line.Width) / 2),
                _ => room.Left,
            };

            yield return (line, new Point(left, top));
            top += line.Height;
        }
    }
}
