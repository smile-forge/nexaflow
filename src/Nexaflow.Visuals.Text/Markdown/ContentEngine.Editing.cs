using System;
using System.Collections.Generic;
using System.Linq;

using System.Windows;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// What a key means where it landed.
///
/// <para>
/// <strong>Asked of the language it landed in, and made here.</strong> From the piece the caret stands against, up the layout to
/// the first piece drawn from a part of a syntax tree, and from that part up its own tree to the root, which names the language
/// (<see cref="ContentEdit"/>). That language's <see cref="IContentLanguage.OnEdit"/> says what the edit is to be, and the engine
/// makes it. Where it says nothing the engine has an answer of its own for the keys aimed at the caret
/// (<see cref="Ordinary"/>), and where that has nothing to say either, the key does what a key does to the characters.
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
    internal static EditState? Edited(EditKind kind, string text, Landing landing, Piece from = default) =>
        Said(kind, text, landing, from) is { } change ? Made(landing.State, change) : null;

    /// <summary>
    /// The same, as the change itself rather than the content it comes to — for the one caller that has to see what the language
    /// said rather than only what it wrote: a language may ask for an edit of the engine's instead (<see cref="ContentChange.Asked"/>).
    /// </summary>
    private static ContentChange? Said(EditKind kind, string text, Landing landing, Piece from)
    {
        var edit = Walked(kind, text, landing, from);
        // Nothing on the way up named a part of any tree, so there is no language to ask.
        var language = edit.Part is null ? null : ContentLanguages.WrittenIn(edit.Root);
        var said = language?.Editing.OnEdit?.Edit(edit) ?? Ordinary(edit, language);

        return said is { Asked: not null } ? said
             : said is not null ? Spelling(said, landing.State, language!)
             : kind is EditKind.Erasing or EditKind.Deleting ? Edges(edit)
             : null;
    }

    /// <summary>
    /// <paramref name="change"/> with the words in it spelled as the language's own source spells them
    /// (<see cref="ITranspile"/>) — escaped, or denied where they cannot be written there at all.
    ///
    /// <para>
    /// Only what is words as the reader means them (<see cref="ContentWrite.Meant"/>) is asked about, whether a handler named it
    /// or the engine did: that is the one kind of write nobody has yet put into the language's own syntax. Everything else a
    /// handler answers is already source — it wrote it in its own language — and goes as it stands.
    /// </para>
    /// <para>
    /// A language whose parser says nothing about writing has nothing written for it: better a key that does nothing than
    /// characters spliced into a syntax that nothing has vouched for.
    /// </para>
    /// </summary>
    private static ContentChange Spelling(ContentChange change, EditState state, ContentLanguage language) =>
        !change.Writes.Any(write => write.Meant) ? change
        : language.Transpile?.Invoke(change) ?? ContentChange.Stay(state);

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
    /// The edit as the language it landed in is told it: the piece it applied to — <paramref name="from"/>, or else the one the caret
    /// stands against — the first part named on the way up the layout from it, and the root of that part's tree.
    /// </summary>
    private static ContentEdit Walked(EditKind kind, string text, Landing landing, Piece from = default)
    {
        var (piece, part, root) = Reached(landing, from);

        return new ContentEdit(kind, text, landing, piece, part, root);
    }

    /// <summary>
    /// What a gesture reached: the piece it applied to — <paramref name="from"/>, or else the one the caret stands against — the
    /// first part named on the way up the layout from it, and the root of that part's tree, which names the language.
    ///
    /// <para>
    /// Every tree a language reads is its own and only the layout is one tree, so the layout is the only way from one language to
    /// another. An edit and a move both find their language this way, which is why the walk is here rather than in either.
    /// </para>
    /// </summary>
    private static (Piece Piece, ContentPart? Part, ContentPart Root) Reached(Landing landing, Piece from)
    {
        var piece = from.Exists ? from : Against(landing);

        for (var up = piece; up.Exists; up = up.Parent)
            if (Drawn(up.Part) is { } part)
                return (piece, part, part.Ancestors().LastOrDefault() ?? part);

        return (piece, null, NothingRead);
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

        // Asking a language what it offers is running its code, on a right-click. One that falls over costs the reader what
        // that language would have added to the menu, not the window.
        try
        {
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
        catch
        {
            return [];
        }
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
    /// What a key comes to where the language it landed in said nothing — the ordinary answer, where that language takes text only
    /// in a run of words. A language that answers for its own keys is asked first and may ask for this itself.
    /// </summary>
    private static ContentChange? Ordinary(ContentEdit edit, ContentLanguage? language) =>
        language is { Editing.TakesTextOnlyInWords: true } ? OrdinaryEdits.Keyed(edit) : null;

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
