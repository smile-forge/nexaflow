using System;

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

    /// <summary>The change as it was given: what a language writes where it has nothing of its own to say.</summary>
    public static Func<ContentChange, ContentChange?> AsGiven { get; } = change => change;
}
