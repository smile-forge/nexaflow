using System;
using System.Collections.Generic;
using System.Linq;

using System.Windows;
using Nexaflow.Markdown.Ast;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// What a key means where it landed.
///
/// <para>
/// <strong>Asked of the language it landed in, and made here.</strong> From the piece the caret stands against, up the layout to
/// the first piece drawn from a part of a syntax tree, and from that part up its own tree to the root, which names the language
/// (<see cref="ContentEdit"/>). That language's <see cref="IContentLanguage.OnEdit"/> says what the edit is to be, and the engine
/// makes it. Where it says nothing, or has nothing to say, the key does what a key does to the characters.
/// </para>
/// <para>
/// Whatever the edit came to, the whole content is read again from its source: a bracket or a brace typed anywhere can change
/// how everything after it nests, so nothing about the old reading is trusted with the new text.
/// </para>
/// </summary>
public sealed partial class ContentEngine
{
    /// <summary>
    /// What a key doing <paramref name="kind"/>, writing <paramref name="text"/>, comes to where it landed — or null, which leaves
    /// it to be done as the key does anywhere.
    ///
    /// <para>
    /// A key taking back characters goes as far as the edges of content written in another language and no further: past them is
    /// what says the language is there, and taking it would turn the rest into something else. Where nothing is left inside, the
    /// whole of it goes, because there is nothing left to keep.
    /// </para>
    /// </summary>
    /// <param name="from">The piece the edit applied to, where it is not the one the caret stands against — a right-click's.</param>
    internal static EditState? Edited(EditKind kind, string text, Landing landing, Piece from = default)
    {
        var edit = Walked(kind, text, landing, from);
        // Nothing on the way up named a part of any tree, so there is no language to ask.
        var language = edit.Part is null ? null : ContentLanguages.WrittenIn(edit.Root);
        var said = language?.Editing.OnEdit?.Edit(edit) is { } answer ? Safe(answer, landing.State, language) : null;
        var change = said ?? (kind is EditKind.Erasing or EditKind.Deleting ? Edges(edit) : null);

        return change is null ? null : Made(landing.State, change);
    }

    /// <summary>
    /// What <paramref name="change"/> makes of <paramref name="state"/>: every stretch written — the last first, so each is still
    /// where it was named — and nothing picked out. A change writing nothing leaves what is picked out where it is, unless it moves
    /// the caret, which puts down what was picked out as a caret moved anywhere does.
    /// </summary>
    internal static EditState Made(EditState state, ContentChange change)
    {
        if (change.Writes.Count == 0)
        {
            var caret = Math.Clamp(change.Caret, 0, state.Source.Length);
            return state with { Caret = caret, Raw = change.Raw, Selected = caret == state.Caret ? state.Selected : [] };
        }

        var source = state.Source;
        foreach (var write in change.Writes.OrderByDescending(write => write.Start))
            source = string.Concat(source.AsSpan(0, write.Start), write.Text, source.AsSpan(write.End));

        return new EditState(source, Math.Clamp(change.Caret, 0, source.Length), [], change.Raw);
    }

    /// <summary>
    /// <paramref name="change"/> with every stretch it names as words (<see cref="ContentWrite.Words"/>) made safe for the part they go
    /// in by the language's parser, and the caret kept where it stood among them — or nothing changed at all, where any of them is
    /// something its part cannot hold.
    /// </summary>
    private static ContentChange Safe(ContentChange change, EditState state, ContentLanguage language)
    {
        if (language.SafeFormatText is not { } safe || !change.Writes.Any(write => write.Meant)) return change;

        var writes = new List<ContentWrite>();
        var caret = change.Caret;
        var moved = 0;
        var grown = 0;

        foreach (var write in change.Writes.OrderBy(write => write.Start))
        {
            // Where the write stands in the document as the handler said it would read afterwards, and how far it moves what follows.
            var at = write.Start + moved;
            moved += write.Text.Length - write.Length;

            if (!write.Meant)
            {
                writes.Add(write);
                continue;
            }

            if (safe(write.Part!, write.Text) is not { } written) return ContentChange.Stay(state);

            writes.Add(new ContentWrite(write.Start, write.Length, written));

            if (change.Caret >= at && change.Caret <= at + write.Text.Length)
                caret = at + grown + (safe(write.Part!, write.Text[..(change.Caret - at)])?.Length ?? written.Length);
            else if (change.Caret > at + write.Text.Length)
                caret += written.Length - write.Text.Length;

            grown += written.Length - write.Text.Length;
        }

        return change with { Writes = writes, Caret = caret };
    }

    /// <summary>
    /// The edit as the language it landed in is told it: the piece it applied to — <paramref name="from"/>, or else the one the caret
    /// stands against — the first part named on the way up the layout from it, and the root of that part's tree.
    /// </summary>
    private static ContentEdit Walked(EditKind kind, string text, Landing landing, Piece from = default)
    {
        var piece = from.Exists ? from : Against(landing);

        for (var up = piece; up.Exists; up = up.Parent)
            if (Drawn(up.Part) is { } part)
                return new ContentEdit(kind, text, landing, piece, part, part.Ancestors().LastOrDefault() ?? part);

        return new ContentEdit(kind, text, landing, piece, null, NothingRead);
    }

    /// <summary>
    /// What the language drawn at <paramref name="at"/> offers there — what a right-click opens: the same walks a key makes, from
    /// the piece under the point to its part and that part's root, and that language's <see cref="IContentLanguage.Offers"/> asked.
    /// </summary>
    internal IReadOnlyList<LayoutIntent> Asked(Point at)
    {
        var edit = Walked(EditKind.Choosing, string.Empty, Landing, _laid.Root.PieceAt(at));
        if (edit.Part is null || ContentLanguages.WrittenIn(edit.Root) is not { } language) return [];

        var state = edit.State;
        var chosen = state.HasSelection && state.SelectionStart >= edit.Start && state.SelectionStart + state.SelectionLength <= edit.End
            ? (state.SelectionStart - edit.Start, state.SelectionLength)
            : ((int, int)?)null;

        return language.Editing.Offers(new ContentAsk(edit.Root.Node is BlockNode block ? block.Language : string.Empty, edit.Source)
        {
            Part = edit.Part,
            Piece = edit.Piece,
            Root = edit.Root,
            Chosen = chosen,
            Caret = state.HasSelection ? null : state.Caret,
            IsReadOnly = _readOnly,
        });
    }

    /// <summary>
    /// The piece the caret stands against: the place it was put at, the words at its offset, or else the innermost piece drawn
    /// from a stretch holding it.
    /// </summary>
    private static Piece Against(Landing landing)
    {
        var laid = landing.Laid;
        if (landing.At >= 0 && landing.At < laid.Places.Count) return laid.Places[landing.At].Against;

        var caret = landing.State.Caret;
        if (laid.Root.WordsAt(caret) is { Exists: true } words) return words;

        return laid.Root.SelfAndDescendants()
            .Where(piece => piece.Part is { } part && part.Start <= caret && caret <= part.End())
            .DefaultIfEmpty(laid.Root)
            .MaxBy(piece => piece.Depth);
    }

    /// <summary>The part of a syntax tree a piece was drawn from: its own, the one it stands for, or the first of a run's.</summary>
    private static ContentPart? Drawn(ISourcePart? part) => part switch
    {
        ContentPart drawn => drawn,
        IStandsFor standing => Drawn(standing.Of),
        PartRun run => run.Parts.Select(Drawn).FirstOrDefault(drawn => drawn is not null),
        _ => null,
    };

    /// <summary>What a key taking back characters means at the edges of content written in another language — null anywhere else.</summary>
    private static ContentChange? Edges(ContentEdit edit)
    {
        var state = edit.State;
        if (state.HasSelection || Holder(edit) is not { } holder || holder.Part(Roles.Body) is not { } body) return null;

        var (start, length) = ContentNested.Own(body);
        var inside = state.Source.Substring(start, length);
        if (string.IsNullOrWhiteSpace(inside)) return ContentChange.Write(holder.Start, holder.Length, string.Empty);

        var first = start + (inside.Length - inside.TrimStart().Length);
        var last = start + length - (inside.Length - inside.TrimEnd().Length);

        return (edit.Kind == EditKind.Deleting ? state.Caret >= last : state.Caret <= first) ? ContentChange.Stay(state) : null;
    }

    /// <summary>
    /// The first part on the way up from the caret — up the layout, and up each part's own tree — holding content in another
    /// language whose characters hold the caret.
    /// </summary>
    private static ContentPart? Holder(ContentEdit edit)
    {
        var caret = edit.State.Caret;

        for (var up = edit.Piece; up.Exists; up = up.Parent)
            if (Drawn(up.Part) is { } part)
                foreach (var holder in ContentNested.Holders(part))
                    if (holder.Part(Roles.Body) is { } body && ContentNested.Holds(body, caret, caret))
                        return holder;

        return null;
    }
}
