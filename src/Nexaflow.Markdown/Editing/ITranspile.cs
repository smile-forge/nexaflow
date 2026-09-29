namespace Nexaflow.Markdown.Editing;

/// <summary>
/// How a language writes back to its own source.
///
/// <para>
/// A language that can be written in offers one of these, and every change goes through it before any of it reaches the
/// source — the answer a gesture handler gave, and the one the engine made itself for an ordinary key. What comes back is
/// what is written; null refuses it and nothing is. A language offering none of these cannot be written in at all, which
/// is the point: characters are never spliced into source that nothing has vouched for.
/// </para>
/// <para>
/// <strong>As given, by default.</strong> Almost every change already is what the language would write — a character put
/// where the caret is, a character taken back — so the default answer is the change itself. A language overrides only
/// where its own spelling gets in the way: a quote that has to go in as an entity code, a bracket that would close what it
/// was written inside, a letter that cannot go in a number at all. Everything it says nothing about goes in as asked.
/// </para>
/// </summary>
public interface ITranspile
{
    /// <summary>What a language writes where it has nothing of its own to say: the change, as it was given.</summary>
    static ITranspile AsGiven { get; } = new Plainly();

    /// <summary><paramref name="change"/> as this language writes it, or null to write nothing at all.</summary>
    ContentChange? Rewrite(ContentChange change) => change;

    private sealed class Plainly : ITranspile;
}
