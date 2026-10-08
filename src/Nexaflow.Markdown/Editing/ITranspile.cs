using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Editing;

/// <summary>
/// How a language's parser writes back to its own source.
///
/// <para>
/// A parser that can be written through declares this, and every change goes through it before any of it reaches the
/// source — the answer a gesture handler gave, and the one the engine made itself for an ordinary key. What comes back is
/// what is written; null refuses it and nothing is. A language whose parser declares none of this cannot be written in at
/// all, which is the point: characters are never spliced into source that nothing has vouched for.
/// </para>
/// <para>
/// <strong>As given, by default.</strong> Almost every change already is what the language would write — a character put
/// where the caret is, a character taken back — so the default answer is the change itself. A parser overrides only where
/// its own spelling gets in the way: a quote that has to go in as an entity code, a bracket that would close what it was
/// written inside, a letter that cannot go in a number at all. Everything it says nothing about goes in as asked.
/// </para>
/// <para>
/// Static, so it lives in the parser rather than beside it. Nothing about rewriting a change needs an instance, and a
/// parser is not a thing anybody makes one of.
/// </para>
/// </summary>
public interface ITranspile
{
    /// <summary><paramref name="change"/> as this language writes it, or null to write nothing at all.</summary>
    static virtual ContentChange? Rewrite(ContentChange change) => change;
}

/// <summary>
/// What a parser declared, as something that can be held and called: a static member is reached through the type that
/// declared it, and the engine holds languages rather than their types.
/// </summary>
public static class Transpiles
{
    /// <summary>The rewrite <typeparamref name="T"/> declares — its own, or the one every parser gets.</summary>
    public static Func<ContentChange, ContentChange?> By<T>() where T : ITranspile => T.Rewrite;

    /// <summary>
    /// <paramref name="change"/> with every write that holds words as the reader means them spelled by
    /// <paramref name="spelled"/>, and the caret moved to wherever spelling them put it — or null where any of them
    /// cannot be written where it is going at all.
    ///
    /// <para>
    /// The walk, rather than the spelling: which characters a language has to write differently is the language's own
    /// business, and keeping track of how far each spelling moved the caret and the writes after it is nobody's. A
    /// write that is already source goes as it stands.
    /// </para>
    /// </summary>
    public static ContentChange? Spelling(ContentChange change, Func<ContentPart, string, string?> spelled)
    {
        if (!change.Writes.Any(write => write.Meant)) return change;

        var writes = new List<ContentWrite>(change.Writes.Count);
        var caret = change.Caret;
        var moved = 0;
        var grown = 0;

        foreach (var write in change.Writes.OrderBy(write => write.Start))
        {
            // Where the write stands once the ones before it have been made, and how far it moves what follows.
            var at = write.Start + moved;
            moved += write.Text.Length - write.Length;

            if (!write.Meant) { writes.Add(write); continue; }
            if (spelled(write.Part!, write.Text) is not { } said) return null;

            writes.Add(new ContentWrite(write.Start, write.Length, said) { Part = write.Part });

            if (change.Caret >= at && change.Caret <= at + write.Text.Length)
                caret = at + grown + (spelled(write.Part!, write.Text[..(change.Caret - at)])?.Length ?? said.Length);
            else if (change.Caret > at + write.Text.Length)
                caret += said.Length - write.Text.Length;

            grown += said.Length - write.Text.Length;
        }

        return change with { Writes = writes, Caret = caret };
    }
}
