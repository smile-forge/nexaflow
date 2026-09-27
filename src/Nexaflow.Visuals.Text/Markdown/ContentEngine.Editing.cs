using System;
using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// What a key means where it landed.
///
/// <para>
/// <strong>Editing is shared, and a language may say otherwise for its own source.</strong> From the piece holding the caret,
/// up the layout to the first piece naming a part of the syntax tree, then up the tree to the first part another language was
/// written in — which its parser named (<see cref="ContentNested"/>). That language's <see cref="IContentLanguage.OnEdit"/> is
/// asked what the key means; where it says nothing, or has nothing to say, the key does what a key does to the characters. No
/// such part means the caret is in the content's own language, and that language's rules answer — markdown's
/// (<see cref="Prose.MarkdownEdits"/>) for a document.
/// </para>
/// <para>
/// Whatever the edit came to, the whole content is read again from its source: a bracket or a brace typed anywhere can change
/// how everything after it nests, so nothing about the old reading is trusted with the new text.
/// </para>
/// </summary>
public sealed partial class ContentEngine
{
    /// <summary>What writing <paramref name="text"/> means where it landed, in content written in <paramref name="named"/>; null leaves it to the element, which splices at the caret.</summary>
    public EditState? Typing(string? named, Landing landing, string text)
    {
        var aimed = Aimed(named, landing);

        return aimed.Hook?.Typing(aimed.Edit, text);
    }

    /// <summary>What ending whatever is half-written means — Space and Enter both arrive here.</summary>
    public EditState Settle(string? named, Landing landing, string separator)
    {
        var aimed = Aimed(named, landing);

        return aimed.Hook?.Settling(aimed.Edit, separator) ?? landing.State.Write(separator);
    }

    /// <summary>
    /// What taking back the character on one side of the caret means; null leaves it to the element.
    ///
    /// <para>
    /// The edges of another language's source are as far as a key taking back characters goes: past them is the delimiter that
    /// says the language is there, and taking it would turn the rest into something else. Where nothing is left inside, the
    /// whole of it goes, because there is nothing left to keep.
    /// </para>
    /// </summary>
    /// <param name="forward">Delete rather than backspace.</param>
    public EditState? Erasing(string? named, Landing landing, bool forward)
    {
        var aimed = Aimed(named, landing);
        if (aimed.Hook?.Erasing(aimed.Edit, forward) is { } erased) return erased;

        var state = landing.State;
        if (aimed.Holder is not { } holder || state.HasSelection) return null;

        var inside = aimed.Edit.Source;
        if (string.IsNullOrWhiteSpace(inside))
            return state with
            {
                Source = state.Source.Remove(holder.Start, holder.Length),
                Caret = holder.Start,
                Selected = [],
                Raw = null,
            };

        var first = aimed.Edit.Start + (inside.Length - inside.TrimStart().Length);
        var last = aimed.Edit.End - (inside.Length - inside.TrimEnd().Length);

        return (forward ? state.Caret >= last : state.Caret <= first) ? state : null;
    }

    /// <summary>
    /// The last word on an edit once it is made, however it was made — typed, erased, pasted or dragged: what the language it
    /// landed in makes of it, such as a rename carried to every use.
    /// </summary>
    /// <param name="before">Where the edit landed — the state it was made to, and what was drawn of it.</param>
    /// <param name="after">The state it made.</param>
    public EditState Edited(string? named, Landing before, EditState after)
    {
        var aimed = Aimed(named, before);
        if (aimed.Hook is not { } hook) return after;

        // Only an edit that stayed inside the language's own source is that language's to follow through.
        return aimed.Holder is null || Inside(aimed.Edit, before.State.Source, after.Source) ? hook.Edited(aimed.Edit, after) : after;
    }

    /// <summary>The language an edit landed in: what it says an edit means, where its source is, and the part holding it — null for the content's own.</summary>
    private readonly record struct Aim(IOnEdit? Hook, ContentEdit Edit, ContentPart? Holder);

    /// <summary>
    /// From the caret up the layout, and from each part it names up the syntax tree, to the first part another language was
    /// written in whose source holds what is being edited — or the content itself, where there is none.
    /// </summary>
    private static Aim Aimed(string? named, Landing landing)
    {
        var state = landing.State;
        var (from, to) = state.HasSelection
            ? (state.Selection.Min(range => range.Start), state.Selection.Max(range => range.End))
            : (state.Caret, state.Caret);

        foreach (var part in Named(landing))
            foreach (var holder in ContentNested.Holders(part))
                if (ContentLanguages.For(ContentNested.Language(holder)) is { } language && holder.Part(Roles.Body) is { } body
                    && ContentNested.Own(body) is var (start, length) && start <= from && to <= start + length)
                    return new(language.Editing.OnEdit, new ContentEdit(landing, start, length), holder);

        return new(Language(named).Editing.OnEdit, new ContentEdit(landing, 0, state.Source.Length), null);
    }

    /// <summary>The part of the syntax tree the caret stands in: the first one named on the way up the layout from it.</summary>
    internal static ContentPart? Standing(Landing landing) => Named(landing).FirstOrDefault();

    /// <summary>
    /// The parts of the syntax tree the caret could be standing in, innermost first: every part drawn over what is being
    /// edited, smallest first; of two the same size the one further in, and then the one on the way up the layout from the caret.
    ///
    /// <para>
    /// Innermost rather than first on the way up, because not everything is drawn inside the piece standing for its block:
    /// a fence no language could read is its characters drawn beside the fence's piece, and the way up from them passes
    /// straight to the document. Which block an offset is in is a question about the characters, whatever was drawn.
    /// </para>
    /// </summary>
    private static IEnumerable<ContentPart> Named(Landing landing)
    {
        var state = landing.State;
        var (from, to) = state.HasSelection
            ? (state.Selection.Min(range => range.Start), state.Selection.Max(range => range.End))
            : (state.Caret, state.Caret);

        var near = new HashSet<ContentPart>();
        for (var piece = Piece(landing); piece.Exists; piece = piece.Parent)
            if (piece.Part is ContentPart part) near.Add(part);

        return landing.Laid.Root.SelfAndDescendants()
            .Select(piece => piece.Part as ContentPart)
            .OfType<ContentPart>()
            .Where(part => part.Start <= from && to <= part.End)
            .Distinct()
            .OrderBy(part => part.Length)
            .ThenByDescending(part => part.Ancestors().Count())
            .ThenBy(part => near.Contains(part) ? 0 : 1);
    }

    /// <summary>The piece the caret stands against — the place it was put at, or the words at its offset.</summary>
    private static Piece Piece(Landing landing)
    {
        var laid = landing.Laid;
        if (landing.At >= 0 && landing.At < laid.Places.Count) return laid.Places[landing.At].Against;

        return laid.Root.WordsAt(landing.State.Caret) is { Exists: true } words ? words : laid.Root;
    }

    /// <summary>Whether everything an edit changed lies inside the stretch it is being told to.</summary>
    private static bool Inside(ContentEdit edit, string before, string after)
    {
        var shorter = Math.Min(before.Length, after.Length);

        var start = 0;
        while (start < shorter && before[start] == after[start]) start++;

        var same = 0;
        while (same < shorter - start && before[before.Length - 1 - same] == after[after.Length - 1 - same]) same++;

        return start >= edit.Start && before.Length - same <= edit.End;
    }
}
