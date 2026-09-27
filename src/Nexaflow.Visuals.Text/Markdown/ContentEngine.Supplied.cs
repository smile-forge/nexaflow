using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

using Nexaflow.Markdown.Ast;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Content a binding supplied (<see cref="Roles.Supplied"/>): read, not written.
///
/// <para>
/// <strong>Picked out whole.</strong> Nobody wrote it here, so none of it stands for any source — there is nowhere in it to
/// put the caret, and no stretch of characters a selection of it could be. What is picked out of it is whole pieces: the
/// node pressed, or every node a drag spans, kept here beside the stretches of source picked out, told to whoever follows
/// the content with them, washed like them and copied as the words drawn.
/// </para>
/// </summary>
public sealed partial class ContentEngine
{
    /// <summary>The pieces of supplied content picked out, in the order drawn.</summary>
    private IReadOnlyList<Piece> _picked = [];

    /// <summary>The pieces of supplied content picked out — what is washed besides the stretches of source picked out.</summary>
    internal IReadOnlyList<Piece> PickedWhole => _picked;

    /// <summary>The words drawn on what of supplied content is picked out, a line each — or null where none of it is.</summary>
    internal string? PickedText =>
        _picked.Count == 0 ? null : string.Join("\n", _picked.Select(Drawn).Where(words => words.Length > 0));

    /// <summary>
    /// Whether nothing may be written where the caret is: nobody may write at all, the caret stands in supplied content, or what
    /// is picked out is — which typing would otherwise replace.
    /// </summary>
    private bool Unwritable =>
        _readOnly || _picked.Count > 0 || (_at >= 0 && _at < _laid.Places.Count && Supplied(_laid.Places[_at].Against));

    /// <summary>Whether <paramref name="piece"/> was drawn from content a binding supplied.</summary>
    private static bool Supplied(Piece piece)
    {
        for (var at = piece; at.Exists; at = at.Parent)
            if (at.Part is ContentPart part) return part.Supplied;

        return false;
    }

    /// <summary>
    /// What picking out <paramref name="piece"/> picks out: the thing the drawing says choosing it means, and failing that the
    /// whole of what it was drawn in that was supplied — the box a label sits in, not a letter of the label.
    /// </summary>
    private static Piece Whole(Piece piece)
    {
        var whole = piece;

        for (var at = piece; at.Exists; at = at.Parent)
        {
            if (at.Acts?.Select is not null) return at;

            if (at.Part is not ContentPart part) continue;
            if (!part.Supplied) break;

            whole = at;
        }

        return whole;
    }

    /// <summary>Every whole piece of supplied content with words drawn on it inside <paramref name="where"/> — all of them, where that is null.</summary>
    private IEnumerable<Piece> Wholes(Rect? where = null) =>
        _laid.Root.SelfAndDescendants()
            .Where(piece => piece.Words is not null && Supplied(piece) && (where is not { } span || piece.Ink().IntersectsWith(span)))
            .Select(Whole)
            .Distinct();

    /// <summary>What a piece of supplied content is known by from one laying out to the next: what the drawing calls it, or its words.</summary>
    private static string Known(Piece piece) => piece.Acts?.Select?.Target ?? Drawn(piece);

    /// <summary>The words drawn on <paramref name="piece"/>, in the order drawn.</summary>
    private static string Drawn(Piece piece) =>
        string.Join(" ", piece.SelfAndDescendants().Select(inside => inside.Words?.Glyphs.Text).OfType<string>());

    /// <summary>Picks out <paramref name="pieces"/> of supplied content in place of whatever was picked out.</summary>
    private void PickWhole(IReadOnlyList<Piece> pieces)
    {
        if (_state.HasSelection) Apply(_state.Select(0, 0), notify: false);

        _picked = pieces;
        Chosen();
        Changed?.Invoke(this, false);
    }

    /// <summary>A press on supplied content: the piece pressed, picked out whole.</summary>
    private void PickPressed(Piece pressed)
    {
        _dragging = true;
        _moving = false;

        PickWhole(Whole(pressed) is { Exists: true } whole ? [whole] : []);
    }

    /// <summary>A drag from supplied content: every piece of it the drag spans, picked out whole.</summary>
    private void PickSpanned(Point at) => PickWhole([.. Wholes(new Rect(_pressedAt, at))]);

    /// <summary>A Ctrl press on supplied content: the piece added to what is picked out, or taken back out of it.</summary>
    private void TogglePicked(Piece pressed)
    {
        if (Whole(pressed) is not { Exists: true } whole) return;

        var picked = _picked.Contains(whole) ? _picked.Where(piece => piece != whole) : _picked.Append(whole);
        _picked = [.. picked];

        Chosen();
        Changed?.Invoke(this, false);
    }

    /// <summary>Lets go of what of supplied content was picked out. True where anything was.</summary>
    private bool Unpick()
    {
        if (_picked.Count == 0) return false;

        _picked = [];
        return true;
    }

    /// <summary>
    /// What was picked out of supplied content, found again in the content as it has just been laid out — by what it is known by,
    /// since the pieces themselves belong to the layout before.
    /// </summary>
    private void Repick()
    {
        if (_picked.Count == 0) return;

        var known = _picked.Select(Known).ToHashSet(StringComparer.Ordinal);
        _picked = [.. Wholes().Where(piece => known.Contains(Known(piece)))];
    }

    /// <summary>What of supplied content is picked out, as picks: the words drawn on each, and what the drawing calls it.</summary>
    private IEnumerable<ContentPick> PickedPicks() =>
        _picked.Select(piece =>
        {
            var (id, label) = Selecting(piece);
            var part = piece.Naming() as ContentPart;

            return new ContentPick(part?.Start ?? 0, 0, Drawn(piece), part, LanguageOf(piece), id, label);
        });
}
