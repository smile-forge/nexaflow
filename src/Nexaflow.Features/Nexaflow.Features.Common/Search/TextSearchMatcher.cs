using System.Diagnostics.CodeAnalysis;
using Nexaflow.Search;

namespace Nexaflow.Features.Common.Search;

/// <summary>One line of a searched body that satisfied the query.</summary>
/// <param name="Index">0-based line number — the id an <see cref="ISearchable"/> page round-trips.</param>
/// <param name="Offset">Character offset of the line's first character within the body.</param>
/// <param name="Text">The line, without its terminator.</param>
public readonly record struct SearchLine(int Index, int Offset, string Text);

/// <summary>
/// Applies a <see cref="SearchRequest"/> to a body of text, for the pages that search their own content
/// rather than an index — a viewer, an editor, a message.
/// <para>
/// It owns <em>policy</em>, not semantics. What a term means is <see cref="SearchTerm.Matches"/>'s answer
/// and nothing here second-guesses it: a literal is the word it spells, <c>fig*</c> is a prefix,
/// <c>*fig*</c> a substring, and <c>"partial*"</c> an asterisk. Re-deciding any of that per page is
/// exactly how <c>?foo</c> comes to mean something different in each tab.
/// </para>
/// <para>
/// What it does decide is which requests a single-body page can honour at all, and it says so up front:
/// a filename filter, a property constraint only an index can answer, or a malformed pattern must reach
/// the user as <see cref="SearchOutcome.Unsupported"/>, never as an empty result that reads as
/// "nothing here".
/// </para>
/// </summary>
public sealed class TextSearchMatcher
{
    private readonly SearchRequest _request;

    private TextSearchMatcher(SearchRequest request) => _request = request;

    /// <summary>Validates <paramref name="request"/> against what a single-body page can do, or explains
    /// why it can't be run.</summary>
    public static bool TryCreate(
        SearchRequest request,
        [NotNullWhen(true)]  out TextSearchMatcher? matcher,
        [NotNullWhen(false)] out string? error)
    {
        matcher = null;

        // A page that searches one body has no filename to judge, so a name-scoped term can only be
        // dropped — and dropping it silently answers a narrower question than the user asked.
        if (request.HasNameOnlyTerms)
        {
            error = "Filename filters don't apply when searching inside a document — drop them and search the text.";
            return false;
        }

        if (request.Terms.FirstOrDefault(t => t.IndexEnforced) is { } structured)
        {
            error = $"'{structured.Label}' is a property filter only a search index can answer — this page searches text.";
            return false;
        }

        // Reported rather than silently matching nothing, so the user learns their pattern was bad.
        if (!request.TryValidate(out var invalid))
        {
            error = $"Invalid regular expression: {invalid}";
            return false;
        }

        if (request.Terms.Count == 0)
        {
            error = "Nothing to search for.";
            return false;
        }

        matcher = new TextSearchMatcher(request);
        error = null;
        return true;
    }

    /// <summary>True when <paramref name="candidate"/> satisfies every term of the query.</summary>
    public bool Matches(string candidate) => _request.Matches(candidate);

    /// <summary>Every matched span in <paramref name="candidate"/>, in document order — what the page
    /// paints or selects. Same source as <see cref="Matches"/>, so the two can't disagree.</summary>
    public IReadOnlyList<(int Index, int Length)> Occurrences(string candidate) =>
        _request.Occurrences(candidate);

    /// <summary>
    /// Walks <paramref name="body"/> line by line, yielding the ones that match. Lazy, so a caller can stop
    /// early; line offsets come back with the text because a page that wants to select a match needs both.
    /// </summary>
    public IEnumerable<SearchLine> ScanLines(string body)
    {
        var index = 0;
        var start = 0;
        while (true)
        {
            var newline = body.IndexOf('\n', start);
            var end     = newline < 0 ? body.Length : newline;
            var textEnd = end > start && body[end - 1] == '\r' ? end - 1 : end;

            var text = body[start..textEnd];
            if (Matches(text)) yield return new SearchLine(index, start, text);

            if (newline < 0) yield break;
            start = newline + 1;
            index++;
        }
    }
}
