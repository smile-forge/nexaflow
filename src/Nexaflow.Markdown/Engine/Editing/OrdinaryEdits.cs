using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// What a key comes to where the language it landed in says nothing of its own — the answer the engine makes, offered to a
/// language's own handler so that saying "the usual thing, and also this" costs a line rather than a copy of it.
///
/// <para>
/// <strong>Offered, never fallen back to.</strong> A handler that answered for a key has answered for it; one that wants the
/// usual answer asks for it and says so. The common case is "the usual thing, except where z is written", and an engine that
/// quietly merged its own answer into a handler's would make that the hard case to write — so the asking is explicit, and a
/// handler stays free to take the answer apart, add to it, or refuse it outright.
/// </para>
/// </summary>
public static class OrdinaryEdits
{
    /// <summary>
    /// The run of words or figures the caret stands in, which is what an ordinary key writes into — the innermost, where runs
    /// hold one another. Null where the caret stands in none, which is where a language has a rule of its own: an identifier, a
    /// date, a setting.
    /// </summary>
    public static ContentPart? Written(ContentEdit edit)
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
    /// What the key comes to in the run the caret stands in: the character written where the caret is, or the one either side of
    /// it taken back. Null where there is no ordinary answer — the caret stands in no run, something is picked out, or the key
    /// is one that writes nothing.
    /// </summary>
    public static ContentChange? Keyed(ContentEdit edit)
    {
        var state = edit.State;

        if (state.HasSelection || edit.Kind is not (EditKind.Typing or EditKind.Settling or EditKind.Breaking
                                                    or EditKind.Erasing or EditKind.Deleting)) return null;

        // Written in nothing this knows about: an identifier, a date, a setting. Each has a rule of its own and none of them
        // is here, so the key does what it did before anything here had an answer.
        if (Written(edit) is not { } words) return null;

        var caret = state.Caret;
        var breaking = edit.Kind is EditKind.Breaking || (edit.Kind is EditKind.Settling && edit.Text == "\n");

        // Nothing is written in it, so a key taking a character back cannot be taking one of its own — and what an empty
        // thing does when a reader backs into it is that language's business, not this one's.
        var holds = words.Length > 0;

        if (breaking
            || (holds && edit.Kind is EditKind.Erasing && caret <= words.Start)
            || (holds && edit.Kind is EditKind.Deleting && caret >= words.End)) return ContentChange.Stay(state);

        // Inside the run, so the character taken is one of its own.
        if (edit.Kind is EditKind.Erasing) return ContentChange.Write(caret - 1, 1, string.Empty, caret - 1);
        if (edit.Kind is EditKind.Deleting) return ContentChange.Write(caret, 1, string.Empty, caret);

        return new ContentChange([ContentWrite.Words(words, caret, 0, edit.Text)], caret + edit.Text.Length, state.Raw);
    }
}
