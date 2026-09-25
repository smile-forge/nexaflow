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

    /// <summary>Another content written where these words are, drawn instead of them.</summary>
    private readonly ContentInset? _inset;

    /// <param name="inset">
    /// Another content written where these words are — a tune, a formula, a molecule. Where there is one, it is what is
    /// measured and what is set down, and the words are only what it was written as.
    /// </param>
    internal DiagramWords(FormattedText text, ISourcePart? part, ISourcePart? hole, FormattedText letter, Brush ink,
                          bool maps, bool writes, ContentInset? inset = null)
    {
        _text = text;
        _letter = letter;
        _maps = maps;
        _writes = writes;
        _inset = inset;
        Part = part;
        Hole = hole;
        Ink = ink;
    }

    /// <summary>What they say — empty for a hole.</summary>
    public string Says => Hole is null ? _text.Text : string.Empty;

    /// <summary>Whether these are a whole other content rather than words — which nothing may break, wrap or turn.</summary>
    public bool Nested => _inset is not null;

    /// <summary>What they were written in, or stand for — what pressing them means. Null for words that stand for nothing.</summary>
    public ISourcePart? Part { get; }

    /// <summary>The hole they are, where nothing is written yet.</summary>
    public ISourcePart? Hole { get; }

    public Brush Ink { get; }

    /// <summary>
    /// The same words drawn in <paramref name="ink"/> — set once, since what colour they are is only said as they are drawn,
    /// so a name measured in one colour to see whether it fits and drawn in another where it does not is shaped the once.
    /// </summary>
    public DiagramWords In(Brush ink) =>
        ReferenceEquals(ink, Ink) ? this : new DiagramWords(_text, Part, Hole, _letter, ink, _maps, _writes, _inset);

    /// <summary>How wide they are set: what is written there, the words, or the hole.</summary>
    public double Width => _inset?.Width ?? (Hole is null ? _text.Width : LayoutText.HoleWidth(_letter));

    /// <summary>How tall a line of them is set.</summary>
    public double Height => _inset?.Height ?? Math.Max(_text.Height, _letter.Height);

    /// <summary>How far below their top their baseline is — what words set side by side line up on.</summary>
    /// <remarks>Content written where words go sits on its own foot, having no line of text to share one with.</remarks>
    public double Baseline => _inset is { } inset ? inset.Height : Hole is null ? _text.Baseline : _letter.Baseline;

    /// <summary>Sets them with their top left at <paramref name="at"/>, as a piece of <paramref name="kind"/>.</summary>
    /// <param name="degrees">How far the words are turned about where they start — nought for level words, -90 for words read upward.</param>
    public void Set(LayoutBuilder build, Point at, string kind, double degrees = 0)
    {
        // Content written where words go is set down as itself, whichever way the words round it would have read: a
        // tune turned on its side is not a tune.
        if (_inset is { } inset)
        {
            inset.Set(build, at, MermaidPiece.Nested);
            return;
        }

        if (Hole is not null)
        {
            LayoutText.Hole(build, Hole, at, _letter, Ink);
            return;
        }

        // A line broken to fit keeps the room it was broken in, and the lines it was broken into stay set in the middle of one another.
        LayoutText.Words(build, _text, at, _text.MaxTextWidth > 0 ? _text.MaxTextWidth : _text.Width, _text.TextAlignment, Part, kind,
                         maps: _maps, writes: _writes, ink: Ink, degrees: degrees);
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

    /// <summary>
    /// The lines stacked in <paramref name="room"/>, each as a piece of <paramref name="kind"/> — what a shape is drawn with
    /// (<see cref="DiagramShapes.Draw(LayoutBuilder, string, ISourcePart, DiagramShape, Rect, Brush, DiagramStroke, IReadOnlyList{ValueTuple{DiagramWords, Point, string}}, Geometry)"/>).
    /// </summary>
    public static IReadOnlyList<(DiagramWords Words, Point At, string Kind)> Placed(
        IReadOnlyList<DiagramWords> lines, Rect room, string kind, TextAlignment align = TextAlignment.Center) =>
        [.. Stack(lines, room, align).Select(line => (line.Words, line.At, kind))];

    /// <summary>How much room the lines take stacked: as wide as the widest of them, and as tall as all of them together.</summary>
    public static Size Taken(IReadOnlyList<DiagramWords> lines) =>
        new(lines.Select(line => line.Width).DefaultIfEmpty(0).Max(), lines.Sum(line => line.Height));
}
