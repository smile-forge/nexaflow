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

        // Every alternative has to be a filename-shaped glob — one with an extension or a path, the only
        // kind that can be judged against a name alone. A bare wildcard word ("*term*", "term*") is NOT
        // claimed here: it falls through to a plain text term, which the search library matches with its
        // wildcards against a file's CONTENT as well as its name. Mixing the two ("*.txt|notes*") is a bag
        // better left as text than half-interpreted.
        if (!alternatives.All(Glob.IsFilenameGlob)) return null;

        // Handed over already anchored, so the search library matches it with the regex engine it already
        // has — "*" and "?" keep their glob meanings rather than their regex ones.
        //
        // Name-scoped: a glob NARROWS which files are considered, which is what makes it worth typing —
        // it lets a folder scan skip a file without opening it. Someone who wants "*.txt" found INSIDE a
        // document quotes it, and gets a literal text term instead.
        return new SearchTerm(
            SearchTermKind.Regex,
            alternatives.Select(Glob.ToRegexPattern).ToList(),
            NameOnly: true,
            Display:  token,
            Sources:  alternatives);   // the globs themselves, for a backend that speaks them natively
    }
}
