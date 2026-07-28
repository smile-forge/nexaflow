namespace Nexaflow.Search;

/// <summary>
/// The one parser for the AI bar's search syntax, so no page re-implements it.
/// <para>
/// After the router strips the <c>?</c> symbol, the remainder is either literal text or a delimited
/// regular expression: <c>/pattern/</c>, optionally with flags — <c>i</c> (ignore case, the default) or
/// <c>c</c> (case sensitive). An unterminated <c>/pattern</c> is still a regex, so the syntax works while
/// the user is mid-type. Anything whose trailing segment isn't valid flags is literal, which keeps a path
/// like <c>/api/v1/users</c> a plain text search rather than the regex <c>api</c> with nonsense flags.
/// </para>
/// </summary>
public static class SearchSyntax
{
    /// <summary>The delimiter that opens (and closes) a regex.</summary>
    public const char RegexDelimiter = '/';

    private const char IgnoreCaseFlag    = 'i';
    private const char CaseSensitiveFlag = 'c';

    /// <summary>
    /// Parses symbol-stripped input into a request. Never throws and never rejects: input that isn't
    /// valid regex syntax simply comes back as a literal search.
    /// </summary>
    public static SearchRequest Parse(string input)
    {
        var text = (input ?? string.Empty).Trim();
        if (text.Length < 2 || text[0] != RegexDelimiter) return Literal(text);

        var close = text.LastIndexOf(RegexDelimiter);

        // Only the opening delimiter — an unterminated regex the user is still typing.
        if (close == 0)
            return new SearchRequest(text[1..], IsRegex: true);

        var pattern = text[1..close];
        var flags   = text[(close + 1)..];

        // "//" or "//i" — no pattern to run; treat the whole thing as literal rather than matching everything.
        if (pattern.Length == 0) return Literal(text);

        if (!TryReadFlags(flags, out var matchCase)) return Literal(text);

        return new SearchRequest(pattern, IsRegex: true, MatchCase: matchCase);
    }

    /// <summary>
    /// Parses a whole query — possibly several AND-ed terms — into a request. <paramref name="recognizers"/>
    /// contribute the term kinds this caller can honour; supply none and glob syntax stays plain text.
    /// <para>
    /// <see cref="SearchRequest.Text"/>/<see cref="SearchRequest.IsRegex"/> describe the FIRST term, so a
    /// single-term query reads exactly as it did before terms existed and pages that only handle one
    /// pattern keep working untouched.
    /// </para>
    /// </summary>
    public static SearchRequest ParseRequest(
        string input, IReadOnlyList<ISearchTermRecognizer>? recognizers = null)
    {
        var terms = ParseTerms(input, recognizers);

        // Nothing to search for. An explicitly empty term list says so, where the default single-term
        // fallback would manufacture one empty term and read as a real (if useless) query.
        if (terms.Count == 0) return new SearchRequest(string.Empty) { Terms = [] };

        var first = terms[0];
        return new SearchRequest(first.Value, first.Kind == SearchTermKind.Regex, first.MatchCase)
        {
            Terms = terms,
        };
    }

    /// <summary>Renders a request back into the bar's syntax — the inverse of <see cref="Parse"/>.</summary>
    public static string Format(SearchRequest request)
    {
        // EVERY term, not just the first. This string is how a query survives being handed between
        // surfaces (the browser opening a Search tab passes it through a page parameter), so dropping the
        // tail here silently turns a three-term search into a one-term one — and dropping the quotes turns
        // a phrase back into separate words.
        if (request.Terms.Count > 0)
            return string.Join(" ", request.Terms.Select(FormatTerm));

        return FormatSingle(request.Text, request.IsRegex, request.MatchCase);
    }

    /// <summary>One term as the user would have typed it, so <see cref="ParseTerms"/> reads it back the same.</summary>
    private static string FormatTerm(SearchTerm term)
    {
        // What they actually typed, where it was kept — the only rendering guaranteed to round-trip.
        if (!string.IsNullOrEmpty(term.Display)) return term.Display;

        if (term.Kind == SearchTermKind.Regex && !term.NameOnly)
            return FormatSingle(term.Value, isRegex: true, term.MatchCase);

        var joined = string.Join("|", term.SourceForms);
        return joined.Contains(' ') ? $"\"{joined}\"" : joined;
    }

    private static string FormatSingle(string text, bool isRegex, bool matchCase)
    {
        if (!isRegex) return text.Contains(' ') ? $"\"{text}\"" : text;
        var flags = matchCase ? CaseSensitiveFlag.ToString() : string.Empty;
        return $"{RegexDelimiter}{text}{RegexDelimiter}{flags}";
    }

    /// <summary>
    /// Splits a whole query into its AND-ed terms: filename globs (<c>*.txt|*.md</c>), delimited regular
    /// expressions (<c>/ma(ths|gic)/</c>) and plain text, mixed freely —
    /// <c>*.txt|*.md /ma(ths|gic)/ ocr</c>. Within a term, <c>|</c> is OR.
    /// <para>
    /// A regex is the only term that may contain spaces, because its delimiters say where it ends. Splitting
    /// on whitespace outside the delimiters is what lets the three kinds share one input line.
    /// </para>
    /// </summary>
    public static IReadOnlyList<SearchTerm> ParseTerms(
        string input, IReadOnlyList<ISearchTermRecognizer>? recognizers = null)
    {
        var terms = new List<SearchTerm>();
        foreach (var token in Tokenize(input ?? string.Empty))
        {
            if (token.Length == 0) continue;

            // A delimited pattern is a regex term, whatever it contains — the base syntax owns these, so a
            // recognizer never gets the chance to reinterpret one.
            if (token.Length >= 2 && token[0] == RegexDelimiter)
            {
                var parsed = Parse(token);
                terms.Add(parsed.IsRegex
                    ? new SearchTerm(SearchTermKind.Regex, [parsed.Text], parsed.MatchCase, Display: token)
                    : new SearchTerm(SearchTermKind.Text,  [parsed.Text], Display: token));
                continue;
            }

            // A quoted token is a phrase: one literal term, spaces and all. Quoting means "take this
            // exactly", so no recogniser gets to reinterpret it and "|" inside it is just a character —
            // otherwise "the lost dog" would become three separate AND-ed words.
            if (IsQuoted(token))
            {
                terms.Add(new SearchTerm(SearchTermKind.Text, [Unquote(token)], Display: token));
                continue;
            }

            // Then whatever the caller taught this parse to understand. A surface that doesn't supply a
            // glob recognizer simply never produces glob terms — it can't be handed one it can't honour.
            var claimed = recognizers?.Select(r => r.Recognize(token)).FirstOrDefault(t => t is not null);
            if (claimed is not null) { terms.Add(claimed); continue; }

            // Default: plain text, with "|" as OR. Alternatives may themselves be quoted phrases.
            var alternatives = token.Split('|', StringSplitOptions.RemoveEmptyEntries);
            if (alternatives.Length == 0) continue;
            terms.Add(new SearchTerm(SearchTermKind.Text, alternatives.Select(Unquote).ToList(), Display: token));
        }
        return terms;
    }

    private static bool IsQuoted(string s) =>
        s.Length >= 2 && s[0] == '"' && s[^1] == '"';

    private static string Unquote(string s) => IsQuoted(s) ? s[1..^1] : s;

    // Whitespace-separated, except inside "quotes" or /pattern/ — a regex may legitimately contain spaces.
    private static IEnumerable<string> Tokenize(string input)
    {
        var current  = new System.Text.StringBuilder();
        var inRegex  = false;
        var inQuote  = false;

        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];

            if (inRegex)
            {
                current.Append(c);
                // The closing delimiter ends the pattern; any trailing flag characters ride along with it.
                if (c == RegexDelimiter && current.Length > 1) inRegex = false;
                continue;
            }

            if (c == '"') { inQuote = !inQuote; current.Append(c); continue; }

            if (!inQuote && c == RegexDelimiter && current.Length == 0)
            {
                inRegex = true;
                current.Append(c);
                continue;
            }

            if (!inQuote && char.IsWhiteSpace(c))
            {
                if (current.Length > 0) { yield return current.ToString(); current.Clear(); }
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0) yield return current.ToString();
    }

    // Quotes mark a phrase, so they aren't part of what to search for — and stripping them here is what
    // lets Format/Parse round-trip a phrase without the quotes accumulating or the phrase splitting.
    private static SearchRequest Literal(string text) => new(Unquote(text));

    // Flags are a short set of known characters; anything else means this wasn't a regex at all.
    private static bool TryReadFlags(string flags, out bool matchCase)
    {
        matchCase = false;
        foreach (var f in flags)
        {
            switch (char.ToLowerInvariant(f))
            {
                case IgnoreCaseFlag:    matchCase = false; break;
                case CaseSensitiveFlag: matchCase = true;  break;
                default: return false;
            }
        }
        return true;
    }
}
