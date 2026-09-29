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
    internal static EditState? Edited(EditKind kind, string text, Landing landing, Piece from = default)
    {
        var edit = Walked(kind, text, landing, from);
        // Nothing on the way up named a part of any tree, so there is no language to ask.
        var language = edit.Part is null ? null : ContentLanguages.WrittenIn(edit.Root);
        var said = language?.Editing.OnEdit?.Edit(edit) ?? Ordinary(edit, language);

        var change = said is not null ? Spelling(said, landing.State, language!)
                   : kind is EditKind.Erasing or EditKind.Deleting ? Edges(edit)
                   : null;

        return change is null ? null : Made(landing.State, change);
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
    /// What a key aimed at the caret comes to in a language that is written in only inside its runs of words
    /// (<see cref="IContentLanguage.TakesTextOnlyInWords"/>) — and null for every other language, which leaves the key to do
    /// what it does to the characters.
    ///
    /// <para>
    /// Three of them are refused, because what the caret is written in cannot hold what they would do. A key taking back the
    /// character before its start, or after its end, would take the syntax that makes it what it is: the quote that opens it,
    /// the bracket that closes it, the comma before the next thing. A line break cannot go in one at all — where a diagram
    /// wants Enter to mean something, that is its own handler's to say, and this runs only where it said nothing.
    /// </para>
    /// <para>
    /// What is left is text going in, and it goes in as the reader meant it
    /// (<see cref="ContentWrite.Words(ContentPart, int, int, string)"/>): the language's parser is given it and makes it safe for
    /// that run before it is written, and writes nothing where it cannot be. That is the whole of the default — the language
    /// spells it, and the engine only says where.
    /// </para>
    /// </summary>
    private static ContentChange? Ordinary(ContentEdit edit, ContentLanguage? language)
    {
        if (language is not { Editing.TakesTextOnlyInWords: true }) return null;

        var state = edit.State;
        if (state.HasSelection || edit.Kind is not (EditKind.Typing or EditKind.Settling or EditKind.Breaking
                                                    or EditKind.Erasing or EditKind.Deleting)) return null;

        // Written in nothing this knows about: an identifier, a date, a setting. Each has a rule of its own and none of them
        // is here, so the key does what it did before anything here had an answer.
        if (Written(edit) is not { } words) return null;

        var caret = state.Caret;
        var breaking = edit.Kind is EditKind.Breaking || (edit.Kind is EditKind.Settling && edit.Text == "\n");

        // Nothing is written in it, so a key taking a character back cannot be taking one of its own — and what an empty
        // thing does when a reader backs into it is that diagram's business, not this one's.
        var holds = words.Length > 0;

        if (breaking
            || (holds && edit.Kind is EditKind.Erasing && caret <= words.Start)
            || (holds && edit.Kind is EditKind.Deleting && caret >= words.End)) return ContentChange.Stay(state);

        // Inside the run, so the character taken is one of its own.
        if (edit.Kind is EditKind.Erasing) return ContentChange.Write(caret - 1, 1, string.Empty, caret - 1);
        if (edit.Kind is EditKind.Deleting) return ContentChange.Write(caret, 1, string.Empty, caret);

        return new ContentChange([ContentWrite.Words(words, caret, 0, edit.Text)], caret + edit.Text.Length, state.Raw);
    }

    /// <summary>
    /// What the caret is written in — a run of words or a number — or null.
    ///
    /// <para>
    /// Asked of the tree and not of the layout, which is the only place the question has an answer. A layout is a picture of
    /// what the source meant: it draws a piece for a connector and a rule as readily as for a word, gives each of them a stop
    /// the caret can rest at, and is allowed to draw a piece from nothing written at all. What was written where the caret is
    /// standing is a fact about the source, and the tree is what holds the source.
    /// </para>
    /// <para>
    /// Innermost, because these nest: a name inside a label inside a line. The shortest one holding the caret is the one a
    /// reader is writing in.
    /// </para>
    /// </summary>
    private static ContentPart? Written(ContentEdit edit)
    {
        ContentPart? found = null;
        var caret = edit.State.Caret;

        foreach (var part in edit.Root.SelfAndDescendants())
        {
            if (part.Kind is not (Kinds.Words or Kinds.Number) || part.Derived || part.Supplied) continue;
            if (caret < part.Start || caret > part.End) continue;
            if (found is null || part.Length < found.Length) found = part;
        }

        return found;
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
