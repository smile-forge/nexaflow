using Nexaflow.Search;

namespace Nexaflow.IO.Common;

/// <summary>
/// Teaches a search query to understand filename globs (<c>*.txt</c>, <c>report?.md</c>), including
/// <c>|</c>-separated alternatives. Lives here, beside <see cref="Glob"/> itself, so the search query
/// language needs no glob implementation and no dependency on this library — it only accepts the term this
/// produces.
/// <para>
/// Supplied by the surfaces that search files (the browser and the file index) and by nothing else, so a
/// page searching its own single body is never handed a <c>*.txt</c> term it could only ignore.
/// </para>
/// </summary>
public sealed class GlobTermRecognizer : ISearchTermRecognizer
{
    public SearchTerm? Recognize(string token)
    {
        var alternatives = token.Split('|', StringSplitOptions.RemoveEmptyEntries);
        if (alternatives.Length == 0) return null;

        // Every alternative has to look like a glob: "*.txt|notes" is a mixed bag better left as plain
        // text than half-interpreted.
        if (!alternatives.All(Glob.ContainsGlobChars)) return null;

        // Handed over already translated, so the search library matches it with the regex engine it already
        // has — "*" and "?" keep their glob meanings rather than their regex ones.
        //
        // NOT name-scoped. A glob is the wildcard syntax people reach for when they don't want to write a
        // regex, and restricting it to filenames makes it a different feature from the one they typed. So
        // it matches the name OR a word in the contents — which is two patterns, because "*" means "the
        // rest of the name" in one and "the rest of this word" in the other.
        return new SearchTerm(
            SearchTermKind.Regex,
            alternatives.Select(Glob.ToRegexPattern).ToList(),
            Display:             token,
            Sources:             alternatives,   // the globs themselves, for a backend that speaks them
            ContentAlternatives: alternatives.Select(Glob.ToContentRegexPattern).ToList(),
            IsGlob:              true);
    }
}
