using System;
using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Prose;

/// <summary>
/// A markdown document as a kind of content: how its source is laid out, and which language an edit landed in.
///
/// <para>
/// <strong>Editing is shared, and a language may say otherwise for its own source.</strong> From the piece holding the
/// caret, up the layout to the first piece naming a part of the syntax tree, then up the tree to the first part another
/// language was written in — which its parser named (<see cref="ContentNested"/>). That
/// language's <see cref="IContentLanguage.OnEdit"/> is asked what the key means; where it says nothing, or has nothing to
/// say, the key does what a key does to the characters. No such part means the caret is in markdown's own source, and
/// markdown's rules answer (<see cref="MarkdownEdits"/>).
/// </para>
/// <para>
/// Whatever the edit came to, the whole document is read again from its source: a bracket or a brace typed anywhere can
/// change how everything after it nests, so nothing about the old reading is trusted with the new text.
/// </para>
/// </summary>
/// <param name="lay">Lays the state out at a width, told whether anybody can write in it — the builder, as the element asks for it.</param>
/// <param name="forget">What is told that something the source does not say has changed — see <see cref="Forget"/>.</param>
public sealed class MarkdownContent(Func<EditState, double, bool, Laid> lay, Action? forget = null) : IContent
{
    /// <summary>The ordinary case: a document drawn in one style, laid out by <paramref name="engine"/>.</summary>
    public static MarkdownContent Of(StyleFormat style, ContentEngine engine) =>
        new((state, room, readOnly) => engine.Lay(null, state, style, room, readOnly), engine.Forget);

    /// <inheritdoc/>
    public Laid Lay(EditState state, double room, bool readOnly) => lay(state, room, readOnly);

    /// <inheritdoc/>
    public void Forget() => forget?.Invoke();

    /// <inheritdoc/>
    public EditState? Typing(Landing landing, string text)
    {
        var aimed = Aimed(landing);

        return aimed.Hook?.Typing(aimed.Edit, text);
    }

    /// <inheritdoc/>
    public EditState Settle(Landing landing, string separator)
    {
        var aimed = Aimed(landing);

        return aimed.Hook?.Settling(aimed.Edit, separator) ?? landing.State.Write(separator);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The edges of another language's source are as far as a key taking back characters goes: past them is the delimiter
    /// that says the language is there, and taking it would turn the rest into something else. Where nothing is left
    /// inside, the whole of it goes, because there is nothing left to keep.
    /// </remarks>
    public EditState? Erasing(Landing landing, bool forward)
    {
        var aimed = Aimed(landing);
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

    /// <inheritdoc/>
    public EditState Edited(Landing before, EditState after)
    {
        var aimed = Aimed(before);
        if (aimed.Hook is not { } hook) return after;

        // Only an edit that stayed inside the language's own source is that language's to follow through.
        return aimed.Holder is null || Inside(aimed.Edit, before.State.Source, after.Source) ? hook.Edited(aimed.Edit, after) : after;
    }

    // ── Which language an edit landed in ────────────────────────────────────

    /// <summary>The language an edit landed in: what it says an edit means, where its source is, and the part holding it — null for markdown's own.</summary>
    private readonly record struct Aim(IOnEdit? Hook, ContentEdit Edit, ContentPart? Holder);

    /// <summary>
    /// From the caret up the layout, and from each part it names up the syntax tree, to the first part another language was
    /// written in whose source holds what is being edited — or the document itself, where there is none.
    /// </summary>
    private static Aim Aimed(Landing landing)
    {
        var state = landing.State;
        var (from, to) = state.HasSelection
            ? (state.Selection.Min(range => range.Start), state.Selection.Max(range => range.End))
            : (state.Caret, state.Caret);

        foreach (var named in Named(landing))
            foreach (var holder in ContentNested.Holders(named))
                if (ContentLanguages.For(ContentNested.Language(holder)) is { } language && holder.Part(Roles.Body) is { } body
                    && ContentNested.Own(body) is var (start, length) && start <= from && to <= start + length)
                    return new(language.Editing.OnEdit, new ContentEdit(landing, start, length), holder);

        return new(MarkdownEdits.Instance, new ContentEdit(landing, 0, state.Source.Length), null);
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
