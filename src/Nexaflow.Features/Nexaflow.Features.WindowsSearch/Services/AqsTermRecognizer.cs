using System.Text.RegularExpressions;
using Nexaflow.Search;

namespace Nexaflow.Features.WindowsSearch.Services;

/// <summary>
/// Teaches a search query to accept Advanced Query Syntax property constraints — the syntax people already
/// know from Explorer's search box (<c>kind:document</c>, <c>author:john</c>, <c>size:&gt;1mb</c>,
/// <c>modified:last week</c>) — alongside our own globs, patterns and text.
/// <para>
/// Only offered by the surfaces backed by the index, and only for tokens that actually look like a property
/// constraint: everything else stays plain text, so a search for <c>http://example.com</c> isn't mistaken
/// for a property named "http".
/// </para>
/// </summary>
public sealed partial class AqsTermRecognizer(IAqsTranslator translator) : ISearchTermRecognizer
{
    // property:value — a bare word, a colon, then something. Deliberately narrow; the translator has the
    // final say on whether the property actually exists.
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9._]*:[^\s]", RegexOptions.Compiled)]
    private static partial Regex PropertyShape { get; }

    public SearchTerm? Recognize(string token)
    {
        if (!PropertyShape.IsMatch(token)) return null;
        if (!translator.Recognises(token)) return null;

        // Carried as the raw AQS. Translation happens once, where the SQL is built — this layer stays free
        // of both COM and SQL.
        return new SearchTerm(SearchTermKind.Structured, [token], Display: token);
    }
}
