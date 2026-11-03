using System;
using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Editing;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Languages;

/// <summary>
/// What Enter and Tab mean in content written line by line — code, plain text — as they mean in a text editor: a line broken carries on
/// at the indent of the line it was broken from; Tab writes a tab at the caret, or over lines picked out indents each of them, leaving
/// them picked out so it can be pressed again; and Shift+Tab takes a level of indent — a tab, or as many as four spaces — off each line
/// it covers.
/// </summary>
internal sealed class LineIndenting : IOnEdit
{
    /// <summary>The one there need ever be: it holds nothing.</summary>
    public static LineIndenting Instance { get; } = new();

    /// <summary>How many spaces make a level of indent, for a line indented with spaces rather than a tab.</summary>
    private const int SpacesPerLevel = 4;

    public ContentChange? Edit(ContentEdit edit) => edit.Kind switch
    {
        EditKind.Settling when edit.Text == "\n" => Broken(edit.State),
        EditKind.Tabbing => Lines(edit.State) is { Count: > 1 } lines ? Indented(edit.State, lines) : ContentChange.Typed(edit.State, "\t"),
        EditKind.TabbingBack => Outdented(edit.State, Lines(edit.State)),
        _ => null,
    };

    /// <summary>A line break at the caret — over what is picked out — and after it the indent of the line it was broken from.</summary>
    private static ContentChange Broken(EditState state)
    {
        var document = state.Document;
        var from = state.HasSelection ? state.Selection.Min(range => range.Start) : state.Caret;
        var start = document.OffsetOf(document.LineOf(from));

        var indent = start;
        while (indent < from && document[indent] is ' ' or '\t') indent++;

        return ContentChange.Typed(state, "\n" + document.Slice(start, indent - start));
    }

    /// <summary>
    /// Where each line the caret, or what is picked out, covers starts — a stretch picked out to the start of a line not covering that
    /// line, which is what picking out whole lines leaves.
    /// </summary>
    private static List<int> Lines(EditState state)
    {
        var document = state.Document;
        var (low, high) = state.HasSelection
            ? (state.Selection.Min(range => range.Start), state.Selection.Max(range => range.End))
            : (state.Caret, state.Caret);

        var (first, last) = (document.LineOf(low), document.LineOf(high));
        if (last > first && document.OffsetOf(last) == high) last--;

        return [.. Enumerable.Range(first, last - first + 1).Select(document.OffsetOf)];
    }

    /// <summary>A tab at the start of each of <paramref name="lines"/>.</summary>
    private static ContentChange Indented(EditState state, List<int> lines) =>
        Shifted(state, [.. lines.Select(start => new ContentWrite(start, 0, "\t"))]);

    /// <summary>A level of indent off the start of each of <paramref name="lines"/> that has one: a tab, or as many as four spaces.</summary>
    private static ContentChange Outdented(EditState state, List<int> lines)
    {
        var document = state.Document;
        var writes = new List<ContentWrite>();

        foreach (var start in lines)
        {
            var taken = 0;
            if (start < document.Length && document[start] == '\t') taken = 1;
            else while (taken < SpacesPerLevel && start + taken < document.Length && document[start + taken] == ' ') taken++;

            if (taken > 0) writes.Add(new ContentWrite(start, taken, string.Empty));
        }

        // Nothing to take off is still the key taken: Shift+Tab in code is about its indent, not about moving on from it.
        return writes.Count == 0 ? ContentChange.Stay(state) : Shifted(state, writes);
    }

    /// <summary>
    /// <paramref name="writes"/>, in order down the document, with the caret and what is picked out carried through them — a stretch picked
    /// out from where a tab is written taking the tab in, so lines indented stay picked out whole.
    /// </summary>
    private static ContentChange Shifted(EditState state, IReadOnlyList<ContentWrite> writes)
    {
        int Moved(int offset, bool before)
        {
            var moved = offset;

            foreach (var write in writes)
            {
                if (write.Start > offset || (write.Start == offset && write.Length == 0 && before)) break;

                moved += write.Text.Length - Math.Min(write.Length, offset - write.Start);
            }

            return moved;
        }

        var picked = state.HasSelection
            ? state.Selection.Select(range =>
            {
                var start = Moved(range.Start, before: true);
                return new EditRange(start, Math.Max(0, Moved(range.End, before: false) - start));
            }).ToList()
            : null;

        return new ContentChange(writes, Moved(state.Caret, before: false)) { PicksOut = picked };
    }
}
