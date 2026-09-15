using System.Text;
using TreeSitter;

namespace Nexaflow.Syntax;

/// <summary>One attribute of an element: where it is, where its value is (quotes included), and what it means.</summary>
internal sealed record XmlAttribute(string Name, int Start, int End, int ValueStart, int ValueEnd, char Quote,
                                    string Value);

/// <summary>
/// Everything an edit needs to know about one element, as plain offsets into the text — the parse it came from is
/// gone by the time anything uses it.
/// </summary>
internal sealed record XmlElement(
    string QName, int Start, int End, int TriviaStart,
    int TagNameStart, int TagNameEnd, int TagEnd, int CloserStart, bool SelfClosing,
    int? EndTagStart, int? EndTagNameStart, int? EndTagNameEnd,
    int? FirstContentStart, int? LastContentEnd, bool HasText,
    IReadOnlyList<XmlAttribute> Attributes, int DescendantElements, bool IsRoot,
    string CanonicalPath, int Line)
{
    public XmlAttribute? Attribute(string name) => Attributes.LastOrDefault(a => a.Name == name);

    /// <summary>The element as a <see cref="DeclarationAnchor"/>, so the edits that only need its extent and
    /// the comment above it — replace, doc, substitute — are the ones every other language uses.</summary>
    public DeclarationAnchor ToAnchor() => new(
        "element", Start, End, TriviaStart, TagNameStart, TagNameEnd, null, null,
        SelfClosing ? null : TagEnd, SelfClosing ? null : EndTagStart, FirstContentStart, LastContentEnd);
}

/// <summary>
/// Finds elements to edit: by the id the outline gave a XAML element, or by an <see cref="XmlPath"/> in any
/// XML-family file.
/// <para>
/// A separate resolver rather than a branch in <see cref="DeclarationAnchors"/>, which finds a declaration by the
/// grammar's <c>name</c> field — and the XML grammar has none: an element's identity is an attribute's value, or
/// where it sits. Asked that way, every edit to a XAML id failed with "no declaration named …" while the outline
/// listed it one line away.
/// </para>
/// </summary>
internal static class XmlAnchors
{
    private static T? Parse<T>(string grammarId, string text, Func<Node, T> visit)
    {
        using var highlighter = CodeHighlighter.TryCreate(grammarId);
        if (highlighter is null) return default;
        try { return highlighter.WithParseTree(text, visit); }
        catch { return default; }
    }

    private const string Unparsed =
        "The file does not parse into elements, so there is nothing to address in it structurally. Fix it with "
      + "substitute on the file: id first.";

    /// <summary>
    /// The element a XAML id names — <c>T:</c>, <c>N:</c>, <c>K:</c> or <c>A:</c>, with any <c>#n</c> — through
    /// the same walk that handed the id out, together with the attribute carrying its name and the label the
    /// outline shows. Plain XML has one id, its root's.
    /// </summary>
    public static (XmlElement? Element, XmlAttribute? Identity, string? Label, string? Error) ByAstPath(
        string grammarId, string text, string astPath)
    {
        if (astPath.Contains("/M:", StringComparison.Ordinal))
            return (null, null, null,
                    $"'{astPath}' is an event handler, and a handler is an attribute of its element rather than an "
                  + "element of its own. Use set-attribute or remove-attribute on the element's id.");

        return Parse(grammarId, text, root =>
        {
            if (root.Type == "ERROR" || CodeStructureExtractor.FirstChild(root, "element") is not { } rootElement)
                return ((XmlElement?)null, (XmlAttribute?)null, (string?)null, (string?)Unparsed);

            if (grammarId == "xml")
            {
                var name = CodeStructureExtractor.FirstChild(rootElement, "STag", "EmptyElemTag") is { } tag
                    ? CodeStructureExtractor.XamlLocalName(tag) : "";
                return astPath == "T:" + name
                    ? (Describe(rootElement, text), null, name, null)
                    : (null, null, null, $"'{astPath}' names no element here - the only id a plain XML file has is "
                                       + $"its root's, T:{name}. Address anything else with --at.");
            }

            (XmlElement Element, XmlAttribute? Identity, string Label)? found = null;
            CodeStructureExtractor.WalkXamlAnchors(rootElement, visit =>
            {
                if (found is not null) return;
                var id = visit.Primary?.Path == astPath ? visit.Primary
                       : visit.Secondary?.Path == astPath ? visit.Secondary
                       : null;
                if (id is not { } hit) return;

                var element = Describe(visit.Element, text);
                var identity = hit.Attribute is { } attr ? element.Attributes.FirstOrDefault(a => a.Start == attr.Node.StartIndex) : null;
                found = (element, identity, hit.Label);
            });

            return found is { } f
                ? (f.Element, f.Identity, f.Label, null)
                : ((XmlElement?)null, (XmlAttribute?)null, (string?)null, (string?)null);
        });
    }

    /// <summary>Every element the path matches, or why none — naming how far the path did get, and what was there.</summary>
    public static (IReadOnlyList<XmlElement> Elements, string? Error) ByPath(string grammarId, string text, XmlPath path)
    {
        var result = Parse(grammarId, text, root =>
        {
            if (root.Type == "ERROR" || CodeStructureExtractor.FirstChild(root, "element") is null)
                return ((IReadOnlyList<XmlElement>)[], (string?)Unparsed);

            var evaluation = path.Evaluate(root);
            if (evaluation.Matches.Count > 0)
                return ([.. evaluation.Matches.Select(e => Describe(e, text))], null);

            return ([], NoMatch(root, path, evaluation, text));
        });
        return result.Item1 is null ? ([], "The file could not be parsed.") : result;
    }

    /// <summary>
    /// The element at a place in the text, as a compiler or an analyzer gives one. With a column it is the innermost element
    /// spanning that place — a build warning's position lands in the element it is about; a bare line is the element whose
    /// tag opens on it, every one of them when several do.
    /// </summary>
    public static (IReadOnlyList<XmlElement> Elements, string? Error) ByPlace(string grammarId, string text, int line, int? column)
    {
        var result = Parse(grammarId, text, root =>
        {
            if (root.Type == "ERROR" || CodeStructureExtractor.FirstChild(root, "element") is null)
                return ((IReadOnlyList<XmlElement>)[], (string?)Unparsed);

            if (OffsetOf(text, line, column ?? 1) is not { } offset)
                return ([], $"line {line}{(column is null ? "" : $", column {column}")} is not in the file.");

            if (column is not null)
                return AllElements(root).LastOrDefault(e => e.StartIndex <= offset && offset < e.EndIndex) is { } innermost
                    ? ([Describe(innermost, text)], null)
                    : ([], $"no element is at line {line}, column {column}.");

            var lineEnd = text.IndexOf('\n', offset) is var n and >= 0 ? n : text.Length;
            IReadOnlyList<XmlElement> opening = [.. AllElements(root).Where(e => e.StartIndex >= offset && e.StartIndex < lineEnd)
                                                                     .Select(e => Describe(e, text))];
            return opening.Count > 0
                ? (opening, null)
                : ([], $"no element opens on line {line} - line:column takes the element around a place instead.");
        });
        return result.Item1 is null ? ([], "The file could not be parsed.") : result;
    }

    /// <summary>The offset of a 1-based line and column, the column held to its line; null when the line is not there.</summary>
    private static int? OffsetOf(string text, int line, int column)
    {
        if (line < 1 || column < 1) return null;

        var at = 0;
        for (var l = 1; l < line; l++)
        {
            at = text.IndexOf('\n', at);
            if (at < 0) return null;
            at++;
        }
        var end = text.IndexOf('\n', at) is var n and >= 0 ? n : text.Length;
        return Math.Min(at + column - 1, end);
    }

    /// <summary>The element that starts at <paramref name="start"/>, for re-finding one after an edit that did not
    /// move its start.</summary>
    public static XmlElement? ElementAt(string grammarId, string text, int start) =>
        Parse(grammarId, text, root =>
        {
            foreach (var element in AllElements(root))
                if (element.StartIndex == start) return Describe(element, text);
            return null;
        });

    public static int CountElements(string grammarId, string text) =>
        Parse(grammarId, text, root => AllElements(root).Count());

    /// <summary>Every element as a <see cref="DeclarationSignature"/>: its start tag on one line, and the path that
    /// addresses it — what a listing of a view shows, and what a diagnostic in one is placed by.</summary>
    internal static IReadOnlyList<DeclarationSignature> Signatures(string grammarId, string text) =>
        Parse(grammarId, text, root => (IReadOnlyList<DeclarationSignature>)[.. AllElements(root).Select(e =>
        {
            var tag = CodeStructureExtractor.FirstChild(e, "STag", "EmptyElemTag") ?? e;
            return new DeclarationSignature(e.StartPosition.Row + 1, e.EndPosition.Row + 1, XmlPath.RawName(e),
                                            DeclarationSignatures.OneLine(text[tag.StartIndex..tag.EndIndex]),
                                            XmlPath: CanonicalPath(e));
        })]) ?? [];

    private static IEnumerable<Node> AllElements(Node root)
    {
        foreach (var top in XmlPath.ChildElements(root))
            foreach (var e in SelfAndDescendants(top)) yield return e;

        static IEnumerable<Node> SelfAndDescendants(Node node)
        {
            yield return node;
            foreach (var c in XmlPath.ChildElements(node))
                foreach (var d in SelfAndDescendants(c)) yield return d;
        }
    }

    /// <summary>
    /// One indent level as this document's elements use it: the most common step from an element to a child
    /// that starts its own line. The file-wide guess is thrown off by attributes aligned under the first one,
    /// which make a thirteen-space "level" out of a four-space file.
    /// </summary>
    public static string IndentUnit(string grammarId, string text)
    {
        var steps = Parse(grammarId, text, root =>
        {
            var found = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var element in AllElements(root))
            {
                var parentIndent = LeadingIndent(text, element.StartIndex);
                if (parentIndent is null) continue;
                foreach (var child in XmlPath.ChildElements(element))
                {
                    if (LeadingIndent(text, child.StartIndex) is not { } childIndent) continue;
                    if (childIndent.Length <= parentIndent.Length || !childIndent.StartsWith(parentIndent, StringComparison.Ordinal))
                        continue;
                    var unit = childIndent[parentIndent.Length..];
                    found[unit] = found.GetValueOrDefault(unit) + 1;
                }
            }
            return found;
        });

        return steps is { Count: > 0 }
            ? steps.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.Length).First().Key
            : SourceText.Of(text).IndentUnit();
    }

    /// <summary>The indentation in front of <paramref name="offset"/> when only indentation is — null when
    /// something else shares the line before it.</summary>
    internal static string? LeadingIndent(string text, int offset)
    {
        var start = offset;
        while (start > 0 && text[start - 1] != '\n') start--;
        var lead = text[start..offset];
        return lead.All(c => c is ' ' or '\t') ? lead : null;
    }

    /// <summary>Everything about one element, as offsets.</summary>
    internal static XmlElement Describe(Node element, string text)
    {
        var tag      = CodeStructureExtractor.FirstChild(element, "STag", "EmptyElemTag")!;
        var nameNode = CodeStructureExtractor.FirstChild(tag, "Name");
        var self     = tag.Type == "EmptyElemTag";

        var attributes = new List<XmlAttribute>();
        foreach (var a in tag.NamedChildren)
        {
            if (a.Type != "Attribute") continue;
            var name  = CodeStructureExtractor.FirstChild(a, "Name");
            var value = CodeStructureExtractor.FirstChild(a, "AttValue");
            if (name is null || value is null || value.EndIndex - value.StartIndex < 2) continue;
            attributes.Add(new XmlAttribute(name.Text, a.StartIndex, a.EndIndex, value.StartIndex, value.EndIndex,
                                            text[value.StartIndex],
                                            XmlPath.Decode(text[(value.StartIndex + 1)..(value.EndIndex - 1)])));
        }

        var endTag     = CodeStructureExtractor.FirstChild(element, "ETag");
        var endTagName = endTag is null ? null : CodeStructureExtractor.FirstChild(endTag, "Name");
        var content    = CodeStructureExtractor.FirstChild(element, "content");

        int? first = null, last = null;
        var hasText = false;
        if (content is not null)
            foreach (var c in content.NamedChildren)
            {
                if (c.Type == "CharData" && string.IsNullOrWhiteSpace(c.Text)) continue;
                if (c.Type is "CharData" or "CDSect" or "EntityRef" or "CharRef") hasText = true;
                first ??= c.StartIndex;
                last = c.EndIndex;
            }

        return new XmlElement(
            QName: nameNode?.Text ?? "",
            Start: element.StartIndex, End: element.EndIndex, TriviaStart: TriviaStart(element, text),
            TagNameStart: nameNode?.StartIndex ?? tag.StartIndex + 1, TagNameEnd: nameNode?.EndIndex ?? tag.StartIndex + 1,
            TagEnd: tag.EndIndex, CloserStart: tag.EndIndex - (self ? 2 : 1), SelfClosing: self,
            EndTagStart: endTag?.StartIndex, EndTagNameStart: endTagName?.StartIndex, EndTagNameEnd: endTagName?.EndIndex,
            FirstContentStart: first, LastContentEnd: last, HasText: hasText,
            Attributes: attributes,
            DescendantElements: CountDescendants(element),
            IsRoot: element.Parent?.Type == "document",
            CanonicalPath: CanonicalPath(element),
            Line: element.StartPosition.Row + 1);
    }

    /// <summary>
    /// Where the comment that belongs to an element starts — the run of comments directly above it with no blank
    /// line in between — or the element's own start when there is none.
    /// </summary>
    private static int TriviaStart(Node element, string text)
    {
        var start = element.StartIndex;
        var previous = element.PreviousNamedSibling;
        if (previous?.Type == "prolog") previous = previous.LastNamedChild;

        while (previous is not null)
        {
            if (previous.Type == "CharData" && string.IsNullOrWhiteSpace(previous.Text))
            {
                previous = previous.PreviousNamedSibling;
                continue;
            }
            if (previous.Type != "Comment" || text[previous.EndIndex..start].Count(c => c == '\n') > 1) break;

            start = previous.StartIndex;
            previous = previous.PreviousNamedSibling;
        }
        return start;
    }

    private static int CountDescendants(Node element) =>
        XmlPath.ChildElements(element).Sum(c => 1 + CountDescendants(c));

    /// <summary><c>/Project/ItemGroup[2]/PackageReference[3]</c> — positions only where a name repeats.</summary>
    internal static string CanonicalPath(Node element)
    {
        var segments = new List<string>();
        for (var node = element; node is not null && node.Type == "element"; node = ParentElement(node))
        {
            var name = XmlPath.RawName(node);
            var parent = ParentElement(node);
            var siblings = parent is null ? [node] : XmlPath.ChildElements(parent).Where(s => XmlPath.RawName(s) == name).ToList();
            segments.Add(siblings.Count > 1
                ? $"{name}[{siblings.FindIndex(s => s.StartIndex == node.StartIndex) + 1}]"
                : name);
        }
        segments.Reverse();
        return "/" + string.Join('/', segments);
    }

    private static Node? ParentElement(Node node)
    {
        var parent = node.Parent;
        if (parent?.Type == "content") parent = parent.Parent;
        return parent?.Type == "element" ? parent : null;
    }

    /// <summary>Why a path matched nothing: how far it got, and what was there to match instead.</summary>
    private static string NoMatch(Node document, XmlPath path, XmlPath.Evaluation evaluation, string text)
    {
        var sb = new StringBuilder($"'{path.Text}' matches no element, so nothing was changed. ");
        var failing = path.Steps[evaluation.DeepestMatched + 1];
        IReadOnlyList<Node> contexts = evaluation.DeepestMatched < 0 ? [document] : evaluation.DeepestSet;

        if (evaluation.DeepestMatched >= 0)
        {
            var lines = contexts.Take(6).Select(e => e.StartPosition.Row + 1);
            sb.Append($"'{path.Prefix(evaluation.DeepestMatched)}' matches {contexts.Count} element(s) (line(s) "
                    + $"{string.Join(", ", lines)}{(contexts.Count > 6 ? ", …" : "")}); ");
        }

        // What the failing step could have matched, ignoring its predicates — which is almost always what differs.
        var named = contexts
            .SelectMany(c => failing.Axis == XmlPath.Axis.Child ? XmlPath.ChildElements(c) : Below(c))
            .Where(e => XmlPath.NameMatches(e, failing.Name))
            .ToList();

        if (named.Count > 0 && failing.Predicates.Count > 0)
        {
            sb.Append($"there {(named.Count == 1 ? "is" : "are")} {named.Count} <{failing.Name}> "
                    + $"{(failing.Axis == XmlPath.Axis.Child ? "under it" : "below it")}, and none passes its predicates: ");
            sb.Append(string.Join("; ", named.Take(8).Select(e =>
                $"line {e.StartPosition.Row + 1} {string.Join(' ', XmlPath.Attributes(e).Take(2).Select(a => $"{a.Name}=\"{a.Value}\""))}".TrimEnd())));
            if (named.Count > 8) sb.Append("; …");
            return sb.Append('.').ToString();
        }

        var children = contexts
            .SelectMany(c => XmlPath.ChildElements(c))
            .GroupBy(XmlPath.RawName)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => g.Count() == 1 ? g.Key : $"{g.Key} ×{g.Count()}")
            .ToList();

        sb.Append(evaluation.DeepestMatched < 0
            ? $"the root element is <{XmlPath.RawName(XmlPath.ChildElements(document).First())}>."
            : children.Count == 0
                ? "those have no child elements."
                : $"their children are: {string.Join(", ", children)}.");
        return sb.ToString();

        static IEnumerable<Node> Below(Node node)
        {
            foreach (var c in XmlPath.ChildElements(node))
            {
                yield return c;
                foreach (var d in Below(c)) yield return d;
            }
        }
    }

    /// <summary>Why several matches are refused, and the canonical path of each so the caller can pick one — plus an
    /// attribute whose value tells them apart, when one does.</summary>
    public static string Ambiguous(IReadOnlyList<XmlElement> elements, string path, bool allowsAll)
    {
        var sb = new StringBuilder($"'{path}' matches {elements.Count} elements, and this edit needs exactly one");
        sb.Append(allowsAll ? " (or --all)" : "").Append(':');
        foreach (var e in elements.Take(8))
            sb.Append($"\n  {e.CanonicalPath}  (line {e.Line}"
                    + (e.Attributes.Count > 0 ? $", {e.Attributes[0].Name}=\"{e.Attributes[0].Value}\"" : "") + ")");
        if (elements.Count > 8) sb.Append($"\n  … and {elements.Count - 8} more");

        var telling = elements[0].Attributes
            .Select(a => a.Name)
            .FirstOrDefault(name => elements.All(e => e.Attribute(name) is not null)
                                 && elements.Select(e => e.Attribute(name)!.Value).Distinct(StringComparer.Ordinal).Count() == elements.Count);
        if (telling is not null)
            sb.Append($"\nEach has a different {telling}, so [@{telling}='…'] picks one.");
        return sb.ToString();
    }
}
