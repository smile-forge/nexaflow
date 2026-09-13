using System.Globalization;
using System.Text;
using TreeSitter;

namespace Nexaflow.Syntax;

/// <summary>
/// The part of XPath that names one element of a document — enough to say "the PackageReference for NAudio" or
/// "the second ItemGroup", and nothing that would need an engine.
/// <para>
/// <c>/a/b</c> from the root, <c>//b</c> at any depth, <c>*</c> for any element; predicates <c>[n]</c> (the
/// n-th among its parent's matching children, from 1, as XPath counts), <c>[@attr]</c>,
/// <c>[@attr='value']</c> and <c>[child]</c>. An unprefixed name matches whatever prefix an element carries,
/// because a project file's elements are in a default namespace nobody writes out; a prefixed one, and an
/// attribute name like <c>x:Name</c>, is matched as written. A dotted XAML property element
/// (<c>Grid.RowDefinitions</c>) is just a name.
/// </para>
/// <para>
/// Why XPath at all, rather than extending the <c>T:</c>/<c>N:</c> paths the outline hands out: those name
/// what has a name, and most of a project file does not — no element of an <c>ItemGroup</c> has an id, only
/// an attribute that tells it apart. XPath is the notation an agent already knows for exactly that, and a
/// subset that refuses the rest out loud is better than a dialect nobody knows.
/// </para>
/// </summary>
internal sealed class XmlPath
{
    internal enum Axis { Child, Descendant }

    internal abstract record Predicate;
    internal sealed record Position(int N) : Predicate;
    internal sealed record HasAttribute(string Name, string? Value) : Predicate;
    internal sealed record HasChild(string Name) : Predicate;

    /// <param name="End">Where this step ends in the text, so a report can quote the part of the path that matched.</param>
    internal sealed record Step(Axis Axis, string Name, IReadOnlyList<Predicate> Predicates, int End);

    private XmlPath(string text, IReadOnlyList<Step> steps) { Text = text; Steps = steps; }

    public string Text { get; }
    public IReadOnlyList<Step> Steps { get; }

    /// <summary>The path's text up to and including step <paramref name="index"/>.</summary>
    public string Prefix(int index) => Text[..Steps[index].End];

    public static bool TryParse(string text, out XmlPath? path, out string? error)
    {
        path  = null;
        error = null;
        var steps = new List<Step>();
        var i = 0;

        string Fail(string message, int at) =>
            $"{message}\n  {text}\n  {new string(' ', Math.Clamp(at, 0, text.Length))}^";

        if (text.Length == 0) { error = "An element path is needed, e.g. /Project/ItemGroup[1]."; return false; }
        if (text[0] != '/')
        {
            error = Fail($"'{text}' is relative, and there is nothing for it to be relative to: write /{text} for "
                       + $"the root element, or //{text} to find it at any depth.", 0);
            return false;
        }

        while (i < text.Length)
        {
            Axis axis;
            if (string.CompareOrdinal(text, i, "//", 0, 2) == 0) { axis = Axis.Descendant; i += 2; }
            else if (text[i] == '/') { axis = Axis.Child; i++; }
            else
            {
                error = text[i] == '|'
                    ? Fail("A union (|) is not supported - make one edit per path.", i)
                    : Fail("Expected / before the next step.", i);
                return false;
            }

            string name;
            if (i < text.Length && text[i] == '*') { name = "*"; i++; }
            else
            {
                var start = i;
                if (i < text.Length && IsNameStart(text[i])) { i++; while (i < text.Length && IsNameChar(text[i])) i++; }
                name = text[start..i];
                if (name.Length == 0)
                {
                    error = i < text.Length && text[i] == '.'
                        ? Fail(". and .. are not supported - write the path down from the root instead.", i)
                        : i < text.Length && text[i] == '('
                            ? Fail("Grouping and functions are not supported - only names and [predicates].", i)
                            : Fail("Expected an element name (or *).", i);
                    return false;
                }
                if (i < text.Length && text[i] == '(')
                {
                    error = Fail($"{name}() is a function, and functions are not supported - only names and [predicates].", start);
                    return false;
                }
            }

            var predicates = new List<Predicate>();
            while (i < text.Length && text[i] == '[')
            {
                var open = i++;
                SkipSpaces();

                if (i < text.Length && char.IsAsciiDigit(text[i]))
                {
                    var start = i;
                    while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
                    var n = int.Parse(text[start..i], CultureInfo.InvariantCulture);
                    if (n <= 0) { error = Fail("Positions count from 1, as XPath does: [1] is the first.", start); return false; }
                    predicates.Add(new Position(n));
                }
                else if (i < text.Length && text[i] == '@')
                {
                    i++;
                    var start = i;
                    if (i < text.Length && IsNameStart(text[i])) { i++; while (i < text.Length && IsNameChar(text[i])) i++; }
                    var attribute = text[start..i];
                    if (attribute.Length == 0) { error = Fail("Expected an attribute name after @.", i); return false; }
                    SkipSpaces();

                    string? value = null;
                    if (i < text.Length && text[i] == '=')
                    {
                        i++;
                        SkipSpaces();
                        if (i >= text.Length || text[i] is not ('\'' or '"'))
                        {
                            error = Fail("Quote the value: [@Include='NAudio'].", i);
                            return false;
                        }
                        var quote = text[i++];
                        var close = text.IndexOf(quote, i);
                        if (close < 0) { error = Fail("This quote is never closed.", i - 1); return false; }
                        value = text[i..close];
                        i = close + 1;
                    }
                    predicates.Add(new HasAttribute(attribute, value));
                }
                else
                {
                    var start = i;
                    if (i < text.Length && IsNameStart(text[i])) { i++; while (i < text.Length && IsNameChar(text[i])) i++; }
                    var child = text[start..i];
                    if (child.Length == 0)
                    {
                        error = Fail("Expected a position, @attribute or child element name inside [ ].", i);
                        return false;
                    }
                    if (i < text.Length && text[i] == '(')
                    {
                        error = Fail($"{child}() is a function, and functions are not supported inside [ ].", start);
                        return false;
                    }
                    predicates.Add(new HasChild(child));
                }

                SkipSpaces();
                if (i >= text.Length || text[i] != ']')
                {
                    error = Fail(i < text.Length && text[i] is '<' or '>' or '!'
                                     ? "Comparisons other than = are not supported."
                                     : "Expected ] to close the predicate opened here.", i < text.Length ? i : open);
                    return false;
                }
                i++;
            }

            steps.Add(new Step(axis, name, predicates, i));
        }

        path = new XmlPath(text, steps);
        return true;

        void SkipSpaces() { while (i < text.Length && text[i] == ' ') i++; }
    }

    private static bool IsNameStart(char c) => char.IsLetter(c) || c == '_';
    private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c is '_' or ':' or '.' or '-' or '·';

    /// <summary>What a path found, and how far it got when it found nothing: the last step that matched
    /// anything (-1 for none) and what that step matched.</summary>
    internal sealed record Evaluation(List<Node> Matches, int DeepestMatched, List<Node> DeepestSet);

    /// <summary>Evaluates the path over a parsed document. The nodes are only valid inside the parse.</summary>
    public Evaluation Evaluate(Node document)
    {
        var current = new List<Node> { document };
        var deepest = -1;
        var deepestSet = new List<Node>();

        for (var s = 0; s < Steps.Count; s++)
        {
            var step = Steps[s];
            var next = new List<Node>();
            var seen = new HashSet<int>();

            foreach (var context in current)
                foreach (var parent in step.Axis == Axis.Child ? [context] : SelfAndDescendants(context))
                {
                    var candidates = ChildElements(parent).Where(e => NameMatches(e, step.Name)).ToList();
                    foreach (var predicate in step.Predicates) candidates = Apply(predicate, candidates);
                    foreach (var c in candidates)
                        if (seen.Add(c.StartIndex)) next.Add(c);
                }

            if (next.Count == 0) return new Evaluation([], deepest, deepestSet);
            current = next;
            deepest = s;
            deepestSet = next;
        }

        return new Evaluation(current, deepest, deepestSet);
    }

    private static List<Node> Apply(Predicate predicate, List<Node> candidates) => predicate switch
    {
        Position p     => p.N <= candidates.Count ? [candidates[p.N - 1]] : [],
        HasAttribute a => [.. candidates.Where(e => Attributes(e).Any(x => x.Name == a.Name && (a.Value is null || x.Value == a.Value)))],
        HasChild c     => [.. candidates.Where(e => ChildElements(e).Any(k => NameMatches(k, c.Name)))],
        _              => candidates,
    };

    /// <summary>Whether an element answers to a step's name: any element for <c>*</c>, the name as written for a
    /// prefixed step, and the local name — whatever the prefix — for an unprefixed one.</summary>
    internal static bool NameMatches(Node element, string name)
    {
        if (name == "*") return true;
        var raw = RawName(element);
        if (name.Contains(':')) return raw == name;
        var colon = raw.LastIndexOf(':');
        return (colon >= 0 ? raw[(colon + 1)..] : raw) == name;
    }

    /// <summary>An element's name as written, prefix and all.</summary>
    internal static string RawName(Node element) =>
        CodeStructureExtractor.FirstChild(element, "STag", "EmptyElemTag") is { } tag
            ? CodeStructureExtractor.FirstChild(tag, "Name")?.Text ?? ""
            : "";

    /// <summary>The element children of a document (its root) or of an element.</summary>
    internal static IEnumerable<Node> ChildElements(Node node) =>
        node.Type == "document"
            ? node.NamedChildren.Where(c => c.Type == "element")
            : CodeStructureExtractor.XamlChildren(node);

    private static IEnumerable<Node> SelfAndDescendants(Node node)
    {
        yield return node;
        foreach (var child in ChildElements(node))
            foreach (var d in SelfAndDescendants(child)) yield return d;
    }

    /// <summary>An element's attributes with their values decoded — what a predicate compares against.</summary>
    internal static IEnumerable<(string Name, string Value)> Attributes(Node element) =>
        CodeStructureExtractor.FirstChild(element, "STag", "EmptyElemTag") is { } tag
            ? CodeStructureExtractor.XamlAttributes(tag).Select(a => (a.Name, Decode(a.Value)))
            : [];

    /// <summary>The value an attribute means: the five predefined entities and character references decoded.</summary>
    internal static string Decode(string raw)
    {
        if (!raw.Contains('&')) return raw;

        var sb = new StringBuilder(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            var semi = raw[i] == '&' ? raw.IndexOf(';', i) : -1;
            if (semi < 0) { sb.Append(raw[i]); continue; }

            var entity = raw[(i + 1)..semi];
            string? decoded = entity switch
            {
                "amp" => "&", "lt" => "<", "gt" => ">", "quot" => "\"", "apos" => "'",
                _ when entity.StartsWith("#x", StringComparison.OrdinalIgnoreCase)
                       && int.TryParse(entity[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex)
                       && IsScalar(hex)
                    => char.ConvertFromUtf32(hex),
                _ when entity.StartsWith('#') && int.TryParse(entity[1..], CultureInfo.InvariantCulture, out var dec)
                       && IsScalar(dec)
                    => char.ConvertFromUtf32(dec),
                _ => null,
            };
            if (decoded is null) { sb.Append(raw[i]); continue; }
            sb.Append(decoded);
            i = semi;
        }
        return sb.ToString();

        // A reference to a surrogate or past the last code point is not a character; it stays as written.
        static bool IsScalar(int code) => code is >= 0 and <= 0x10FFFF and not (>= 0xD800 and <= 0xDFFF);
    }
}
