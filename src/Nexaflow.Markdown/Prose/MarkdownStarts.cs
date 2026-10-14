
namespace Nexaflow.Markdown.Prose;

/// <summary>What a character can start in a run of words (<see cref="MarkdownTokenizer.Starts"/>).</summary>
internal enum MarkdownStarts
{
    /// <summary>Nothing: it is only itself.</summary>
    Nothing,

    /// <summary>
    /// Something wherever it stands: an escape, a code span, an entity, a tag, a bracket, and emphasis written with one of a
    /// character.
    /// </summary>
    Alone,

    /// <summary>Something where two of it stand together: the emphasis written only with a pair — <c>++</c>, <c>==</c>, <c>""</c>.</summary>
    Doubled,

    /// <summary>Something where another of it stands further along: a formula, between dollars.</summary>
    Paired,

    /// <summary>Something where a bracket follows it: a picture.</summary>
    Bracketed,
}
