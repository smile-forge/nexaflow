using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>One stretch of a document a selection can run through: a run of prose, or a rendered block.</summary>
/// <param name="Length">How much of it there is — characters of prose, or of the block's own source.</param>
/// <param name="IsBlock">Whether it is rendered content rather than prose.</param>
public readonly record struct DocumentPart(int Length, bool IsBlock);

/// <summary>
/// Where one end of a selection sits: which part of the document, and how far into it.
/// <para>
/// Deliberately a pair of numbers rather than anything the host understands. Ordering two ends of a
/// selection, working out which parts lie between them and how much of each is taken, is arithmetic — and
/// keeping it arithmetic is what lets it be exercised without a document, a window or a caret.
/// </para>
/// </summary>
public readonly record struct DocumentPoint(int Part, int Offset) : IComparable<DocumentPoint>
{
    public int CompareTo(DocumentPoint other) =>
        Part != other.Part ? Part.CompareTo(other.Part) : Offset.CompareTo(other.Offset);

    public static bool operator <(DocumentPoint a, DocumentPoint b) => a.CompareTo(b) < 0;
    public static bool operator >(DocumentPoint a, DocumentPoint b) => a.CompareTo(b) > 0;
    public static bool operator <=(DocumentPoint a, DocumentPoint b) => a.CompareTo(b) <= 0;
    public static bool operator >=(DocumentPoint a, DocumentPoint b) => a.CompareTo(b) >= 0;
}

/// <summary>How much of one part a selection takes.</summary>
public readonly record struct DocumentRange(int Part, int Start, int Length)
{
    public int End => Start + Length;
}

/// <summary>
/// A selection that runs across a document rather than inside one thing — from prose, through a formula,
/// out into more prose.
/// <para>
/// The rule a reader expects is simple and worth stating: whatever the selection <em>passes through</em>
/// is taken whole, and only the two ends are partial. A formula caught in the middle of a sweep is
/// selected entirely, however the pointer happened to cross it, because a selection that clipped it at
/// whatever pixel the mouse was travelling through would be a selection nobody asked for.
/// </para>
/// <para>
/// Built against parts rather than against blocks so that the same rules serve the score and the diagrams
/// when they adopt this: a document is an ordered run of things with lengths, and that is all this needs
/// to know about it.
/// </para>
/// </summary>
public sealed class DocumentSelection
{
    private DocumentSelection(DocumentPoint from, DocumentPoint to, IReadOnlyList<DocumentRange> ranges)
    {
        From = from;
        To = to;
        Ranges = ranges;
    }

    /// <summary>The earlier end, whichever way the drag went.</summary>
    public DocumentPoint From { get; }

    /// <summary>The later end.</summary>
    public DocumentPoint To { get; }

    /// <summary>How much of each part is taken, in document order. Parts taking nothing are left out.</summary>
    public IReadOnlyList<DocumentRange> Ranges { get; }

    public bool IsEmpty => Ranges.Count == 0;

    /// <summary>Nothing selected.</summary>
    public static DocumentSelection None { get; } =
        new(default, default, []);

    /// <summary>Whether this part is taken in its entirety — the question a block asks before washing itself.</summary>
    public bool Wholly(IReadOnlyList<DocumentPart> parts, int part) =>
        Ranges.Any(r => r.Part == part && r.Start == 0 && r.Length >= Length(parts, part));

    /// <summary>
    /// What a drag from <paramref name="anchor"/> to <paramref name="focus"/> selected.
    /// </summary>
    public static DocumentSelection Between(
        IReadOnlyList<DocumentPart> parts, DocumentPoint anchor, DocumentPoint focus)
    {
        if (parts.Count == 0) return None;

        var from = Clamp(parts, Min(anchor, focus));
        var to = Clamp(parts, Max(anchor, focus));
        if (from.CompareTo(to) >= 0) return new DocumentSelection(from, to, []);

        var ranges = new List<DocumentRange>();
        for (var part = from.Part; part <= to.Part; part++)
        {
            var start = part == from.Part ? from.Offset : 0;
            var end = part == to.Part ? to.Offset : Length(parts, part);
            if (end > start) ranges.Add(new DocumentRange(part, start, end - start));
        }

        return new DocumentSelection(from, to, ranges);
    }

    /// <summary>What is taken of one part, or nothing.</summary>
    public DocumentRange? Of(int part)
    {
        foreach (var range in Ranges)
            if (range.Part == part) return range;
        return null;
    }

    private static int Length(IReadOnlyList<DocumentPart> parts, int part) =>
        part >= 0 && part < parts.Count ? Math.Max(0, parts[part].Length) : 0;

    private static DocumentPoint Clamp(IReadOnlyList<DocumentPart> parts, DocumentPoint point)
    {
        var part = Math.Clamp(point.Part, 0, parts.Count - 1);
        return new DocumentPoint(part, Math.Clamp(point.Offset, 0, Length(parts, part)));
    }

    private static DocumentPoint Min(DocumentPoint a, DocumentPoint b) => a.CompareTo(b) <= 0 ? a : b;
    private static DocumentPoint Max(DocumentPoint a, DocumentPoint b) => a.CompareTo(b) >= 0 ? a : b;
}
